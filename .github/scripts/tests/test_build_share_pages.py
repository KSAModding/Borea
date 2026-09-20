"""Tests for build_share_pages.py, offline against the fixture snapshot."""

from __future__ import annotations

import contextlib
import html.parser
import io
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
import urllib.error
from pathlib import Path
from unittest import mock

SCRIPTS = Path(__file__).resolve().parents[1]
REPOSITORY = SCRIPTS.parents[1]
sys.path.insert(0, str(SCRIPTS))

import build_share_pages as share  # noqa: E402
import listing_icons  # noqa: E402

FIXTURES = Path(__file__).resolve().parent / "fixtures" / "share-pages"
SITE = REPOSITORY / "site"
ICON = (FIXTURES / "icon.png").read_bytes()
ICON_NAME = "eda52096df4e16fedb674dd8b90bacd8b1c1077cc1eba673a2f16d8471290bbd.png"
ICON_URL = "https://example.org/afc/icon.png"


def snapshot() -> dict:
    return json.loads((FIXTURES / "snapshot.json").read_text(encoding="utf-8"))


def edge_snapshot() -> dict:
    """The fixture with the cases it does not have by itself."""
    document = snapshot()
    listings = {entry["id"]: entry for entry in document["listings"]}
    listings["StarMap"]["index_status"] = {"state": "quarantined"}
    listings["StarMap"]["authored"]["links"]["constructor"] = "https://example.org/constructor"
    listings["NoRelease"]["authored"]["type"] = ["mod-loader"]
    listings["OldMod"]["authored"]["superseded_by"] = "NewMod"
    listings["AdvancedFlightComputer"]["releases"][2]["game_min_revision"] = 9999
    for version in document["packs"][1]["versions"]:
        version["index_status"] = {"state": "retracted"}
    return document


class FakeFetch:
    """Serves the fixture icon at its URL and fails every other request."""

    def __init__(self, answers=None):
        self.answers = {ICON_URL: ICON} if answers is None else answers
        self.requests = []

    def __call__(self, url, cap):
        self.requests.append(url)
        if url not in self.answers:
            raise listing_icons.IconError(f"{url} answered HTTP 404")
        return self.answers[url]


class Tags(html.parser.HTMLParser):
    """Every start tag of a page with its attributes, and the text of the script elements that carry one."""

    def __init__(self):
        super().__init__()
        self.tags = []
        self.scripts = []
        self._script = None

    def handle_starttag(self, tag, attrs):
        self.tags.append((tag, dict(attrs)))
        if tag == "script":
            self._script = []

    def handle_data(self, data):
        if self._script is not None:
            self._script.append(data)

    def handle_endtag(self, tag):
        if tag == "script" and self._script is not None:
            text = "".join(self._script)
            if text:
                self.scripts.append(text)
            self._script = None


def parse(page: str) -> Tags:
    tags = Tags()
    tags.feed(page)
    return tags


class Nodes(html.parser.HTMLParser):
    """A fragment of HTML as elements and text, in the shape the fallback builds in the browser."""

    def __init__(self):
        super().__init__()
        self.stack = [{"tag": None, "attrs": {}, "children": []}]

    def handle_starttag(self, tag, attrs):
        node = {"tag": tag, "attrs": dict(attrs), "children": []}
        self.stack[-1]["children"].append(node)
        self.stack.append(node)

    def handle_endtag(self, tag):
        if len(self.stack) > 1:
            self.stack.pop()

    def handle_data(self, data):
        children = self.stack[-1]["children"]
        # The generator writes one block per line, and the browser has no line between two elements.
        if not data.strip() and self.stack[-1]["tag"] in (None, "ul", "ol", "figure"):
            return
        if children and isinstance(children[-1], str):
            children[-1] += data
        else:
            children.append(data)


def html_nodes(fragment: str) -> list:
    nodes = Nodes()
    nodes.feed(fragment)
    return nodes.stack[0]["children"]


class Build(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.out = Path(self.temp.name) / "out"
        self.cache = Path(self.temp.name) / "cache"
        self.fetch = FakeFetch()

    def build(self, document=None, cache=None):
        with contextlib.redirect_stderr(io.StringIO()) as errors:
            written = share.build(document or snapshot(), SITE, self.out, share.Icons(self.out, cache, self.fetch))
        self.log = errors.getvalue()
        return written

    def page(self, kind, identifier) -> str:
        return (self.out / kind / identifier / "index.html").read_text(encoding="utf-8")

    def prose(self, kind, identifier) -> str:
        """The description of a page, without the side panel that follows it."""
        page = self.page(kind, identifier)
        start = page.index('<div class="prose">')
        return page[start:page.index("</section>", start)]

    def panel_links(self, kind, identifier) -> str:
        """The Links list of the side panel, without the links a description holds."""
        page = self.page(kind, identifier)
        start = page.index('<ul class="links">')
        return page[start:page.index("</ul>", start)]

    def test_one_page_per_listing_and_pack_in_its_authored_casing(self):
        written = self.build()

        pages = sorted(path.parent.relative_to(self.out).as_posix() for path in self.out.glob("*/*/index.html"))
        self.assertEqual(
            ["mod/AdvancedFlightComputer", "mod/EvilMod", "mod/NoRelease", "mod/OldMod", "mod/StarMap",
             "pack/NavigationStarterPack"],
            pages)
        self.assertEqual(6, written)
        self.assertTrue((self.out / "index.html").is_file())
        self.assertTrue((self.out / "404.html").is_file())
        self.assertTrue((self.out / "styles.css").is_file())

    def test_tombstones_invalid_ids_and_repeated_casings_get_no_page(self):
        self.build()

        self.assertFalse((self.out / "mod" / "GoneMod").exists())
        self.assertFalse((self.out / "pack" / "GonePack").exists())
        self.assertFalse((self.out / "escape").exists())
        self.assertNotIn("Impostor", self.page("mod", "StarMap"))
        self.assertIn("the id repeats in another casing", self.log)

    def test_the_page_shows_the_listing(self):
        self.build()
        page = self.page("mod", "AdvancedFlightComputer")

        self.assertIn("<h1>Advanced Flight Computer</h1>", page)
        self.assertIn("by Maxi, cairn5", page)
        self.assertIn("Maneuver planning, burn execution and closed-loop guidance", page)
        self.assertIn("<dt>Latest version</dt><dd>0.7.5 &middot; <time datetime=\"2026-09-02T09:48:03Z\">2 September 2026</time></dd>", page)
        self.assertIn("<dt>Downloads</dt><dd>1,183</dd>", page)
        self.assertIn("<dt>License</dt><dd>MIT</dd>", page)
        self.assertIn("<dt>Type</dt><dd>Mod</dd>", page)

    def test_the_side_panel_shows_the_compatibility_above_the_links(self):
        self.build()
        page = self.page("mod", "AdvancedFlightComputer")
        panel = page[page.index('<aside class="panel">'):]

        self.assertIn('<li class="chip">2026.9.4.5400 to 2026.9.7.5402</li>', panel)
        self.assertLess(panel.index("<h2>Compatibility</h2>"), panel.index("<h2>Links</h2>"))
        self.assertLess(panel.index("<h2>Links</h2>"), panel.index("<h2>Details</h2>"))

    def test_the_header_names_the_curated_tags_first_and_keeps_the_other_tags(self):
        self.build()
        header = self.page("mod", "AdvancedFlightComputer").split('<div class="listing-body">')[0]

        self.assertIn('<li class="chip">Gameplay</li>\n          <li class="chip">User Interface</li>\n'
                      '          <li class="chip">navigation</li>', header)

    def test_the_description_is_markdown(self):
        self.build()
        page = self.page("mod", "AdvancedFlightComputer")

        self.assertIn("<h4>Features</h4>", page)
        self.assertIn("Plans <strong>maneuvers</strong> and flies them, with <em>quick tools</em>", page)
        self.assertIn("<li>Flyby targeting and multi-pass burns</li>", page)
        self.assertIn("<ol>\n            <li>Plan the burn</li>", page)
        self.assertIn('<a href="https://example.org/afc/manual" rel="nofollow noopener">manual</a>', page)
        self.assertIn("never a rogue one", page)
        self.assertIn("keep snake_case_names and \u00fcber_wert_ whole", page)
        self.assertIn('<a href="https://example.org/afc/demo" rel="nofollow noopener">Watch the demo</a>', page)
        self.assertIn("<pre><code>[staging]\ndelay = 2.0</code></pre>", page)

    def test_an_image_with_a_record_carries_it_and_keeps_room_for_the_image(self):
        self.build()
        prose = self.prose("mod", "AdvancedFlightComputer")

        self.assertIn(
            '<figure data-image="https://example.org/afc/planner.png" '
            'data-sha256="1111111111111111111111111111111111111111111111111111111111111111" '
            'data-width="1600" data-height="900" data-size="402117">\n'
            '            <div class="frame" style="aspect-ratio: 1600 / 900; '
            'max-width: min(1600px, calc(var(--shot-height) * 1600 / 900))"></div>\n'
            "            <figcaption>The planner window</figcaption>\n"
            "          </figure>", prose)
        self.assertNotIn("<img", prose)

    def test_a_linked_image_keeps_its_link_on_the_frame_and_on_the_caption(self):
        self.build()
        prose = self.prose("mod", "AdvancedFlightComputer")

        self.assertIn('<a class="frame" style="aspect-ratio: 800 / 1600; '
                      'max-width: min(800px, calc(var(--shot-height) * 800 / 1600))" href="https://example.org/afc/demo" '
                      'rel="nofollow noopener" tabindex="-1" aria-hidden="true"></a>', prose)
        self.assertIn('<figcaption><a href="https://example.org/afc/demo" rel="nofollow noopener">Watch the demo</a>'
                      '<span class="credit">Artwork by cairn5 &middot; '
                      '<a href="https://example.org/afc/source" rel="nofollow noopener">Source</a></span></figcaption>',
                      prose)

    def test_an_image_without_a_usable_record_stands_as_its_caption(self):
        self.build()

        self.assertIn('<p class="figure">The burn editor</p>', self.prose("mod", "AdvancedFlightComputer"))
        self.assertIn('<p class="figure">Not served over https</p>', self.prose("mod", "EvilMod"))

    def test_an_image_with_nothing_to_say_keeps_a_caption_for_the_reader_who_gets_no_image(self):
        images = {"plain": {"id": "plain", "url": "https://example.org/plain.png", "sha256": "3" * 64,
                            "width": 40, "height": 30, "size": 900, "attribution": None, "source": None}}

        self.assertIn('<figcaption class="untitled">Image</figcaption>',
                      share.markdown_html("![](ksa-image:plain)", "", images))
        self.assertEqual('<p class="figure">Image</p>\n', share.markdown_html("![](ksa-image:gone)", "", images))
        self.assertEqual("", share.markdown_html("![](https://example.org/other.png)", "", images))

    def test_a_record_the_page_may_not_fetch_or_lay_out_is_left_out(self):
        self.build()

        self.assertNotIn("second-planner", self.page("mod", "AdvancedFlightComputer"))
        self.assertNotIn("toobig", self.page("mod", "AdvancedFlightComputer"))
        self.assertNotIn("plain.png", self.page("mod", "EvilMod"))

    def test_the_record_of_an_image_is_escaped_in_its_attributes_and_its_credit(self):
        self.build()
        prose = self.prose("mod", "EvilMod")

        self.assertIn('data-image="https://example.org/evil/a&quot;onmouseover=&quot;alert(1).png"', prose)
        self.assertIn("By &lt;b&gt;nobody&lt;/b&gt; &quot; onmouseover=&quot;alert(1)", prose)
        for _, attrs in parse(prose).tags:
            self.assertNotIn("onmouseover", attrs)

    def test_the_page_loads_the_script_that_shows_the_description_images(self):
        self.build()
        page = self.page("mod", "AdvancedFlightComputer")

        # The script is not deferred, because it reads the switch of the reader before the body is parsed.
        self.assertIn('<script src="../../description-images.js"></script>', page)
        self.assertTrue((self.out / "description-images.js").is_file())
        self.assertIn('<section class="description">', page)

    def test_a_page_with_an_image_carries_the_switch_above_the_description(self):
        self.build()
        page = self.page("mod", "AdvancedFlightComputer")

        self.assertIn('<p class="image-switch">\n'
                      '          <label><input type="checkbox"> Show images from author hosts</label>',
                      page)
        self.assertLess(page.index('class="image-switch"'), page.index('<div class="prose">'))
        self.assertNotIn("image-switch", self.page("mod", "NoRelease"))

    def test_the_switch_is_off_until_the_reader_asks(self):
        self.build()
        page = self.page("mod", "AdvancedFlightComputer")

        self.assertNotIn('type="checkbox" checked', page)

    def test_a_listing_without_a_description_says_so(self):
        self.build()

        self.assertIn('<p class="meta">No description provided.</p>', self.page("mod", "NoRelease"))

    def test_the_buttons_link_to_borea_and_the_landing_page(self):
        self.build()
        hrefs = {attrs.get("href") for tag, attrs in parse(self.page("mod", "AdvancedFlightComputer")).tags if tag == "a"}

        self.assertIn("borea://mod/AdvancedFlightComputer", hrefs)
        self.assertIn("borea://install/AdvancedFlightComputer", hrefs)
        self.assertIn("../../#download", hrefs)
        self.assertIn("need Borea on this computer", self.page("mod", "AdvancedFlightComputer"))

    def test_the_page_does_not_redirect_by_itself(self):
        self.build()
        page = self.page("mod", "AdvancedFlightComputer")

        self.assertNotIn("http-equiv", page)
        self.assertEqual([None, "application/ld+json"],
                         [attrs.get("type") for tag, attrs in parse(page).tags if tag == "script"])

    def test_links_keep_the_app_order_and_only_http_and_https(self):
        self.build()
        links = [attrs["href"] for tag, attrs in parse(self.panel_links("mod", "AdvancedFlightComputer")).tags
                 if tag == "a"]

        self.assertEqual(
            ["https://forums.ahwoo.com/threads/advanced-flight-computer.783/",
             "https://github.com/Maximilian-Nesslauer/KSA-AdvancedFlightComputer",
             "https://example.org/afc/wiki"],
            links)
        self.assertIn(">Wiki</a>", self.page("mod", "AdvancedFlightComputer"))

    def test_opengraph_and_twitter_cards_name_the_share_url_and_the_verified_icon(self):
        self.build()
        metas = {attrs.get("property") or attrs.get("name"): attrs.get("content")
                 for tag, attrs in parse(self.page("mod", "AdvancedFlightComputer")).tags if tag == "meta"}

        self.assertEqual("https://ksamodding.github.io/Borea/mod/AdvancedFlightComputer/", metas["og:url"])
        self.assertEqual("Advanced Flight Computer", metas["og:title"])
        self.assertTrue(metas["og:description"].startswith("Maneuver planning"))
        self.assertEqual(f"https://ksamodding.github.io/Borea/img/icons/{ICON_NAME}", metas["og:image"])
        self.assertEqual(metas["og:image"], metas["twitter:image"])
        self.assertEqual("summary", metas["twitter:card"])

    def test_a_verified_icon_is_served_from_the_site(self):
        self.build()

        self.assertEqual(ICON, (self.out / "img" / "icons" / ICON_NAME).read_bytes())
        images = [attrs for tag, attrs in parse(self.page("mod", "AdvancedFlightComputer")).tags if tag == "img"]
        self.assertIn({"src": f"../../img/icons/{ICON_NAME}", "width": "256", "height": "256", "alt": ""}, images)

    def test_an_icon_that_fails_leaves_the_placeholder_and_the_build_goes_on(self):
        self.fetch = FakeFetch({ICON_URL: ICON[:-1]})
        written = self.build()

        self.assertEqual(6, written)
        page = self.page("mod", "AdvancedFlightComputer")
        self.assertIn('class="listing-icon placeholder"', page)
        self.assertIn('content="https://ksamodding.github.io/Borea/img/icon.png"', page)
        self.assertFalse((self.out / "img" / "icons").exists())
        self.assertIn("mod AdvancedFlightComputer: the icon is not used", self.log)

    def test_an_unexpected_icon_error_leaves_the_placeholder_and_the_build_goes_on(self):
        self.fetch = mock.Mock(side_effect=ValueError("Invalid IPv6 URL"))
        written = self.build()

        self.assertEqual(6, written)
        self.assertIn('class="listing-icon placeholder"', self.page("mod", "AdvancedFlightComputer"))
        self.assertIn("because of an unexpected error", self.log)

    def test_a_description_cannot_bring_markup_of_its_own(self):
        self.build()
        page = self.page("mod", "EvilMod")
        prose = page[page.index('<div class="prose">'):]

        self.assertNotIn("<script>alert", page)
        self.assertNotIn("<img src=x", page)
        self.assertNotIn("<b>bold</b>", page)
        self.assertIn("&lt;img src=x onerror=alert(1)&gt;", prose)
        self.assertIn("<code>&lt;script&gt;</code>", prose)
        self.assertIn(">A link, an ", prose)
        self.assertIn('<li class="chip">&lt;b&gt;bold&lt;/b&gt;</li>', page)

    def test_author_text_is_escaped_in_text_attributes_and_script(self):
        self.build()
        page = self.page("mod", "EvilMod")
        tags = parse(page)

        self.assertNotIn("<script>alert", page)
        self.assertNotIn("<img src=x", page)
        self.assertNotIn("<b>", page)
        self.assertEqual(1, len(tags.scripts))
        self.assertNotIn("<", tags.scripts[0])
        data = json.loads(tags.scripts[0])
        self.assertEqual('Evil </script><script>alert(1)</script> "Mod"', data["name"])
        for tag, attrs in tags.tags:
            self.assertNotIn("onmouseover", attrs)
            self.assertNotIn("onerror", attrs)
        metas = {attrs.get("property"): attrs.get("content") for tag, attrs in tags.tags if tag == "meta"}
        self.assertEqual('Evil </script><script>alert(1)</script> "Mod"', metas["og:title"])
        self.assertEqual("Breaks <out> & \" onmouseover=\"alert(1) '", metas["og:description"])

    def test_a_link_with_a_quote_or_a_space_is_escaped_or_left_out(self):
        self.build()
        links = [attrs["href"] for tag, attrs in parse(self.panel_links("mod", "EvilMod")).tags
                 if tag == "a"]

        self.assertEqual(['https://example.org/a"onmouseover="alert(1)'], links)

    def test_a_disputed_listing_shows_a_warning_with_the_reason(self):
        self.build()

        self.assertIn('<p class="notice" role="note">The index marks this as disputed. Ownership is &lt;b&gt;unclear&lt;/b&gt;.</p>',
                      self.page("mod", "EvilMod"))

    def test_an_unknown_index_state_shows_as_a_warning(self):
        self.build(edge_snapshot())

        self.assertIn('<p class="notice" role="note">The index marks this as quarantined.</p>', self.page("mod", "StarMap"))

    def test_a_link_key_that_names_an_object_member_is_a_plain_label(self):
        self.build(edge_snapshot())

        self.assertIn(">Constructor</a>", self.page("mod", "StarMap"))

    def test_a_type_that_is_not_text_shows_as_a_mod(self):
        self.build(edge_snapshot())

        self.assertIn("<dt>Type</dt><dd>Mod</dd>", self.page("mod", "NoRelease"))

    def test_a_successor_that_is_not_in_the_index_is_plain_text(self):
        self.build(edge_snapshot())
        page = self.page("mod", "OldMod")

        self.assertIn('<p class="notice" role="note">Deprecated by its author. Its successor is NewMod.</p>', page)
        self.assertNotIn('<a href="../', page)

    def test_a_revision_that_is_not_in_the_game_versions_shows_the_release_text(self):
        self.build(edge_snapshot())

        self.assertIn('<li class="chip">2026.9 to 2026.9.7.5402</li>', self.page("mod", "AdvancedFlightComputer"))

    def test_a_pack_with_every_version_retracted_says_so(self):
        self.build(edge_snapshot())
        page = self.page("pack", "NavigationStarterPack")

        self.assertIn("<h1>Navigation Starter Pack (broken)</h1>", page)
        self.assertIn('<p class="notice" role="note">Every version of this pack is retracted.</p>', page)

    def test_a_deprecated_listing_names_its_successor(self):
        self.build()
        page = self.page("mod", "OldMod")

        self.assertIn('Deprecated by its author. Its successor is <a href="../AdvancedFlightComputer/">Advanced Flight Computer</a>.', page)
        self.assertIn('1.0.0 <span class="channel">testing</span>', page)

    def test_a_listing_without_a_release_shows_its_authored_compatibility(self):
        self.build()
        page = self.page("mod", "NoRelease")

        self.assertIn("<dt>Latest version</dt><dd>No release yet</dd>", page)
        self.assertIn('<li class="chip">2026.8 or newer</li>', page)
        self.assertNotIn("<dt>Downloads</dt>", page)

    def test_a_mod_loader_page_says_so(self):
        self.build()
        page = self.page("mod", "StarMap")

        self.assertIn('<p class="meta kind">Mod loader</p>', page)
        self.assertIn('<li class="chip">2026.8.3.5117 or newer</li>', page)

    def test_a_pack_page_shows_its_newest_version_that_is_not_retracted(self):
        self.build()
        page = self.page("pack", "NavigationStarterPack")

        self.assertIn("<h1>Navigation Starter Pack</h1>", page)
        self.assertIn('<li class="chip">Gameplay</li>', page)
        self.assertIn("<p>Two mods that <strong>work together</strong>.</p>", page)
        self.assertIn("<dt>Mods</dt><dd>2</dd>", page)
        self.assertIn(">5 August 2026</time>", page)
        self.assertIn("borea://pack/NavigationStarterPack", page)
        self.assertIn("borea://install/NavigationStarterPack", page)
        self.assertIn('content="https://ksamodding.github.io/Borea/pack/NavigationStarterPack/"', page)

    def test_an_unknown_snapshot_version_fails_and_writes_nothing(self):
        document = snapshot()
        document["snapshot_version"] = 2

        with self.assertRaises(ValueError):
            self.build(document)
        self.assertFalse(self.out.exists())

    def test_a_snapshot_without_listings_fails(self):
        with self.assertRaises(ValueError):
            self.build({"snapshot_version": 1, "packs": []})

    def test_a_cached_icon_is_used_without_a_fetch_and_unused_ones_are_pruned(self):
        self.cache.mkdir()
        (self.cache / ICON_NAME).write_bytes(ICON)
        (self.cache / ("f" * 64 + ".png")).write_bytes(b"old")
        self.fetch = FakeFetch({})

        self.build(cache=self.cache)

        self.assertNotIn(ICON_URL, self.fetch.requests)
        self.assertEqual(ICON, (self.out / "img" / "icons" / ICON_NAME).read_bytes())
        self.assertEqual([ICON_NAME], sorted(path.name for path in self.cache.iterdir()))

    def test_a_cached_icon_that_no_longer_matches_is_fetched_again(self):
        self.cache.mkdir()
        (self.cache / ICON_NAME).write_bytes(b"changed")

        self.build(cache=self.cache)

        self.assertEqual(1, self.fetch.requests.count(ICON_URL))
        self.assertEqual(ICON, (self.cache / ICON_NAME).read_bytes())

    def test_a_record_with_a_digest_that_is_not_hex_is_not_fetched(self):
        document = snapshot()
        document["listings"][1]["authored"]["images"]["icon"]["sha256"] = "../../index"

        self.build(document)

        self.assertNotIn(ICON_URL, self.fetch.requests)
        self.assertIn("the icon record is incomplete", self.log)


NODE_BLOCKS = """
const path = require("path");
const share = require(path.resolve(process.argv[1]));
const samples = JSON.parse(process.argv[2]);
process.stdout.write(JSON.stringify(samples.map((sample) => share.markdownBlocks(sample))));
"""

MARKDOWN_SAMPLES = [
    "\u00fcber_wert_ stays whole, and `x`_y_ does not",
    "## Features\n\nPlans **maneuvers** with _quick tools_ and `code`.",
    "- one\n- two\n\n1. first\n2. second",
    "Read the [manual](https://example.org/a_(b)), never a [rogue](javascript:alert(1)) one.",
    "![The planner](https://example.org/i.png) and ![](https://example.org/j.png)",
    "```\n[staging]\ndelay = 2.0\n```\n\nafter the code",
    "snake_case_name stays, __strong__ and *thin* do not, a_b_c stays",
    "###### deep\n\n####### not a heading",
    "**a *b* c** and [**bold link**](https://example.org/)",
    "[![Watch the demo](ksa-image:demo)](https://example.org/v) and ![](ksa-image:none)",
    "![A shot](ksa-image:plain)\n\n![](ksa-image:demo)\n\n# [![in a heading](ksa-image:plain)](https://example.org/h)",
    "![](ksa-image:plain)\n\nan image with nothing to say",
]

MARKDOWN_IMAGES = {
    "demo": {"id": "demo", "url": "https://example.org/demo.png", "sha256": "2" * 64, "width": 800, "height": 600,
             "size": 1000, "attribution": "Artwork by an artist", "source": "https://example.org/source"},
    "plain": {"id": "plain", "url": "https://example.org/plain.png", "sha256": "3" * 64, "width": 1200, "height": 900,
              "size": 2000, "attribution": None, "source": None},
}


NODE_NODES = """
const path = require("path");
const share = require(path.resolve(process.argv[1]));

function Node(tag) {
  this.tag = tag;
  this.attrs = {};
  this.children = [];
}

Object.defineProperty(Node.prototype, "className", { set: function (value) { this.attrs.class = value; } });
Object.defineProperty(Node.prototype, "textContent", { set: function (value) { this.children = [{ text: String(value) }]; } });
Object.defineProperty(Node.prototype, "href", { set: function (value) { this.attrs.href = value; } });
Object.defineProperty(Node.prototype, "rel", { set: function (value) { this.attrs.rel = value; } });
Object.defineProperty(Node.prototype, "childNodes", { get: function () { return this.children; } });

Node.prototype.setAttribute = function (name, value) { this.attrs[name] = value; };

Node.prototype.appendChild = function (child) {
  if (child instanceof Node && child.tag === null) {
    child.children.forEach(function (one) { this.children.push(one); }, this);
    child.children = [];
    return;
  }
  this.children.push(child);
};

global.document = {
  createElement: function (tag) { return new Node(tag); },
  createDocumentFragment: function () { return new Node(null); },
  createTextNode: function (text) { return { text: text }; }
};

function plain(node) {
  if (node.tag === undefined) {
    return node.text;
  }
  const children = [];
  node.children.forEach(function (child) {
    const value = plain(child);
    if (typeof value === "string" && typeof children[children.length - 1] === "string") {
      children[children.length - 1] += value;
      return;
    }
    children.push(value);
  });
  return { tag: node.tag, attrs: node.attrs, children: children };
}

const samples = JSON.parse(process.argv[2]);
const images = JSON.parse(process.argv[3]);
process.stdout.write(JSON.stringify(samples.map(function (sample) {
  return plain(share.markdownNodes(sample, images)).children;
})));
"""

NODE_VIEWS = """
const fs = require("fs");
const path = require("path");
const share = require(path.resolve(process.argv[1]));
const snapshot = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
const views = { mod: {}, pack: {} };
for (const entry of snapshot.listings) {
  if (entry && entry.authored && typeof entry.authored === "object") {
    views.mod[entry.id] = share.listingView(entry, snapshot);
  }
}
for (const entry of snapshot.packs) {
  const view = share.packView(entry, snapshot);
  if (view) {
    views.pack[entry.id] = view;
  }
}
for (const kind of ["mod", "pack"]) {
  for (const view of Object.values(views[kind])) {
    view.links = view.links.map((link) => [link.label, link.url.hostname]);
    view.notices = view.notices.map((notice) => [notice.text, notice.link ? notice.link.label : null]);
  }
}
process.stdout.write(JSON.stringify(views));
"""


@unittest.skipUnless(shutil.which("node"), "Node is not installed")
class FallbackParity(unittest.TestCase):
    """site/share.js builds the pages of the 404 fallback and must show what the generator shows."""

    def views(self, document) -> dict:
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "snapshot.json"
            path.write_text(json.dumps(document), encoding="utf-8")
            result = subprocess.run(["node", "-e", NODE_VIEWS, str(SITE / "share.js"), str(path)],
                                    capture_output=True, text=True, encoding="utf-8", check=True)
        return json.loads(result.stdout)

    def nodes(self, samples) -> list:
        result = subprocess.run(["node", "-e", NODE_NODES, str(SITE / "share.js"), json.dumps(samples),
                                 json.dumps(MARKDOWN_IMAGES)],
                                capture_output=True, text=True, encoding="utf-8", check=True)
        return json.loads(result.stdout)

    def blocks(self, samples) -> list:
        result = subprocess.run(["node", "-e", NODE_BLOCKS, str(SITE / "share.js"), json.dumps(samples)],
                                capture_output=True, text=True, encoding="utf-8", check=True)
        return json.loads(result.stdout)

    def test_the_fallback_reads_a_description_the_way_the_generator_reads_it(self):
        for sample, blocks in zip(MARKDOWN_SAMPLES, self.blocks(MARKDOWN_SAMPLES)):
            with self.subTest(sample=sample):
                self.assertEqual(share.markdown_blocks(sample), blocks)

    def test_the_fallback_shows_a_description_the_way_the_generator_shows_it(self):
        for sample, nodes in zip(MARKDOWN_SAMPLES, self.nodes(MARKDOWN_SAMPLES)):
            with self.subTest(sample=sample):
                self.assertEqual(html_nodes(share.markdown_html(sample, "", MARKDOWN_IMAGES)), nodes)

    def test_the_fallback_shows_what_the_generator_shows(self):
        for name, document in (("fixture", snapshot()), ("edge cases", edge_snapshot())):
            views = self.views(document)
            with contextlib.redirect_stderr(io.StringIO()):
                pages = share.pages_of(share.Snapshot(document))
            self.assertEqual(6, len(pages))
            for page, _ in pages:
                with self.subTest(snapshot=name, page=f"{page.kind}/{page.id}"):
                    expected = {
                        "name": page.name, "type": page.type_label, "authors": page.authors, "abstract": page.abstract,
                        "license": page.license, "version": page.version, "channel": page.channel,
                        "date": share.date_text(page.date) if page.date else None, "game": page.game,
                        "downloads": page.downloads, "modCount": page.mod_count, "tags": page.tags,
                        "description": page.description, "images": page.images,
                        "links": [[label, share.urllib.parse.urlsplit(url).hostname] for label, url in page.links],
                        "notices": [[notice.text, notice.link[0] if notice.link else None] for notice in page.notices],
                    }
                    view = views[page.kind][page.id]
                    self.assertEqual(expected, {key: view[key] for key in expected})


class Values(unittest.TestCase):
    def test_web_url_accepts_only_absolute_http_and_https(self):
        self.assertEqual("https://example.org/a?b=c&d", share.web_url("https://example.org/a?b=c&d"))
        self.assertEqual("http://example.org/", share.web_url("http://example.org/"))
        for value in ("javascript:alert(1)", "JAVASCRIPT:alert(1)", "data:text/html,x", "//example.org/", "/relative",
                      "https://", " https://example.org/", "https://example.org/a b", "https://example.org/\n", None, 5):
            with self.subTest(value=value):
                self.assertIsNone(share.web_url(value))

    def test_script_json_cannot_end_the_script_element(self):
        value = {"name": "</script><!-- & " + chr(0x2028) + chr(0x2029)}
        encoded = share.script_json(value)

        self.assertNotIn("<", encoded)
        self.assertNotIn(">", encoded)
        self.assertNotIn("&", encoded)
        self.assertTrue(encoded.isascii())
        self.assertEqual(value, json.loads(encoded))

    def test_ids_follow_the_content_id_rules(self):
        for value in ("StarMap", "KSP-Redux", "a", "Mod.Name_2"):
            self.assertTrue(share.valid_id(value), value)
        for value in ("..", "../x", "a/b", "-a", "a-", "", "CON", "com1.x", "a" * 65, None, 5):
            self.assertFalse(share.valid_id(value), value)

    def test_a_record_that_ends_in_a_newline_is_no_record(self):
        """A browser ends a pattern at the end of the value, so the generator must end it there as well."""
        record = {"id": "shot", "url": "https://example.org/a.png", "sha256": "a" * 64, "width": 10, "height": 10,
                  "size": 100}

        self.assertIsNotNone(share.image_record(record))
        self.assertIsNone(share.image_record({**record, "sha256": "a" * 64 + "\n"}))
        self.assertIsNone(share.image_record({**record, "id": "shot\n"}))
        self.assertIsNone(share.image_reference("ksa-image:shot\n"))

    def test_text_collapses_whitespace_and_control_characters(self):
        self.assertEqual("a b c", share.text(" a\n\tb \x00 c "))
        self.assertIsNone(share.text("  "))
        self.assertIsNone(share.text(["a"]))


class LoadSnapshot(unittest.TestCase):
    def test_a_local_file_is_read(self):
        self.assertEqual(1, share.load_snapshot(str(FIXTURES / "snapshot.json"))["snapshot_version"])

    def test_a_fetch_that_keeps_failing_raises(self):
        with mock.patch.object(share.urllib.request, "urlopen", side_effect=urllib.error.URLError("down")) as urlopen:
            with self.assertRaises(RuntimeError):
                share.load_snapshot("https://example.org/index.json", attempts=2, wait=0)
        self.assertEqual(2, urlopen.call_count)

    def test_a_fetch_is_tried_again(self):
        response = mock.MagicMock()
        response.__enter__.return_value.read.return_value = b'{"snapshot_version": 1}'
        with mock.patch.object(share.urllib.request, "urlopen", side_effect=[urllib.error.URLError("blip"), response]):
            self.assertEqual({"snapshot_version": 1}, share.load_snapshot("https://example.org/index.json", wait=0))


class Main(unittest.TestCase):
    def test_the_command_line_builds_from_a_local_snapshot(self):
        with tempfile.TemporaryDirectory() as temp:
            out = Path(temp) / "out"
            with mock.patch.object(listing_icons, "fetch", side_effect=listing_icons.IconError("offline")), \
                    contextlib.redirect_stdout(io.StringIO()) as output, contextlib.redirect_stderr(io.StringIO()):
                code = share.main(["--site", str(SITE), "--out", str(out), "--snapshot", str(FIXTURES / "snapshot.json")])

            self.assertEqual(0, code)
            self.assertIn("wrote 6 pages", output.getvalue())
            self.assertTrue((out / "pack" / "NavigationStarterPack" / "index.html").is_file())


if __name__ == "__main__":
    unittest.main()
