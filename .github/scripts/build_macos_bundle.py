#!/usr/bin/env python3
"""Puts the published macOS App into an app bundle, Borea.app.

The bundle is what Finder opens, what the Dock shows with the Borea icon, and what Launch
Services calls for borea:// links, which Info.plist declares. The program goes under
Contents/MacOS, and the license, notices and icon go under Contents/Resources, where
codesign seals them by their hash. Signing is not done here, because it needs macOS.
"""

from __future__ import annotations

import argparse
import plistlib
import re
import shutil
import sys
from pathlib import Path

IDENTIFIER = "io.github.ksamodding.borea"
NAME = "Borea"
PROGRAM_NAME = "borea"
ICON_NAME = "borea.icns"
LINK_SCHEME = "borea"
# .NET 10 runs on macOS 15 and later, so an older macOS refuses the bundle with a clear message instead of a crash.
MINIMUM_SYSTEM = "15.0"
COPYRIGHT = "Copyright (c) KSAModding contributors"

VERSION_PATTERN = re.compile(r"^(?P<core>\d+\.\d+\.\d+)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")


class BundleError(Exception):
    """A reason why the bundle cannot be made."""


def bundle_version(version: str) -> str:
    """The part of the version that CFBundleVersion takes: digits and dots only.

    The whole version, pre-release and all, goes to CFBundleShortVersionString.
    """
    match = VERSION_PATTERN.match(version)
    if match is None:
        raise BundleError(f"{version!r} is not a version like 0.5.0 or 0.5.0-beta.1.")
    return match.group("core")


def info_plist(version: str) -> dict:
    return {
        "CFBundleDevelopmentRegion": "en",
        "CFBundleDisplayName": NAME,
        "CFBundleExecutable": PROGRAM_NAME,
        "CFBundleIconFile": ICON_NAME,
        "CFBundleIdentifier": IDENTIFIER,
        "CFBundleInfoDictionaryVersion": "6.0",
        "CFBundleName": NAME,
        "CFBundlePackageType": "APPL",
        "CFBundleShortVersionString": version,
        "CFBundleVersion": bundle_version(version),
        "CFBundleURLTypes": [
            {
                "CFBundleURLName": f"{NAME} link",
                "CFBundleURLSchemes": [LINK_SCHEME],
            }
        ],
        "LSApplicationCategoryType": "public.app-category.utilities",
        "LSMinimumSystemVersion": MINIMUM_SYSTEM,
        "NSHighResolutionCapable": True,
        "NSHumanReadableCopyright": COPYRIGHT,
    }


def build(program: Path, version: str, icon: Path, resources: list[Path], output: Path) -> None:
    if output.exists():
        raise BundleError(f"{output} exists already.")
    if output.suffix != ".app":
        raise BundleError(f"{output} does not end in .app.")
    if not program.is_file():
        raise BundleError(f"{program} is not a file.")
    if not icon.is_file() or icon.suffix != ".icns":
        raise BundleError(f"{icon} is not an .icns file.")
    names = set()
    for resource in resources:
        if not resource.is_file() or resource.stat().st_size == 0:
            raise BundleError(f"{resource} is not a file or is empty.")
        if resource.name in names or resource.name == ICON_NAME:
            raise BundleError(f"Two resources are named {resource.name}.")
        names.add(resource.name)
    plist = info_plist(version)

    contents = output / "Contents"
    programs = contents / "MacOS"
    resources_folder = contents / "Resources"
    programs.mkdir(parents=True)
    resources_folder.mkdir()
    shutil.copyfile(program, programs / PROGRAM_NAME)
    (programs / PROGRAM_NAME).chmod(0o755)
    shutil.copyfile(icon, resources_folder / ICON_NAME)
    for resource in resources:
        shutil.copyfile(resource, resources_folder / resource.name)
    with open(contents / "Info.plist", "wb") as file:
        plistlib.dump(plist, file, sort_keys=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--program", required=True, type=Path, help="the published borea program")
    parser.add_argument("--version", required=True, help="the version of this release, such as 0.5.0-beta.1")
    parser.add_argument("--icon", required=True, type=Path, help="the icon as .icns")
    parser.add_argument("--resource", action="append", default=[], type=Path, help="a text file for Contents/Resources, such as LICENSE")
    parser.add_argument("--output", required=True, type=Path, help="the bundle to make, such as Borea.app")
    arguments = parser.parse_args(argv)
    try:
        build(arguments.program, arguments.version, arguments.icon, arguments.resource, arguments.output)
    except BundleError as error:
        print(f"build_macos_bundle: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
