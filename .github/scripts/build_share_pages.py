#!/usr/bin/env python3
"""Builds the landing site with one share page per listing of the content index.

The output is a copy of site/ plus mod/<id>/index.html for every mod and mod loader and
pack/<id>/index.html for every pack, id in its authored casing.
Authors write most of the snapshot, so every value is checked before use and escaped when written.
Icons are fetched, verified against their records and served from the site itself.
"""

from __future__ import annotations

import argparse
import html
import json
import re
import shutil
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import listing_icons  # noqa: E402

SNAPSHOT_URL = "https://ksamodding.github.io/content-index-releases/v1/index.json"
SITE_URL = "https://ksamodding.github.io/Borea/"
SNAPSHOT_VERSION = 1
FETCH_ATTEMPTS = 3

# ModIds.IsValid in Borea.Core.
ID_PATTERN = re.compile(r"^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$")
RESERVED_IDS = {"core", "con", "prn", "aux", "nul"} | {f"{name}{digit}" for name in ("com", "lpt") for digit in range(1, 10)}
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
CONTROL = re.compile(r"[\x00-\x1f\x7f]")
# The same, but a description keeps its line breaks and its tabs.
BLOCK_CONTROL = re.compile(r"[\x00-\x08\x0b-\x1f\x7f]")

TYPE_LABELS = {"mod": "Mod", "mod-loader": "Mod loader", "modpack": "Modpack"}
# The order and names of the links on the content page of the App.
LINK_LABELS = {
    "forums": "Forum",
    "repository": "Repository",
    "spacedock": "SpaceDock",
    "bugtracker": "Bug tracker",
    "homepage": "Homepage",
    "discussions": "Discussions",
}
MONTHS = ("January", "February", "March", "April", "May", "June", "July", "August", "September", "October",
          "November", "December")
DESCRIPTION_LIMIT = 300
# ReleaseChannels in Borea.Core: stable offers stable releases, testing adds testing ones, and dev offers every status.
CHANNELS = {"stable": 0, "testing": 1}
DEV_CHANNEL = 2

# The blocks and the inline spans of a description, a narrower subset than the description view of the App reads,
# because a reference-style link and a raw HTML image stay the text the author wrote.
HEADING_PATTERN = re.compile(r"^(#{1,6})\s+(.*)$")
ITEM_PATTERN = re.compile(r"^\s*([-*+]|\d+\.)\s+(.*)$")
IMAGE = r"!\[(?P<alt>[^\]]*)\]\((?P<target>(?:[^\s()]|\([^\s()]*\))*)(?:\s+[^)]*)?\)"
# The same image, as the whole label of a link, so a linked image becomes the image and keeps its link.
IMAGE_ONLY = re.compile(IMAGE)
# An image inside the label of a link, without the capture groups an image of its own has.
NESTED_IMAGE = r"!\[[^\]]*\]\((?:[^\s()]|\([^\s()]*\))*(?:\s+[^)]*)?\)"
# An underscore opens emphasis only outside a word, the way the App reads it, so a snake_case_name stays whole.
INLINE_PATTERN = re.compile(
    IMAGE
    + r"|`(?P<code>[^`]+)`"
    + rf"|\[(?P<label>(?:{NESTED_IMAGE}|[^\]])*)\]\((?P<url>(?:[^\s()]|\([^\s()]*\))*)(?:\s+[^)]*)?\)"
    + r"|\*\*(?P<strong>[^*]+)\*\*"
    + r"|(?<!\w)__(?P<strong_score>[^_]+)__(?!\w)"
    + r"|\*(?P<emphasis>[^*]+)\*"
    + r"|(?<!\w)_(?P<emphasis_score>[^_]+)_(?!\w)")
# A heading of a description sits below the name of the listing and the heading of its section.
HEADING_OFFSET = 2
# Emphasis inside emphasis stops here, so a pathological description cannot recurse without an end.
SPAN_DEPTH = 3

# RFC 0058: a description shows the images of its own records, which it names as ksa-image:<id>, and no other image.
IMAGE_REFERENCE = "ksa-image:"
IMAGE_ID = re.compile(r"^[A-Za-z0-9](?:[A-Za-z0-9_-]{0,62}[A-Za-z0-9])?$")
IMAGE_PIXELS = 2048
IMAGE_CAP = 1024 * 1024
# What an image with no words of its own is called, so that a reader always sees a line where an image is.
IMAGE_PLACEHOLDER = "Image"
# The line under the Borea buttons, and the line that takes their place on a phone or a tablet.
DESKTOP_HINT = "Open in Borea and Install with Borea need Borea on this computer."
HANDHELD_HINT = "Borea runs on Windows, Linux and macOS. To install this with Borea, open this page on a computer."
# RFC 0058 asks a client to offer a reader a way to load no image from the host of an author. A share page goes
# further and loads none until the reader asks, because a reader arrives here from a link and never chose to
# tell a host their address. The page carries the switch, so that it stands in its place from the first paint,
# and the style sheet shows it only when the script that drives it runs. The script reads the answer of the
# reader and sets the box.
IMAGE_SWITCH = """        <p class="image-switch">
          <label><input type="checkbox"> Show images from author hosts</label>
          <span class="meta">Every image comes from the host of its author, which then learns your address.</span>
        </p>
"""


@dataclass
class Notice:
    text: str
    link: tuple[str, str] | None = None


@dataclass
class Page:
    kind: str
    id: str
    name: str
    type_label: str
    authors: list[str]
    abstract: str | None
    description: str | None
    license: str | None
    version: str | None
    channel: str | None
    date: datetime | None
    game: str | None
    downloads: int | None
    mod_count: int | None
    tags: list[str] = field(default_factory=list)
    # The member lines a pack thread lists by forum rule 4.3, and the members with a newer release. None on a mod page.
    forum_list: list[str] | None = None
    newer: list[str] = field(default_factory=list)
    links: list[tuple[str, str]] = field(default_factory=list)
    notices: list[Notice] = field(default_factory=list)
    images: dict[str, dict] = field(default_factory=dict)
    icon: tuple[str, int, int] | None = None


def valid_id(value) -> bool:
    return isinstance(value, str) and bool(ID_PATTERN.match(value)) and value.split(".")[0].lower() not in RESERVED_IDS


def text(value) -> str | None:
    """A trimmed string with its whitespace collapsed, or None for anything else."""
    if not isinstance(value, str):
        return None
    collapsed = " ".join(CONTROL.sub(" ", value).split())
    return collapsed or None


def rich_text(value) -> str | None:
    """A trimmed string that keeps its line breaks, for a description, or None for anything else."""
    if not isinstance(value, str):
        return None
    cleaned = BLOCK_CONTROL.sub(" ", value.replace("\r\n", "\n").replace("\r", "\n")).strip()
    return cleaned or None


def web_url(value) -> str | None:
    """`value` when it is an absolute http or https URL, else None."""
    if not isinstance(value, str) or CONTROL.search(value) or any(character.isspace() for character in value):
        return None
    try:
        parts = urllib.parse.urlsplit(value)
    except ValueError:
        return None
    if parts.scheme.lower() not in ("http", "https") or not parts.hostname:
        return None
    return value


def https_url(value) -> str | None:
    """`value` when it is an absolute https URL, else None. An image and its source are https only."""
    url = web_url(value)
    return url if url is not None and url.split(":", 1)[0].lower() == "https" else None


def count(value) -> int | None:
    return value if isinstance(value, int) and not isinstance(value, bool) and value >= 0 else None


def timestamp(value) -> datetime | None:
    if not isinstance(value, str):
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed.astimezone(timezone.utc) if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)


def date_text(value: datetime) -> str:
    return f"{value.day} {MONTHS[value.month - 1]} {value.year}"


def state_of(entry) -> str | None:
    status = entry.get("index_status")
    return status.get("state") if isinstance(status, dict) and isinstance(status.get("state"), str) else None


def status_notice(entry) -> Notice | None:
    """The warning line for an index state. A state this script does not know is shown too, never hidden."""
    state = state_of(entry)
    if state is None or state == "delisted":
        return None
    reason = text(entry["index_status"].get("reason"))
    label = text(state) or "unknown"
    return Notice(f"The index marks this as {label}. {reason}" if reason else f"The index marks this as {label}.")


def game_text(minimum: str | None, maximum: str | None) -> str | None:
    if minimum is None:
        return None
    if maximum is None:
        return f"{minimum} or newer"
    return minimum if minimum == maximum else f"{minimum} to {maximum}"


def image_reference(target) -> str | None:
    """The record id an image names, or None for an image with any other destination, which a page never fetches."""
    if not isinstance(target, str) or not target.startswith(IMAGE_REFERENCE):
        return None
    name = target[len(IMAGE_REFERENCE):]
    return name if IMAGE_ID.fullmatch(name) else None


def image_record(record) -> dict | None:
    """One description image record, or None when a page may not fetch it or cannot lay it out."""
    if not isinstance(record, dict):
        return None
    identifier = record.get("id")
    url = https_url(record.get("url"))
    digest = str(record.get("sha256") or "").lower()
    width, height, size = (count(record.get(key)) for key in ("width", "height", "size"))
    if (not isinstance(identifier, str) or not IMAGE_ID.fullmatch(identifier) or url is None
            or not SHA256_PATTERN.fullmatch(digest)):
        return None
    if width is None or height is None or not 0 < width <= IMAGE_PIXELS or not 0 < height <= IMAGE_PIXELS:
        return None
    if size is None or not 0 < size <= IMAGE_CAP:
        return None
    return {"id": identifier, "url": url, "sha256": digest, "width": width, "height": height, "size": size,
            "attribution": text(record.get("attribution")), "source": https_url(record.get("source"))}


def image_records(authored) -> dict[str, dict]:
    """The description image records of a document by id, the first one when two records share an id."""
    images = authored.get("images") if isinstance(authored, dict) else None
    records = images.get("description") if isinstance(images, dict) else None
    found: dict[str, dict] = {}
    for record in records if isinstance(records, list) else []:
        checked = image_record(record)
        if checked is not None:
            found.setdefault(checked["id"], checked)
    return found


def image_spans(alt: str, target, href: str | None) -> list[dict]:
    """The span of one image, or nothing when it has neither a text nor a record to show."""
    reference = image_reference(target)
    if not alt and reference is None:
        return []
    return [{"kind": "image", "text": alt, "id": reference, "href": href}]


def markdown_spans(value: str, depth: int = 0, images: bool = False) -> list[dict]:
    """The inline spans of one block. With `images` an image is a span of its own, else it becomes its alternative text."""
    if depth >= SPAN_DEPTH:
        return [{"kind": "text", "text": value}] if value else []
    spans: list[dict] = []
    position = 0
    for match in INLINE_PATTERN.finditer(value):
        if match.start() > position:
            spans.append({"kind": "text", "text": value[position:match.start()]})
        position = match.end()
        groups = match.groupdict()
        if groups["alt"] is not None:
            spans.extend(image_spans(groups["alt"], groups["target"], None) if images
                         else ([{"kind": "text", "text": groups["alt"]}] if groups["alt"] else []))
        elif groups["code"] is not None:
            spans.append({"kind": "code", "text": groups["code"]})
        elif groups["label"] is not None:
            url = web_url(groups["url"])
            # A link whose whole label is an image becomes that image, and the image keeps the link.
            linked = IMAGE_ONLY.fullmatch(groups["label"].strip()) if images else None
            inside = image_spans(linked.group("alt"), linked.group("target"), url) if linked else []
            spans.extend(inside if inside
                         else [{"kind": "link", "url": url, "spans": markdown_spans(groups["label"], depth + 1)}])
        else:
            strong = groups["strong"] if groups["strong"] is not None else groups["strong_score"]
            inner = strong if strong is not None else (groups["emphasis"] if groups["emphasis"] is not None else groups["emphasis_score"])
            spans.append({"kind": "strong" if strong is not None else "emphasis", "spans": markdown_spans(inner, depth + 1)})
    if position < len(value):
        spans.append({"kind": "text", "text": value[position:]})
    return spans


def markdown_blocks(value: str) -> list[dict]:
    """The blocks of a description, which are fenced code, a heading, a list, a paragraph and the caption of an image."""
    blocks: list[dict] = []
    paragraph: list[str] = []
    lines = value.split("\n")
    index = 0

    def close(run: list[dict]) -> None:
        if any(span["kind"] != "text" or span["text"].strip() for span in run):
            blocks.append({"kind": "paragraph", "spans": run})

    def add(spans: list[dict]) -> None:
        """One paragraph per run of spans, with a block for every image between them, so two images never merge into one sentence."""
        run: list[dict] = []
        for span in spans:
            if span["kind"] != "image":
                run.append(span)
                continue
            close(run)
            run = []
            blocks.append({"kind": "image", "text": span["text"], "id": span["id"], "href": span["href"]})
        close(run)

    def flush() -> None:
        if paragraph:
            add(markdown_spans(" ".join(paragraph), images=True))
            paragraph.clear()

    while index < len(lines):
        line = lines[index]
        if line.startswith("```"):
            flush()
            index += 1
            code = []
            while index < len(lines) and not lines[index].startswith("```"):
                code.append(lines[index])
                index += 1
            index += 1
            blocks.append({"kind": "code", "text": "\n".join(code)})
            continue
        heading = HEADING_PATTERN.match(line)
        item = None if heading else ITEM_PATTERN.match(line)
        if heading:
            flush()
            blocks.append({"kind": "heading", "level": len(heading.group(1)), "spans": markdown_spans(heading.group(2))})
        elif item:
            flush()
            ordered = item.group(1)[0].isdecimal()
            if not blocks or blocks[-1]["kind"] != "list" or blocks[-1]["ordered"] != ordered:
                blocks.append({"kind": "list", "ordered": ordered, "items": []})
            blocks[-1]["items"].append(markdown_spans(item.group(2)))
        elif line.strip():
            paragraph.append(line.strip())
        else:
            flush()
        index += 1

    flush()
    return blocks


def span_html(spans: list[dict]) -> str:
    parts = []
    for span in spans:
        if span["kind"] in ("text", "image"):
            parts.append(escape(span["text"]))
        elif span["kind"] == "code":
            parts.append(f"<code>{escape(span['text'])}</code>")
        else:
            inner = span_html(span["spans"])
            if not inner:
                continue
            if span["kind"] == "strong":
                parts.append(f"<strong>{inner}</strong>")
            elif span["kind"] == "emphasis":
                parts.append(f"<em>{inner}</em>")
            elif span["url"]:
                parts.append(f'<a href="{escape(span["url"])}" rel="nofollow noopener">{inner}</a>')
            else:
                parts.append(inner)
    return "".join(parts)


def figure_style(record: dict) -> str:
    """The room the figure keeps for the image before it arrives, and the size a large image may not pass.

    The frame is never wider than the image itself, so a small image keeps its own pixels instead of
    being stretched, and never taller than the height a screen has room for.
    """
    return f'aspect-ratio: {record["width"]} / {record["height"]}; ' \
           f'max-width: min({record["width"]}px, calc(var(--shot-height) * {record["width"]} / {record["height"]}))'


def figure_credit(record: dict) -> str:
    """The attribution of a record and the link to its source, which RFC 0058 asks a client to show with the image."""
    parts = []
    if record["attribution"]:
        parts.append(escape(record["attribution"]))
    if record["source"]:
        parts.append(f'<a href="{escape(record["source"])}" rel="nofollow noopener">Source</a>')
    return f'<span class="credit">{" &middot; ".join(parts)}</span>' if parts else ""


def figure_html(block: dict, record: dict | None, indent: str) -> list[str]:
    """One image of a description, as its figure with the facts of its record, or as its caption alone."""
    caption = escape(block["text"])
    if caption and block["href"]:
        caption = f'<a href="{escape(block["href"])}" rel="nofollow noopener">{caption}</a>'
    if record is None:
        # An image that names a record the page does not have still leaves a line behind, because a reader
        # has to see that there is an image here that the page does not show.
        if not caption and block["id"] is None:
            return []
        return [f'{indent}<p class="figure">{caption or IMAGE_PLACEHOLDER}</p>']
    # The frame carries the link as well, so a reader can open it from the image, and the caption gives
    # the only link that a reader who navigates by keyboard or by screen reader meets.
    tag = "a" if block["href"] else "div"
    link = f' href="{escape(block["href"])}" rel="nofollow noopener" tabindex="-1" aria-hidden="true"' if block["href"] else ""
    body = caption + figure_credit(record)
    lines = [f'{indent}<figure data-image="{escape(record["url"])}" data-sha256="{record["sha256"]}" '
             f'data-width="{record["width"]}" data-height="{record["height"]}" data-size="{record["size"]}">',
             f'{indent}  <{tag} class="frame" style="{escape(figure_style(record))}"{link}></{tag}>']
    if body:
        lines.append(f"{indent}  <figcaption>{body}</figcaption>")
    else:
        # An image with no words of its own still gets a caption, because the caption is the whole of what
        # a reader sees when the image does not arrive. The page hides it again once the image is there.
        lines.append(f'{indent}  <figcaption class="untitled">{IMAGE_PLACEHOLDER}</figcaption>')
    lines.append(f"{indent}</figure>")
    return lines


def markdown_html(value: str, indent: str = "", images: dict | None = None) -> str:
    """The description as HTML, one block per line."""
    lines = []
    for block in markdown_blocks(value):
        if block["kind"] == "heading":
            level = min(block["level"] + HEADING_OFFSET, 6)
            lines.append(f"{indent}<h{level}>{span_html(block['spans'])}</h{level}>")
        elif block["kind"] == "code":
            lines.append(f"{indent}<pre><code>{escape(block['text'])}</code></pre>")
        elif block["kind"] == "image":
            lines.extend(figure_html(block, (images or {}).get(block["id"]), indent))
        elif block["kind"] == "list":
            tag = "ol" if block["ordered"] else "ul"
            lines.append(f"{indent}<{tag}>")
            lines.extend(f"{indent}  <li>{span_html(item)}</li>" for item in block["items"])
            lines.append(f"{indent}</{tag}>")
        else:
            lines.append(f"{indent}<p>{span_html(block['spans'])}</p>")
    return "".join(line + "\n" for line in lines)


class Snapshot:
    """The parts of a snapshot the pages need, read defensively."""

    def __init__(self, document: dict):
        self.listings = [entry for entry in document.get("listings") or [] if isinstance(entry, dict)]
        self.packs = [entry for entry in document.get("packs") or [] if isinstance(entry, dict)]
        versions = (document.get("game_versions") or {}).get("versions") if isinstance(document.get("game_versions"), dict) else None
        self.game_versions = {}
        for version in versions if isinstance(versions, list) else []:
            if isinstance(version, str) and version.rsplit(".", 1)[-1].isdecimal():
                self.game_versions.setdefault(int(version.rsplit(".", 1)[-1]), version)
        self.names = {}
        self.entries = {}
        for entry in self.listings:
            if valid_id(entry.get("id")) and isinstance(entry.get("authored"), dict):
                self.names.setdefault(entry["id"].lower(), (entry["id"], text(entry["authored"].get("name")) or entry["id"]))
                if state_of(entry) != "delisted":
                    self.entries.setdefault(entry["id"].lower(), entry)
        curated = document["tags"].get("mod") if isinstance(document.get("tags"), dict) else None
        self.curated_tags = []
        for tag in curated if isinstance(curated, list) else []:
            key = text(tag.get("tag")) if isinstance(tag, dict) else None
            name = text(tag.get("name")) if isinstance(tag, dict) else None
            if key and name:
                self.curated_tags.append((key.lower(), name))

    def game_version(self, revision, fallback) -> str | None:
        if count(revision) is not None and revision in self.game_versions:
            return self.game_versions[revision]
        return text(fallback)

    def display_tags(self, tags) -> list[str]:
        """The curated tags of a listing under their vocabulary name and in its order, then the tags the author wrote."""
        written = [name for name in (text(tag) for tag in tags) if name] if isinstance(tags, list) else []
        chosen = {name.lower() for name in written}
        known = {key for key, _ in self.curated_tags}
        return [name for key, name in self.curated_tags if key in chosen] + [name for name in written if name.lower() not in known]


def links_of(authored: dict) -> list[tuple[str, str]]:
    links = authored.get("links")
    if not isinstance(links, dict):
        return []
    order = list(LINK_LABELS)
    found = []
    for key, value in links.items():
        url = web_url(value)
        label = text(key)
        if url is None or label is None:
            continue
        known = key.lower()
        rank = order.index(known) if known in LINK_LABELS else len(order)
        found.append((rank, LINK_LABELS.get(known) or label[0].upper() + label[1:], url))
    found.sort(key=lambda link: link[0])
    return [(label, url) for _, label, url in found]


def authors_of(authored: dict) -> list[str]:
    authors = authored.get("authors")
    return [name for name in (text(author) for author in authors) if name] if isinstance(authors, list) else []


def newest_release(releases) -> dict | None:
    """The newest stable release that is not yanked, else the newest one that is not yanked."""
    usable = [release for release in releases if isinstance(release, dict) and release.get("yanked") is not True]
    stable = [release for release in usable if release.get("release_status") in (None, "stable")]
    return (stable or usable or [None])[0]


def channel_of(release: dict) -> int:
    """The narrowest channel that offers a release. An unknown status counts as dev, and no status as stable."""
    status = release.get("release_status")
    return CHANNELS["stable"] if status is None else CHANNELS.get(str(status).lower(), DEV_CHANNEL)


def forum_link(authored: dict) -> str | None:
    links = authored.get("links")
    found = [web_url(value) for key, value in links.items() if str(key).lower() == "forums"] if isinstance(links, dict) else []
    return next((url for url in found if url), None)


def pack_member(pin, snapshot: Snapshot) -> tuple[str, str | None] | None:
    """The forum line of one pin and its newer release, or None for an entry that is no pin.

    ModPackForumList in Borea.Core writes the same line. A newer release counts when the channel of the pinned
    release offers it, which is what ModPackMemberReleases finds for a player on the stable channel.
    """
    if not isinstance(pin, dict) or not valid_id(pin.get("id")) or text(pin.get("version")) is None:
        return None
    identifier, version = pin["id"], text(pin["version"])
    entry = snapshot.entries.get(identifier.lower())
    releases = entry.get("releases") if entry and isinstance(entry.get("releases"), list) else []
    releases = [release for release in releases if isinstance(release, dict)]
    index = next((number for number, release in enumerate(releases) if text(release.get("version")) == version), None)
    if index is None:
        return f"{identifier} {version} - Not listed in the content index", None

    authored = entry["authored"]
    name = text(authored.get("name")) or entry["id"]
    download = releases[index].get("download")
    facts = [
        f"{name} {version}",
        f"Author: {', '.join(authors_of(authored)) or 'not stated'}",
        f"License: {text(authored.get('license')) or 'not stated'}",
        f"Download: {(web_url(download.get('url')) if isinstance(download, dict) else None) or 'not stated'}",
        f"Thread: {forum_link(authored) or 'not stated'}",
    ]
    channel = channel_of(releases[index])
    # The releases are in descending SemVer precedence, so every release before the pinned one is newer.
    newer = next((release for release in releases[:index]
                  if release.get("yanked") is not True and channel_of(release) <= channel and text(release.get("version"))), None)
    return " - ".join(facts), f"{name} {text(newer['version'])}" if newer else None


def newer_text(page: Page) -> str | None:
    """The mark forum rule 4.6 asks for, which Borea computes because a pack pins exact versions on purpose."""
    if not page.newer:
        return None
    total = len(page.forum_list)
    count_text = (f"1 of {total} mods has a newer release" if len(page.newer) == 1
                  else f"{len(page.newer)} of {total} mods have newer releases")
    return f"{count_text}: {', '.join(page.newer)}."


def listing_page(entry: dict, snapshot: Snapshot) -> Page:
    authored = entry["authored"]
    releases = entry.get("releases") if isinstance(entry.get("releases"), list) else []
    release = newest_release(releases)
    if release is not None:
        game = game_text(snapshot.game_version(release.get("game_min_revision"), release.get("game_min")),
                         snapshot.game_version(release.get("game_max_revision"), release.get("game_max"))
                         if "game_max" in release or "game_max_revision" in release else None)
    else:
        compatibility = authored.get("compatibility") if isinstance(authored.get("compatibility"), dict) else {}
        game = game_text(text(compatibility.get("game_min")), text(compatibility.get("game_max")))
    channel = text(release.get("release_status")) if release else None
    downloads = entry.get("downloads")

    page = Page(
        kind="mod",
        id=entry["id"],
        name=text(authored.get("name")) or entry["id"],
        type_label=TYPE_LABELS.get(text(authored.get("type")), "Mod"),
        authors=authors_of(authored),
        abstract=text(authored.get("abstract")),
        description=rich_text(authored.get("description")),
        license=text(authored.get("license")),
        version=text(release.get("version")) if release else None,
        channel=None if channel in (None, "stable") else channel,
        date=timestamp(release.get("release_date")) if release else None,
        game=game,
        downloads=count(downloads.get("total")) if isinstance(downloads, dict) else None,
        mod_count=None,
        tags=snapshot.display_tags(authored.get("tags")),
        links=links_of(authored),
        images=image_records(authored),
    )
    notice = status_notice(entry)
    if notice:
        page.notices.append(notice)
    if authored.get("status") == "deprecated":
        successor = authored.get("superseded_by")
        known = snapshot.names.get(successor.lower()) if valid_id(successor) else None
        if known:
            page.notices.append(Notice("Deprecated by its author. Its successor is", (known[1], f"../{known[0]}/")))
        elif valid_id(successor):
            page.notices.append(Notice(f"Deprecated by its author. Its successor is {successor}."))
        else:
            page.notices.append(Notice("Deprecated by its author."))
    return page


def pack_page(entry: dict, snapshot: Snapshot) -> Page | None:
    versions = [version for version in entry.get("versions") or [] if isinstance(version, dict) and isinstance(version.get("authored"), dict)]
    if not versions:
        return None
    current = next((version for version in versions if state_of(version) != "retracted"), None)
    authored = (current or versions[0])["authored"]
    compatibility = authored.get("compatibility") if isinstance(authored.get("compatibility"), dict) else {}
    mods = authored.get("mods")

    page = Page(
        kind="pack",
        id=entry["id"],
        name=text(authored.get("name")) or entry["id"],
        type_label=TYPE_LABELS["modpack"],
        authors=authors_of(authored),
        abstract=text(authored.get("abstract")),
        description=rich_text(authored.get("description")),
        license=text(authored.get("license")),
        version=text(authored.get("version")),
        channel=None,
        date=timestamp(authored.get("released_at")),
        game=game_text(text(compatibility.get("game_min")), text(compatibility.get("game_max"))),
        downloads=None,
        mod_count=len(mods) if isinstance(mods, list) else None,
        tags=snapshot.display_tags(authored.get("tags")),
        links=links_of(authored),
        images=image_records(authored),
    )
    members = [member for member in (pack_member(pin, snapshot) for pin in (mods if isinstance(mods, list) else []))
               if member is not None]
    page.forum_list = [line for line, _ in members]
    page.newer = [newer for _, newer in members if newer]
    notice = status_notice(entry)
    if notice:
        page.notices.append(notice)
    if current is None:
        page.notices.append(Notice("Every version of this pack is retracted."))
    if authored.get("status") == "deprecated":
        page.notices.append(Notice("Deprecated by its author."))
    return page


def icon_record(entry: dict, kind: str):
    if kind == "mod":
        authored = entry.get("authored")
    else:
        versions = [version for version in entry.get("versions") or [] if isinstance(version, dict) and isinstance(version.get("authored"), dict)]
        current = next((version for version in versions if state_of(version) != "retracted"), versions[0] if versions else None)
        authored = current["authored"] if current else None
    images = authored.get("images") if isinstance(authored, dict) else None
    record = images.get("icon") if isinstance(images, dict) else None
    return record if isinstance(record, dict) else None


def log(message: str) -> None:
    # A line that starts with "::" is a workflow command on GitHub Actions, and authors write parts of these messages.
    print("share pages: " + CONTROL.sub(" ", message), file=sys.stderr)


class Icons:
    """Verified icon bytes, from the cache of the last build or from the author host."""

    def __init__(self, out_dir: Path, cache_dir: Path | None = None, fetch=None):
        self.out_dir = out_dir
        self.cache_dir = cache_dir
        self.fetch = fetch or listing_icons.fetch
        self.used: set[str] = set()

    def get(self, owner: str, record: dict) -> tuple[str, int, int] | None:
        """The site path, width and height of the verified icon, or None when the page shows the placeholder."""
        digest = str(record.get("sha256") or "").lower()
        if not SHA256_PATTERN.fullmatch(digest) or not isinstance(record.get("url"), str):
            log(f"{owner}: the icon record is incomplete")
            return None
        try:
            data, extension = self._cached(record, digest) or self._fetched(record)
        except listing_icons.IconError as error:
            log(f"{owner}: the icon is not used: {error}")
            return None
        except Exception as error:
            # One icon must never stop the build of every page.
            log(f"{owner}: the icon is not used, because of an unexpected error: {error!r}")
            return None

        name = f"{digest}.{extension}"
        target = self.out_dir / "img" / "icons" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        if self.cache_dir is not None:
            self.cache_dir.mkdir(parents=True, exist_ok=True)
            (self.cache_dir / name).write_bytes(data)
            self.used.add(name)
        return f"img/icons/{name}", record["width"], record["height"]

    def _cached(self, record, digest):
        if self.cache_dir is None:
            return None
        for path in sorted(self.cache_dir.glob(digest + ".*")):
            try:
                data = path.read_bytes()
                extension = listing_icons.verify(record, data)
            except (OSError, listing_icons.IconError):
                continue
            if path.suffix == "." + extension:
                return data, extension
        return None

    def _fetched(self, record):
        data = self.fetch(record["url"], listing_icons.CAP)
        return data, listing_icons.verify(record, data)

    def prune(self) -> None:
        """Removes the cached icons no page used, so the cache follows the index."""
        if self.cache_dir is None or not self.cache_dir.is_dir():
            return
        for path in self.cache_dir.iterdir():
            if path.is_file() and path.name not in self.used:
                path.unlink()


def escape(value) -> str:
    return html.escape(str(value), quote=True)


def script_json(value) -> str:
    """JSON that cannot end its script element or open a comment inside it."""
    return (json.dumps(value, ensure_ascii=True, sort_keys=True)
            .replace("<", "\\u003c").replace(">", "\\u003e").replace("&", "\\u0026"))


def share_url(site_url: str, page: Page) -> str:
    return f"{site_url}{page.kind}/{page.id}/"


def structured_data(page: Page, url: str, image: str) -> dict:
    data = {
        "@context": "https://schema.org",
        "@type": "SoftwareApplication",
        "name": page.name,
        "url": url,
        "image": image,
        "applicationCategory": "GameApplication",
    }
    if page.abstract:
        data["description"] = page.abstract
    if page.version:
        data["softwareVersion"] = page.version
    if page.authors:
        data["author"] = [{"@type": "Person", "name": name} for name in page.authors]
    return data


def members_html(page: Page) -> str:
    """The Mods section of a pack page, with the forum list that copy-list.js copies, or nothing on a mod page."""
    if page.forum_list is None:
        return ""
    summary = f'        <p class="newer">{escape(newer_text(page))}</p>\n' if page.newer else ""
    return ('      <section class="members">\n        <h2>Mods</h2>\n'
            f'{summary}        <pre class="forum-list">{escape(chr(10).join(page.forum_list))}</pre>\n'
            '        <button class="button secondary" type="button" data-copy-list hidden>Copy forum list</button>\n'
            '        <p class="meta">One line per mod with its version, author, license, download and release thread, '
            'the way the forum rules ask a pack thread to list them.</p>\n'
            '      </section>\n')


def actions_html(page: Page, root: str) -> str:
    """The buttons under the header, and the line that sends a reader on a phone or a tablet to a computer.

    Copy link stays hidden until copy-list.js finds a clipboard.
    """
    return ('        <div class="actions">\n'
            f'          <a class="button desktop-only" href="borea://{page.kind}/{page.id}">Open in Borea</a>\n'
            f'          <a class="button secondary desktop-only" href="borea://install/{page.id}">Install with Borea</a>\n'
            '          <button class="button handheld-only" type="button" data-copy-link hidden>Copy link</button>\n'
            f'          <a class="button secondary" href="{root}#download">Get Borea</a>\n'
            '        </div>\n'
            f'        <p class="meta hint desktop-only">{DESKTOP_HINT}</p>\n'
            f'        <p class="meta hint handheld-only">{HANDHELD_HINT}</p>\n')


def render(page: Page, site_url: str = SITE_URL) -> str:
    root = "../../"
    url = share_url(site_url, page)
    image = site_url + (page.icon[0] if page.icon else "img/icon.png")
    description = page.abstract or f"{page.name} for Kitten Space Agency."
    if len(description) > DESCRIPTION_LIMIT:
        description = description[:DESCRIPTION_LIMIT - 3].rstrip() + "..."
    title = f"{page.name} - Borea"

    if page.icon:
        icon = (f'<img src="{root}{escape(page.icon[0])}" width="{int(page.icon[1])}" height="{int(page.icon[2])}" '
                f'alt="">')
        icon_class = "listing-icon"
    else:
        icon = f'<img src="{root}img/placeholder.svg" width="56" height="56" alt="">'
        icon_class = "listing-icon placeholder"

    notices = []
    for notice in page.notices:
        link = f' <a href="{escape(notice.link[1])}">{escape(notice.link[0])}</a>.' if notice.link else ""
        notices.append(f'        <p class="notice" role="note">{escape(notice.text)}{link}</p>\n')

    tags = ""
    if page.tags:
        items = "".join(f'          <li class="chip">{escape(tag)}</li>\n' for tag in page.tags)
        tags = f'        <ul class="chips">\n{items}        </ul>\n'

    compatibility = ""
    if page.game:
        compatibility = ('        <section>\n          <h2>Compatibility</h2>\n          <ul class="chips">\n'
                         f'            <li class="chip">{escape(page.game)}</li>\n'
                         '          </ul>\n        </section>\n')

    facts = [("Type", escape(page.type_label))]
    if page.version:
        version = escape(page.version)
        if page.channel:
            version += f' <span class="channel">{escape(page.channel)}</span>'
        if page.date:
            version += f' &middot; <time datetime="{page.date.strftime("%Y-%m-%dT%H:%M:%SZ")}">{date_text(page.date)}</time>'
        facts.append(("Latest version", version))
    else:
        facts.append(("Latest version", "No release yet"))
    if page.mod_count is not None:
        facts.append(("Mods", str(page.mod_count)))
    if page.downloads is not None:
        facts.append(("Downloads", f"{page.downloads:,}"))
    if page.license:
        facts.append(("License", escape(page.license)))
    fact_rows = "".join(f"            <dt>{name}</dt><dd>{value}</dd>\n" for name, value in facts)

    links = ""
    if page.links:
        items = "".join(
            f'            <li><a href="{escape(link)}" rel="nofollow noopener">{escape(label)}</a> '
            f'<span class="meta">{escape(urllib.parse.urlsplit(link).hostname or "")}</span></li>\n'
            for label, link in page.links)
        links = (f'        <section>\n          <h2>Links</h2>\n          <ul class="links">\n{items}'
                 f'          </ul>\n        </section>\n')

    body = (markdown_html(page.description, "          ", page.images) if page.description
            else '          <p class="meta">No description provided.</p>\n')
    members = members_html(page)
    main_open, main_close = ('      <div class="listing-main">\n', "      </div>\n") if members else ("", "")
    switch = IMAGE_SWITCH if '<figure data-image="' in body else ""
    authors = f'        <p class="meta by">by {escape(", ".join(page.authors))}</p>\n' if page.authors else ""
    abstract = f'        <p class="tagline">{escape(page.abstract)}</p>\n' if page.abstract else ""
    data = script_json(structured_data(page, url, image))

    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{escape(title)}</title>
  <meta name="description" content="{escape(description)}">
  <meta name="theme-color" content="#171717">
  <link rel="canonical" href="{escape(url)}">
  <meta property="og:site_name" content="Borea">
  <meta property="og:type" content="website">
  <meta property="og:title" content="{escape(page.name)}">
  <meta property="og:description" content="{escape(description)}">
  <meta property="og:url" content="{escape(url)}">
  <meta property="og:image" content="{escape(image)}">
  <meta name="twitter:card" content="summary">
  <meta name="twitter:title" content="{escape(page.name)}">
  <meta name="twitter:description" content="{escape(description)}">
  <meta name="twitter:image" content="{escape(image)}">
  <link rel="icon" href="{root}favicon.ico">
  <link rel="preload" href="{root}fonts/IBMPlexSans-SemiBold.woff2" as="font" type="font/woff2" crossorigin>
  <link rel="preload" href="{root}fonts/IBMPlexSans-Regular.woff2" as="font" type="font/woff2" crossorigin>
  <link rel="stylesheet" href="{root}styles.css">
  <script src="{root}handheld.js"></script>
  <script src="{root}description-images.js"></script>
  <script src="{root}copy-list.js" defer></script>
  <script type="application/ld+json">{data}</script>
</head>
<body class="share">
  <header class="column top">
    <a class="brand" href="{root}"><img src="{root}img/icon.png" width="28" height="28" alt="">Borea</a>
  </header>
  <main class="column">
    <div class="listing listing-header">
      <div class="{icon_class}">{icon}</div>
      <div class="listing-intro">
        <p class="meta kind">{escape(page.type_label)}</p>
        <h1>{escape(page.name)}</h1>
{authors}{abstract}{tags}{"".join(notices)}{actions_html(page, root)}      </div>
    </div>

    <div class="listing-body">
{main_open}      <section class="description">
        <h2>Description</h2>
{switch}        <div class="prose">
{body}        </div>
      </section>
{members}{main_close}
      <aside class="panel">
{compatibility}{links}        <section>
          <h2>Details</h2>
          <dl class="facts">
{fact_rows}          </dl>
        </section>
      </aside>
    </div>
  </main>

  <footer class="column">
    <p class="meta"><a href="https://github.com/KSAModding/Borea/blob/main/LICENSE">MIT licensed</a> &middot; Community project, not affiliated with RocketWerkz.</p>
  </footer>
</body>
</html>
"""


def pages_of(snapshot: Snapshot) -> list[tuple[Page, dict]]:
    """Every page to write. Tombstones, invalid ids and ids that repeat in another casing are left out."""
    pages = []
    for kind, entries in (("mod", snapshot.listings), ("pack", snapshot.packs)):
        seen = set()
        for entry in entries:
            identifier = entry.get("id")
            if not valid_id(identifier):
                log(f"a {kind} entry has an id that cannot be a page: {str(identifier)[:80]!r}")
                continue
            if identifier.lower() in seen:
                log(f"{kind} {identifier}: the id repeats in another casing")
                continue
            seen.add(identifier.lower())
            if state_of(entry) == "delisted":
                continue
            if kind == "mod":
                page = listing_page(entry, snapshot) if isinstance(entry.get("authored"), dict) else None
            else:
                page = pack_page(entry, snapshot)
            if page is not None:
                pages.append((page, entry))
    return pages


def build(document, site_dir: Path, out_dir: Path, icons: Icons, site_url: str = SITE_URL) -> int:
    """Writes the site into `out_dir`, which must not exist yet. Returns the number of pages."""
    if not isinstance(document, dict) or document.get("snapshot_version") != SNAPSHOT_VERSION:
        raise ValueError(f"the snapshot is not snapshot_version {SNAPSHOT_VERSION}")
    if not isinstance(document.get("listings"), list) or not isinstance(document.get("packs"), list):
        raise ValueError("the snapshot has no listings or packs array")

    shutil.copytree(site_dir, out_dir)
    snapshot = Snapshot(document)
    written = 0
    for page, entry in pages_of(snapshot):
        record = icon_record(entry, page.kind)
        if record is not None:
            page.icon = icons.get(f"{page.kind} {page.id}", record)
        target = out_dir / page.kind / page.id / "index.html"
        target.parent.mkdir(parents=True)
        target.write_text(render(page, site_url), encoding="utf-8", newline="\n")
        written += 1
    icons.prune()
    return written


def load_snapshot(source: str, attempts: int = FETCH_ATTEMPTS, wait: float = 5.0):
    """The parsed snapshot from a URL or a file. Raises when it cannot be read, so nothing is deployed."""
    if not source.startswith("https://"):
        return json.loads(Path(source).read_text(encoding="utf-8"))
    request = urllib.request.Request(source, headers={"User-Agent": listing_icons.USER_AGENT})
    for attempt in range(1, attempts + 1):
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                return json.loads(response.read().decode("utf-8"))
        except (urllib.error.URLError, OSError, ValueError) as error:
            if attempt == attempts:
                raise RuntimeError(f"the snapshot could not be read from {source}: {error}") from error
            time.sleep(wait * attempt)
    raise AssertionError("unreachable")


def main(arguments: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--site", type=Path, required=True, help="the folder of the landing site")
    parser.add_argument("--out", type=Path, required=True, help="the output folder, which must not exist")
    parser.add_argument("--snapshot", default=SNAPSHOT_URL, help="the snapshot URL or a local file")
    parser.add_argument("--icon-cache", type=Path, help="a folder that keeps verified icons between builds")
    parser.add_argument("--site-url", default=SITE_URL, help="the address the site is served from")
    options = parser.parse_args(arguments)

    document = load_snapshot(options.snapshot)
    count_written = build(document, options.site, options.out, Icons(options.out, options.icon_cache), options.site_url)
    print(f"share pages: wrote {count_written} pages into {options.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
