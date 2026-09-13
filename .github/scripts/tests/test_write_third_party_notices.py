"""Tests for write_third_party_notices.py."""

from __future__ import annotations

import base64
import contextlib
import hashlib
import io
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parents[1]
REPOSITORY = SCRIPTS.parents[1]
sys.path.insert(0, str(SCRIPTS))

import write_third_party_notices as notices  # noqa: E402

CONFIG = REPOSITORY / ".github" / "notices" / "notices.json"
SKIA_LICENSE = "Copyright (c) 2015-2016 Xamarin, Inc.\r\n\r\nPermission is hereby granted, free of charge.\r\n"
SKIA_NOTICES = "Skia notices\nHarfBuzz notices\n"


def nuspec(name: str, version: str, license: str | None, license_type: str, copyright: str | None, license_url: str | None) -> str:
    parts = [f"<id>{name}</id>", f"<version>{version}</version>"]
    if license is not None:
        parts.append(f'<license type="{license_type}">{license}</license>')
    if license_url is not None:
        parts.append(f"<licenseUrl>{license_url}</licenseUrl>")
    if copyright is not None:
        parts.append(f"<copyright>{copyright}</copyright>")
    return (
        '<?xml version="1.0" encoding="utf-8"?>'
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
        f"<metadata>{''.join(parts)}</metadata></package>"
    )


def archive(files: dict[str, str | bytes]) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as package:
        for name, content in files.items():
            package.writestr(name, content)
    return buffer.getvalue()


def changed_config(root: Path, change) -> Path:
    config = json.loads(CONFIG.read_text())
    change(config)
    path = root / "changed-notices.json"
    path.write_text(json.dumps(config))
    return path


class Fixture:
    """A NuGet global packages folder, a publish output and an SBOM in a temporary folder."""

    def __init__(self, root: Path):
        self.root = root
        self.packages = root / "packages"
        self.components: list[dict] = []
        self.deps: list[Path] = []

    def add_package(
        self,
        name: str,
        version: str,
        license: str | None = "MIT",
        license_type: str = "expression",
        copyright: str | None = "Copyright (c) Example Authors",
        license_url: str | None = None,
        files: dict[str, str | bytes] | None = None,
    ) -> Path:
        content: dict[str, str | bytes] = {f"{name}.nuspec": nuspec(name, version, license, license_type, copyright, license_url), "lib/net8.0/Example.dll": "binary"}
        content.update(files or {})
        data = archive(content)
        folder = self.packages / name.lower() / version.lower()
        folder.mkdir(parents=True)
        path = folder / f"{name.lower()}.{version.lower()}.nupkg"
        path.write_bytes(data)
        self.components.append(
            {
                "bom-ref": f"pkg:nuget/{name}@{version}",
                "name": name,
                "version": version,
                "purl": f"pkg:nuget/{name}@{version}",
                "hashes": [{"alg": "SHA-512", "content": hashlib.sha512(data).hexdigest()}],
            }
        )
        return path

    def add_runtime(self, programs: tuple[str, ...] = ("borea",), rid: str = "linux-x64", version: str = "10.0.12") -> None:
        name = f"Microsoft.NETCore.App.Runtime.{rid}"
        data = archive(
            {
                f"{name}.nuspec": nuspec(name, version, "MIT", "expression", "Microsoft Corporation. All rights reserved.", None),
                "LICENSE.TXT": "The MIT License (MIT)\n\nCopyright (c) .NET Foundation and Contributors\n",
                "THIRD-PARTY-NOTICES.TXT": ".NET Runtime uses third-party libraries or other resources.\n",
                "runtimes/linux-x64/lib/net10.0/System.Private.CoreLib.dll": "binary",
            }
        )
        folder = self.packages / name.lower() / version
        folder.mkdir(parents=True)
        (folder / f"{name.lower()}.{version}.nupkg").write_bytes(data)
        (folder / f"{name.lower()}.{version}.nupkg.sha512").write_text(base64.b64encode(hashlib.sha512(data).digest()).decode("ascii"))
        publish = self.root / "publish"
        publish.mkdir(exist_ok=True)
        for program in programs:
            deps = publish / f"{program}.deps.json"
            deps.write_text(json.dumps({"libraries": {f"runtimepack.{name}/{version}": {"type": "runtimepack"}, "Tomlyn/2.10.1": {"type": "package"}}}))
            self.deps.append(deps)

    def run(self, product: str = "Cli", config: Path = CONFIG) -> tuple[int, str | None, str]:
        sbom = self.root / f"Borea-{product}-1.0.0.cdx.json"
        sbom.write_text(json.dumps({"bomFormat": "CycloneDX", "components": self.components}))
        output = self.root / "out" / "THIRD-PARTY-NOTICES.txt"
        argv = [
            "--sbom", str(sbom),
            "--packages", str(self.packages),
            "--config", str(config),
            "--root", str(REPOSITORY),
            "--product", product,
            "--version", "1.0.0",
            "--output", str(output),
        ]
        for deps in self.deps:
            argv += ["--deps", str(deps)]
        errors = io.StringIO()
        with contextlib.redirect_stderr(errors), contextlib.redirect_stdout(io.StringIO()):
            code = notices.main(argv)
        return code, output.read_text(encoding="utf-8") if output.exists() else None, errors.getvalue()


class WriteThirdPartyNoticesTests(unittest.TestCase):
    def setUp(self) -> None:
        self._folder = tempfile.TemporaryDirectory()
        self.fixture = Fixture(Path(self._folder.name))

    def tearDown(self) -> None:
        self._folder.cleanup()

    def cli_packages(self) -> None:
        self.fixture.add_package("System.CommandLine", "2.0.11", copyright="Microsoft Corporation. All rights reserved.")
        self.fixture.add_package("Tomlyn", "2.10.1", license="BSD-2-Clause", copyright="Alexandre Mutel")
        self.fixture.add_runtime()

    def app_packages(self) -> None:
        self.fixture.add_package("Avalonia", "12.1.2", copyright="Copyright 2013-2026 The AvaloniaUI Project")
        self.fixture.add_package("CommunityToolkit.Mvvm", "8.4.2", files={"License.md": "# .NET Community Toolkit\n\nMIT License\n"})
        self.fixture.add_package("SkiaSharp", "3.119.4", files={"LICENSE.txt": SKIA_LICENSE})
        self.fixture.add_package("SkiaSharp.NativeAssets.Linux", "3.119.4", files={"LICENSE.txt": SKIA_LICENSE, "THIRD-PARTY-NOTICES.txt": SKIA_NOTICES})
        self.fixture.add_package("SkiaSharp.NativeAssets.Win32", "3.119.4", files={"LICENSE.txt": SKIA_LICENSE, "THIRD-PARTY-NOTICES.txt": SKIA_NOTICES})
        self.fixture.add_package("Avalonia.Angle.Windows.Natives", "2.1.27548.20260419", license="LICENSE", license_type="file", files={"LICENSE": "// Copyright 2018 The ANGLE Project Authors.\n"})
        self.fixture.add_package("System.CommandLine", "2.0.11")
        self.fixture.add_package("Tomlyn", "2.10.1", license="BSD-2-Clause", copyright="Alexandre Mutel")
        self.fixture.add_runtime(programs=("Borea.App", "borea"))

    def test_cli_file_names_system_commandline_tomlyn_and_the_runtime(self) -> None:
        self.cli_packages()

        code, text, errors = self.fixture.run("Cli")

        self.assertEqual(0, code, errors)
        self.assertIn("Third-party notices for the Borea 1.0.0 CLI archive", text)
        self.assertIn("\nSystem.CommandLine 2.0.11\n", text)
        self.assertIn("\nTomlyn 2.10.1\nNuGet package: https://www.nuget.org/packages/Tomlyn/2.10.1\nLicense: BSD-2-Clause\nCopyright: Alexandre Mutel\n", text)
        self.assertIn("License text: the standard text of BSD-2-Clause from the SPDX License List 3.28.0", text)
        self.assertIn("Redistributions in binary form must reproduce the above copyright notice", text)
        self.assertIn("\n.NET runtime 10.0.12\nRuntime pack: Microsoft.NETCore.App.Runtime.linux-x64 10.0.12\n", text)
        self.assertIn("Notices: THIRD-PARTY-NOTICES.TXT in the package", text)
        self.assertNotIn("Avalonia", text)
        self.assertNotIn("IBM Plex", text)
        self.assertNotIn("Phosphor", text)

    def test_app_file_names_avalonia_tomlyn_ibm_plex_and_phosphor(self) -> None:
        self.app_packages()

        code, text, errors = self.fixture.run("App")

        self.assertEqual(0, code, errors)
        for title in ("Avalonia 12.1.2", "Tomlyn 2.10.1", "System.CommandLine 2.0.11", "IBM Plex", "Phosphor Icons", ".NET runtime 10.0.12"):
            self.assertIn(f"\n{title}\n", text)
        self.assertIn("Copyright: Copyright (c) 2017 IBM Corp. with Reserved Font Name \"Plex\"", text)
        self.assertIn("SIL OPEN FONT LICENSE Version 1.1", text)
        self.assertIn("Copyright (c) 2023 Phosphor Icons", text)
        self.assertIn("License: license file LICENSE", text)
        self.assertIn("// Copyright 2018 The ANGLE Project Authors.", text)

    def test_package_license_file_wins_over_the_standard_text(self) -> None:
        self.app_packages()

        code, text, errors = self.fixture.run("App")

        self.assertEqual(0, code, errors)
        skia = text.split("\nSkiaSharp 3.119.4\n", 1)[1].split("=" * 80, 1)[0]
        self.assertIn("License text: LICENSE.txt in the package", skia)
        self.assertIn("Copyright (c) 2015-2016 Xamarin, Inc.", skia)
        self.assertNotIn("SPDX", skia)
        self.assertNotIn("\r", text)

    def test_same_text_is_printed_once(self) -> None:
        self.app_packages()

        code, text, errors = self.fixture.run("App")

        self.assertEqual(0, code, errors)
        self.assertEqual(1, text.count("Skia notices"))
        self.assertEqual(1, text.count("Copyright (c) 2015-2016 Xamarin, Inc."))
        self.assertIn("Notices: THIRD-PARTY-NOTICES.txt in the package, the same text as printed above for SkiaSharp.NativeAssets.Linux 3.119.4.", text)

    def test_utf16_license_file_is_decoded(self) -> None:
        self.cli_packages()
        self.fixture.add_package("Utf16.Package", "1.0.0", files={"LICENSE.txt": (chr(0xFEFF) + "Copyright (c) UTF-16 Authors\r\n").encode("utf-16-le")})

        code, text, errors = self.fixture.run("Cli")

        self.assertEqual(0, code, errors)
        self.assertIn("\nCopyright (c) UTF-16 Authors\n", text)
        self.assertNotIn("\x00", text)

    def test_license_file_with_nul_characters_fails(self) -> None:
        self.cli_packages()
        self.fixture.add_package("Binary.Package", "1.0.0", files={"LICENSE": b"C\x00o\x00p\x00y\x00"})

        code, text, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIsNone(text)
        self.assertIn("LICENSE in the package of Binary.Package 1.0.0 contains NUL characters", errors)

    def test_package_without_a_license_text_fails(self) -> None:
        self.cli_packages()
        self.fixture.add_package("Legacy.Package", "1.0.0", license=None, license_url="https://example.com/license")

        code, text, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIsNone(text)
        self.assertIn("Legacy.Package 1.0.0 contains no license file and declares no SPDX license expression", errors)

    def test_standard_text_without_a_copyright_notice_fails(self) -> None:
        self.cli_packages()
        self.fixture.add_package("Anonymous.Package", "1.0.0", copyright=None)

        code, text, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIsNone(text)
        self.assertIn("Anonymous.Package 1.0.0 declares only the license expression MIT and no copyright notice", errors)

    def test_license_file_without_a_copyright_notice_is_accepted(self) -> None:
        self.cli_packages()
        self.fixture.add_package("Filed.Package", "1.0.0", copyright=None, files={"LICENSE.txt": "Copyright (c) Filed Authors\n"})

        code, text, errors = self.fixture.run("Cli")

        self.assertEqual(0, code, errors)
        self.assertIn("Copyright: the package declares no copyright notice, see the license text", text)

    def test_license_file_missing_from_the_package_fails(self) -> None:
        self.cli_packages()
        self.fixture.add_package("Broken.Package", "1.0.0", license="docs/LICENSE.txt", license_type="file")

        code, _, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIn("names docs/LICENSE.txt as its license file, but the package does not contain it", errors)

    def test_license_without_a_pinned_standard_text_fails(self) -> None:
        self.cli_packages()
        self.fixture.add_package("Apache.Package", "1.0.0", license="Apache-2.0")

        code, _, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIn("declares the license Apache-2.0, but the notices configuration has no pinned standard text for it", errors)

    def test_changed_standard_text_fails(self) -> None:
        self.cli_packages()
        changed = self.fixture.root / "MIT.txt"
        changed.write_text((REPOSITORY / ".github" / "notices" / "spdx" / "MIT.txt").read_text() + "\nOne more sentence.\n")

        def point_mit_at_the_changed_text(config: dict) -> None:
            config["spdx"]["texts"]["MIT"]["path"] = str(changed)

        code, _, errors = self.fixture.run("Cli", config=changed_config(self.fixture.root, point_mit_at_the_changed_text))

        self.assertEqual(1, code)
        self.assertIn("does not match its pinned SHA-256 hash", errors)

    def test_archive_that_does_not_match_the_sbom_fails(self) -> None:
        self.cli_packages()
        path = self.fixture.add_package("Changed.Package", "1.0.0")
        path.write_bytes(path.read_bytes() + b"changed")

        code, _, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIn("The restored archive of Changed.Package 1.0.0 does not match the SHA-512 hash in the SBOM", errors)

    def test_package_that_is_not_restored_fails(self) -> None:
        self.cli_packages()
        self.fixture.components.append({"name": "Missing.Package", "version": "1.0.0", "purl": "pkg:nuget/Missing.Package@1.0.0", "hashes": [{"alg": "SHA-512", "content": "00"}]})

        code, _, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIn("Missing.Package 1.0.0 is not restored", errors)

    def test_test_and_debug_only_packages_fail(self) -> None:
        never_shipped = json.loads(CONFIG.read_text())["never_shipped"]
        self.assertIn("AvaloniaUI.DiagnosticsSupport", never_shipped)
        self.assertIn("xunit", never_shipped)
        for name in never_shipped:
            with self.subTest(name=name):
                fixture = Fixture(self.fixture.root / name)
                fixture.add_package("Tomlyn", "2.10.1", license="BSD-2-Clause")
                fixture.add_package(name, "1.0.0")
                fixture.add_runtime()

                code, text, errors = fixture.run("App")

                self.assertEqual(1, code)
                self.assertIsNone(text)
                self.assertIn(f"The SBOM lists {name}, which does not ship.", errors)

    def test_package_excluded_from_the_product_fails(self) -> None:
        self.cli_packages()
        self.fixture.add_package("Avalonia", "12.1.2")

        code, _, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIn("The SBOM lists Avalonia, which does not ship.", errors)

    def test_missing_required_entry_fails(self) -> None:
        self.cli_packages()

        def require_avalonia(config: dict) -> None:
            config["products"]["Cli"]["required"].append("Avalonia")
            config["products"]["Cli"]["excluded"] = []

        code, _, errors = self.fixture.run("Cli", config=changed_config(self.fixture.root, require_avalonia))

        self.assertEqual(1, code)
        self.assertIn("The notices do not name Avalonia.", errors)

    def test_publish_without_a_runtime_pack_fails(self) -> None:
        self.fixture.add_package("System.CommandLine", "2.0.11")
        self.fixture.add_package("Tomlyn", "2.10.1", license="BSD-2-Clause")
        deps = self.fixture.root / "borea.deps.json"
        deps.write_text(json.dumps({"libraries": {"Tomlyn/2.10.1": {"type": "package"}}}))
        self.fixture.deps.append(deps)

        code, _, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIn("names no runtime pack", errors)

    def test_programs_with_different_runtime_packs_fail(self) -> None:
        self.cli_packages()
        other = self.fixture.root / "Borea.App.deps.json"
        other.write_text(json.dumps({"libraries": {"runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/10.0.11": {"type": "runtimepack"}}}))
        self.fixture.deps.append(other)

        code, _, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIn("names other runtime packs", errors)

    def test_changed_runtime_pack_fails(self) -> None:
        self.cli_packages()
        pack = next(self.fixture.packages.glob("microsoft.netcore.app.runtime.linux-x64/*/*.nupkg"))
        pack.write_bytes(pack.read_bytes() + b"changed")

        code, _, errors = self.fixture.run("Cli")

        self.assertEqual(1, code)
        self.assertIn("does not match the hash that NuGet recorded for it", errors)

    def test_extra_entry_with_a_wrong_copyright_line_fails(self) -> None:
        self.cli_packages()

        def give_the_cli_a_wrong_phosphor_line(config: dict) -> None:
            phosphor = dict(config["products"]["App"]["extras"][1], copyright="Copyright (c) 2020 Someone Else")
            config["products"]["Cli"]["extras"] = [phosphor]

        code, _, errors = self.fixture.run("Cli", config=changed_config(self.fixture.root, give_the_cli_a_wrong_phosphor_line))

        self.assertEqual(1, code)
        self.assertIn("does not contain its copyright line", errors)


if __name__ == "__main__":
    unittest.main()
