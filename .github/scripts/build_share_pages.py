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
    license: str | None
    version: str | None
    channel: str | None
    date: datetime | None
    game: str | None
    downloads: int | None
    mod_count: int | None
    links: list[tuple[str, str]] = field(default_factory=list)
    notices: list[Notice] = field(default_factory=list)
    icon: tuple[str, int, int] | None = None


def valid_id(value) -> bool:
    return isinstance(value, str) and bool(ID_PATTERN.match(value)) and value.split(".")[0].lower() not in RESERVED_IDS


def text(value) -> str | None:
    """A trimmed string with its whitespace collapsed, or None for anything else."""
    if not isinstance(value, str):
        return None
    collapsed = " ".join(CONTROL.sub(" ", value).split())
    return collapsed or None


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
        for entry in self.listings:
            if valid_id(entry.get("id")) and isinstance(entry.get("authored"), dict):
                self.names.setdefault(entry["id"].lower(), (entry["id"], text(entry["authored"].get("name")) or entry["id"]))

    def game_version(self, revision, fallback) -> str | None:
        if count(revision) is not None and revision in self.game_versions:
            return self.game_versions[revision]
        return text(fallback)


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
        license=text(authored.get("license")),
        version=text(release.get("version")) if release else None,
        channel=None if channel in (None, "stable") else channel,
        date=timestamp(release.get("release_date")) if release else None,
        game=game,
        downloads=count(downloads.get("total")) if isinstance(downloads, dict) else None,
        mod_count=None,
        links=links_of(authored),
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
        license=text(authored.get("license")),
        version=text(authored.get("version")),
        channel=None,
        date=timestamp(authored.get("released_at")),
        game=game_text(text(compatibility.get("game_min")), text(compatibility.get("game_max"))),
        downloads=None,
        mod_count=len(mods) if isinstance(mods, list) else None,
        links=links_of(authored),
    )
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
        if not SHA256_PATTERN.match(digest) or not isinstance(record.get("url"), str):
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
        notices.append(f'      <p class="notice" role="note">{escape(notice.text)}{link}</p>\n')

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
    if page.game:
        facts.append(("Game", escape(page.game)))
    if page.mod_count is not None:
        facts.append(("Mods", str(page.mod_count)))
    if page.downloads is not None:
        facts.append(("Downloads", f"{page.downloads:,}"))
    if page.license:
        facts.append(("License", escape(page.license)))
    fact_rows = "".join(f"          <dt>{name}</dt><dd>{value}</dd>\n" for name, value in facts)

    links = ""
    if page.links:
        items = "".join(
            f'          <li><a href="{escape(link)}" rel="nofollow noopener">{escape(label)}</a> '
            f'<span class="meta">{escape(urllib.parse.urlsplit(link).hostname or "")}</span></li>\n'
            for label, link in page.links)
        links = (f'      <section>\n        <h2>Links</h2>\n        <ul class="links">\n{items}        </ul>\n'
                 f'      </section>\n')

    authors = f'      <p class="meta by">by {escape(", ".join(page.authors))}</p>\n' if page.authors else ""
    abstract = f'      <p class="tagline">{escape(page.abstract)}</p>\n' if page.abstract else ""
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
  <link rel="stylesheet" href="{root}styles.css">
  <script type="application/ld+json">{data}</script>
</head>
<body>
  <header class="column top">
    <a class="brand" href="{root}"><img src="{root}img/icon.png" width="28" height="28" alt="">Borea</a>
  </header>
  <main>
    <div class="hero listing">
      <div class="{icon_class}">{icon}</div>
      <p class="meta kind">{escape(page.type_label)}</p>
      <h1>{escape(page.name)}</h1>
{authors}{abstract}{"".join(notices)}      <div class="actions">
        <a class="button" href="borea://{page.kind}/{page.id}">Open in Borea</a>
        <a class="button secondary" href="borea://install/{page.id}">Install with Borea</a>
        <a class="button secondary" href="{root}#download">Get Borea</a>
      </div>
      <p class="meta hint">Open in Borea and Install with Borea need Borea on this computer.</p>
    </div>

    <div class="column">
      <section>
        <h2>Details</h2>
        <dl class="facts">
{fact_rows}        </dl>
      </section>
{links}    </div>
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
