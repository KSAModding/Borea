#!/usr/bin/env python3
"""Writes THIRD-PARTY-NOTICES.txt for one Borea release archive.

The packages come from the SBOM of the product and are read only after their hash matches it.
The script fails instead of leaving an entry out.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import io
import json
import re
import sys
import xml.etree.ElementTree as ElementTree
import zipfile
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath
from urllib.parse import unquote

LICENSE_STEMS = {"license", "licence", "copying"}
NOTICE_STEMS = {"third-party-notices", "thirdpartynotices", "notice", "notices"}
TEXT_SUFFIXES = {"", ".txt", ".md"}
SPDX_OPERATORS = {"AND", "OR", "WITH"}
SEPARATOR = "=" * 80
PRODUCT_NAMES = {"App": "App", "Cli": "CLI"}
COPYRIGHT_SIGN = chr(0xA9)


class NoticesError(Exception):
    """A reason why the notices file cannot be complete."""


@dataclass
class Text:
    """One license or notice text, with what kind of text it is and where it comes from."""

    kind: str
    source: str
    content: str
    note: str | None = None


@dataclass
class Entry:
    """One piece of third-party software in the archive."""

    name: str
    version: str | None
    license: str
    copyright: str | None
    details: list[str]
    texts: list[Text] = field(default_factory=list)

    @property
    def title(self) -> str:
        return f"{self.name} {self.version}" if self.version else self.name


def decode_text(data: bytes) -> str:
    """Decodes a license file: UTF-16 with a byte order mark, else UTF-8, else Latin-1."""
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        text = data.decode("utf-16", errors="replace")
    else:
        try:
            text = data.decode("utf-8-sig")
        except UnicodeDecodeError:
            text = data.decode("latin-1")
    return text.replace("\r\n", "\n").replace("\r", "\n").strip("\n")


def read_file(path: Path) -> bytes:
    try:
        return path.read_bytes()
    except OSError as error:
        raise NoticesError(f"Cannot read {path}: {error.strerror}.") from error


def read_pinned(path: Path, expected: str) -> str:
    """Reads a text pinned by the SHA-256 hash of its LF form."""
    data = read_file(path)
    actual = hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest()
    if actual != expected.lower():
        raise NoticesError(f"{path} does not match its pinned SHA-256 hash.")
    return decode_text(data)


def require_content(text: Text, label: str) -> Text:
    if not text.content.strip():
        raise NoticesError(f"{text.source} of {label} is empty.")
    if "\x00" in text.content:
        raise NoticesError(f"{text.source} of {label} contains NUL characters, so it is not a text that this script can read.")
    return text


class SpdxTexts:
    """The standard license texts that are pinned in the notices configuration."""

    def __init__(self, config: dict, root: Path):
        self._list_name = config["list"]
        self._texts = config["texts"]
        self._root = root
        self._cache: dict[str, str] = {}

    def text(self, identifier: str, label: str) -> Text:
        pinned = self._texts.get(identifier)
        if pinned is None:
            raise NoticesError(
                f"{label} declares the license {identifier}, but the notices configuration has no pinned standard text for it."
            )
        if identifier not in self._cache:
            self._cache[identifier] = read_pinned(self._root / pinned["path"], pinned["sha256"])
        return Text(
            "License text",
            f"the standard text of {identifier} from the {self._list_name}",
            self._cache[identifier],
            "The standard text of the declared license expression.",
        )


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def read_nuspec(archive: zipfile.ZipFile, label: str) -> dict[str, ElementTree.Element]:
    nuspecs = [info for info in archive.infolist() if "/" not in info.filename and info.filename.lower().endswith(".nuspec")]
    if len(nuspecs) != 1:
        raise NoticesError(f"{label} does not contain exactly one .nuspec file.")
    try:
        root = ElementTree.fromstring(archive.read(nuspecs[0]))
    except ElementTree.ParseError as error:
        raise NoticesError(f"The .nuspec file of {label} is not valid XML: {error}.") from error
    metadata = next((child for child in root if local_name(child.tag) == "metadata"), None)
    if metadata is None:
        raise NoticesError(f"The .nuspec file of {label} has no metadata.")
    return {local_name(child.tag): child for child in metadata}


def element_text(metadata: dict[str, ElementTree.Element], name: str) -> str:
    element = metadata.get(name)
    return (element.text or "").strip() if element is not None else ""


def find_member(archive: zipfile.ZipFile, wanted: str) -> zipfile.ZipInfo | None:
    """Finds a file of a package by its unescaped path."""
    wanted = wanted.replace("\\", "/").lstrip("/")
    for info in archive.infolist():
        if unquote(info.filename) == wanted:
            return info
    matches = [info for info in archive.infolist() if unquote(info.filename).lower() == wanted.lower()]
    return matches[0] if len(matches) == 1 else None


def root_files(archive: zipfile.ZipFile, stems: set[str]) -> list[tuple[str, zipfile.ZipInfo]]:
    """The files at the top of a package whose names mark them as license or notice texts."""
    found = []
    for info in archive.infolist():
        name = unquote(info.filename)
        if "/" in name:
            continue
        path = PurePosixPath(name)
        if path.suffix.lower() in TEXT_SUFFIXES and path.stem.lower() in stems:
            found.append((name, info))
    return sorted(found, key=lambda item: item[0].lower())


def spdx_identifiers(expression: str) -> list[str]:
    tokens = re.findall(r"[A-Za-z0-9.+-]+", expression)
    return [token for token in tokens if token.upper() not in SPDX_OPERATORS]


def entry_from_archive(
    archive: zipfile.ZipFile,
    name: str,
    version: str,
    label: str,
    spdx: SpdxTexts,
    details: list[str],
) -> Entry:
    """The license, the copyright notice and the texts of one NuGet package.

    A license file named in the .nuspec wins over license files in the package root, which win over the
    standard texts. Members are read whole, which is safe after the SBOM hash check.
    """
    metadata = read_nuspec(archive, label)
    license_element = metadata.get("license")
    license_type = license_element.get("type") if license_element is not None else None
    license_value = element_text(metadata, "license")
    copyright_notice = element_text(metadata, "copyright") or None
    texts: list[Text] = []

    if license_type == "file" and license_value:
        member = find_member(archive, license_value)
        if member is None:
            raise NoticesError(f"{label} names {license_value} as its license file, but the package does not contain it.")
        texts.append(Text("License text", f"{license_value} in the package", decode_text(archive.read(member))))
        declared = f"license file {license_value}"
    elif license_type == "expression" and license_value:
        declared = license_value
    else:
        declared = "not declared, see the license text"

    if not texts:
        for file_name, member in root_files(archive, LICENSE_STEMS):
            texts.append(Text("License text", f"{file_name} in the package", decode_text(archive.read(member))))
    if not texts and license_type == "expression" and license_value:
        # The standard text names no copyright holder, so the package must.
        if copyright_notice is None:
            raise NoticesError(
                f"{label} declares only the license expression {license_value} and no copyright notice, "
                "so the standard license text would name no copyright holder."
            )
        texts.extend(spdx.text(identifier, label) for identifier in spdx_identifiers(license_value))
    if not texts:
        raise NoticesError(
            f"{label} contains no license file and declares no SPDX license expression, so its license text is unknown."
        )

    for file_name, member in root_files(archive, NOTICE_STEMS):
        texts.append(Text("Notices", f"{file_name} in the package", decode_text(archive.read(member))))

    return Entry(name, version, declared, copyright_notice, details, [require_content(text, label) for text in texts])


def package_entry(component: dict, packages: Path, spdx: SpdxTexts) -> Entry:
    name = component.get("name")
    version = component.get("version")
    purl = component.get("purl") or ""
    if not name or not version or not purl.startswith("pkg:nuget/"):
        reference = component.get("bom-ref") or name or "without a name"
        raise NoticesError(f"The SBOM component {reference} is not a NuGet package, so its license cannot be read.")

    label = f"{name} {version}"
    expected = next((item.get("content", "") for item in component.get("hashes", []) if item.get("alg") == "SHA-512"), "")
    if not expected:
        raise NoticesError(f"The SBOM has no SHA-512 hash for {label}.")

    archive_path = packages / name.lower() / version.lower() / f"{name.lower()}.{version.lower()}.nupkg"
    if not archive_path.is_file():
        raise NoticesError(f"{label} is not restored in {packages}.")
    data = read_file(archive_path)
    if hashlib.sha512(data).hexdigest() != expected.lower():
        raise NoticesError(f"The restored archive of {label} does not match the SHA-512 hash in the SBOM.")

    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        return entry_from_archive(archive, name, version, label, spdx, [f"NuGet package: https://www.nuget.org/packages/{name}/{version}"])


def runtime_entries(deps_files: list[Path], packages: Path, spdx: SpdxTexts) -> list[Entry]:
    """The .NET runtime of the self-contained publish, checked against the hash NuGet recorded at restore."""
    packs: list[str] | None = None
    for deps in deps_files:
        try:
            document = json.loads(read_file(deps).decode("utf-8-sig"))
        except ValueError as error:
            raise NoticesError(f"{deps} is not valid JSON: {error}.") from error
        libraries = document.get("libraries", {}) if isinstance(document, dict) else {}
        found = sorted(key.removeprefix("runtimepack.") for key, library in libraries.items() if library.get("type") == "runtimepack")
        if not found:
            raise NoticesError(f"{deps} names no runtime pack, so it does not come from a self-contained publish.")
        if packs is None:
            packs = found
        elif found != packs:
            raise NoticesError(f"{deps} names other runtime packs than {deps_files[0]}.")

    entries = []
    for pack in packs or []:
        name, _, version = pack.partition("/")
        label = f"{name} {version}"
        folder = packages / name.lower() / version.lower()
        archive_path = folder / f"{name.lower()}.{version.lower()}.nupkg"
        hash_path = folder / f"{name.lower()}.{version.lower()}.nupkg.sha512"
        if not archive_path.is_file() or not hash_path.is_file():
            raise NoticesError(f"The runtime pack {label} is not restored in {packages}.")
        data = read_file(archive_path)
        expected = read_file(hash_path).decode("ascii", errors="replace").strip()
        if base64.b64encode(hashlib.sha512(data).digest()).decode("ascii") != expected:
            raise NoticesError(f"The restored runtime pack {label} does not match the hash that NuGet recorded for it.")
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            entries.append(entry_from_archive(archive, ".NET runtime", version, label, spdx, [f"Runtime pack: {name} {version}"]))
    return entries


def normalize_copyright(text: str) -> str:
    return " ".join(text.replace(COPYRIGHT_SIGN, "(c)").split()).lower()


def extra_entries(items: list[dict], root: Path) -> list[Entry]:
    """Fonts and icons from the configuration. Each license text must contain its configured copyright line."""
    entries = []
    for item in items:
        path = root / item["path"]
        content = read_pinned(path, item["sha256"]) if "sha256" in item else decode_text(read_file(path))
        label = item["name"]
        if normalize_copyright(item["copyright"]) not in normalize_copyright(content):
            raise NoticesError(f"The license text of {label} in {item['path']} does not contain its copyright line.")
        text = require_content(Text("License text", f"{item['path']} in the Borea repository", content), label)
        entries.append(Entry(label, item.get("version"), item["license"], item["copyright"], [f"Project: {item['link']}"], [text]))
    return entries


def render(entries: list[Entry], version: str, product: str, sbom_name: str) -> str:
    lines = [
        f"Third-party notices for the Borea {version} {PRODUCT_NAMES.get(product, product)} archive",
        "",
        "Borea is licensed under the MIT license in LICENSE.",
        f"This archive also contains the third-party software below. The NuGet packages are the packages in {sbom_name}.",
        "A text that applies to several entries is printed once.",
        "",
        "Contents",
        "",
    ]
    lines.extend(f"  {entry.title} ({entry.license})" for entry in entries)

    printed: dict[str, str] = {}
    for entry in entries:
        lines.extend(["", SEPARATOR, "", entry.title])
        lines.extend(entry.details)
        lines.append(f"License: {entry.license}")
        lines.append(f"Copyright: {entry.copyright or 'the package declares no copyright notice, see the license text'}")
        for text in entry.texts:
            lines.append("")
            key = hashlib.sha256(text.content.encode("utf-8")).hexdigest()
            if key in printed:
                lines.append(f"{text.kind}: {text.source}, the same text as printed above for {printed[key]}.")
                continue
            printed[key] = entry.title
            lines.append(f"{text.kind}: {text.source}")
            if text.note:
                lines.append(text.note)
            lines.extend(["", text.content])
    return "\n".join(lines) + "\n"


def build(args: argparse.Namespace) -> tuple[str, int]:
    try:
        config = json.loads(read_file(args.config).decode("utf-8-sig"))
        spdx = SpdxTexts(config["spdx"], args.root)
        product = config["products"][args.product]
        never_shipped = [str(name) for name in config["never_shipped"]]
        excluded = {name.lower() for name in never_shipped + [str(name) for name in product["excluded"]]}
        required = [str(name) for name in product["required"]]
        extras = product["extras"]
    except ValueError as error:
        raise NoticesError(f"{args.config} is not valid JSON: {error}.") from error
    except (KeyError, TypeError) as error:
        raise NoticesError(f"{args.config} has no valid entry {error} for the product {args.product}.") from error

    try:
        sbom = json.loads(read_file(args.sbom).decode("utf-8-sig"))
        components = sbom.get("components", [])
    except (ValueError, AttributeError) as error:
        raise NoticesError(f"{args.sbom} is not a CycloneDX JSON SBOM.") from error

    for component in components:
        if str(component.get("name", "")).lower() in excluded:
            raise NoticesError(f"The SBOM lists {component.get('name')}, which does not ship.")

    packages = sorted((package_entry(component, args.packages, spdx) for component in components), key=lambda entry: entry.name.lower())
    entries = packages + extra_entries(extras, args.root) + runtime_entries(args.deps, args.packages, spdx)

    names = {entry.name for entry in entries}
    missing = [name for name in required if name not in names]
    if missing:
        raise NoticesError(f"The notices do not name {', '.join(missing)}.")

    return render(entries, args.version, args.product, args.sbom.name), len(entries)


def parse_arguments(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Writes THIRD-PARTY-NOTICES.txt for one Borea release archive.")
    parser.add_argument("--sbom", type=Path, required=True, help="The CycloneDX JSON SBOM of the product.")
    parser.add_argument("--packages", type=Path, required=True, help="The NuGet global packages folder.")
    parser.add_argument("--deps", type=Path, action="append", required=True, help="A deps.json file of the publish. Repeat for each program.")
    parser.add_argument("--config", type=Path, required=True, help="The notices configuration.")
    parser.add_argument("--product", required=True, help="The product in the configuration, App or Cli.")
    parser.add_argument("--version", required=True, help="The release version.")
    parser.add_argument("--output", type=Path, required=True, help="Where to write the notices file.")
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="The repository root.")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_arguments(argv)
    try:
        text, count = build(args)
    except NoticesError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(text, encoding="utf-8", newline="\n")
    print(f"Wrote {args.output} with {count} entries.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
