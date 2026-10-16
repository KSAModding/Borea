"""Tests for build_macos_bundle.py. They run on every system, because the script signs nothing."""

from __future__ import annotations

import contextlib
import io
import plistlib
import stat
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import build_macos_bundle as bundle  # noqa: E402


class BuildMacOsBundle(unittest.TestCase):
    def setUp(self):
        self._folder = tempfile.TemporaryDirectory()
        self.folder = Path(self._folder.name)
        self.program = self.write("publish/borea", b"program")
        self.icon = self.write("borea.icns", b"icns")
        self.license = self.write("publish/LICENSE", b"MIT")
        self.notices = self.write("publish/THIRD-PARTY-NOTICES.txt", b"notices")
        self.output = self.folder / "Borea.app"

    def tearDown(self):
        self._folder.cleanup()

    def write(self, name, content):
        path = self.folder / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        return path

    def run_script(self, *extra):
        arguments = ["--program", str(self.program), "--version", "0.5.0-beta.1", "--icon", str(self.icon), "--output", str(self.output)]
        arguments += ["--resource", str(self.license), "--resource", str(self.notices)]
        errors = io.StringIO()
        with contextlib.redirect_stderr(errors):
            status = bundle.main(arguments + list(extra))
        return status, errors.getvalue()

    def test_the_bundle_holds_the_program_the_resources_and_the_plist(self):
        status, errors = self.run_script()

        self.assertEqual((0, ""), (status, errors))
        found = sorted(str(path.relative_to(self.output)) for path in self.output.rglob("*"))
        self.assertEqual(
            [
                "Contents",
                "Contents/Info.plist",
                "Contents/MacOS",
                "Contents/MacOS/borea",
                "Contents/Resources",
                "Contents/Resources/LICENSE",
                "Contents/Resources/THIRD-PARTY-NOTICES.txt",
                "Contents/Resources/borea.icns",
            ],
            found,
        )
        program = self.output / "Contents" / "MacOS" / "borea"
        self.assertEqual(b"program", program.read_bytes())
        self.assertTrue(program.stat().st_mode & stat.S_IXUSR, "the program is not executable")
        self.assertEqual(b"MIT", (self.output / "Contents" / "Resources" / "LICENSE").read_bytes())

    def test_the_plist_names_the_program_the_icon_the_versions_and_the_link_scheme(self):
        self.run_script()

        with open(self.output / "Contents" / "Info.plist", "rb") as file:
            plist = plistlib.load(file)
        self.assertEqual("borea", plist["CFBundleExecutable"])
        self.assertEqual("borea.icns", plist["CFBundleIconFile"])
        self.assertEqual("io.github.ksamodding.borea", plist["CFBundleIdentifier"])
        self.assertEqual("APPL", plist["CFBundlePackageType"])
        self.assertEqual("0.5.0-beta.1", plist["CFBundleShortVersionString"])
        self.assertEqual("0.5.0", plist["CFBundleVersion"])
        self.assertEqual([["borea"]], [kind["CFBundleURLSchemes"] for kind in plist["CFBundleURLTypes"]])
        self.assertEqual("15.0", plist["LSMinimumSystemVersion"])
        self.assertTrue(plist["NSHighResolutionCapable"])

    def test_bundle_version_keeps_only_the_numbers(self):
        self.assertEqual("0.5.0", bundle.bundle_version("0.5.0"))
        self.assertEqual("0.5.0", bundle.bundle_version("0.5.0-dev.3"))
        self.assertEqual("0.0.0", bundle.bundle_version("0.0.0-dev"))
        self.assertEqual("1.2.3", bundle.bundle_version("1.2.3+abc"))
        for bad in ["", "0.5", "v0.5.0", "0.5.0 beta", "0.5.0-"]:
            with self.subTest(version=bad):
                with self.assertRaises(bundle.BundleError):
                    bundle.bundle_version(bad)

    def test_a_bad_version_fails_before_anything_is_written(self):
        status, errors = self.run_script("--version", "v0.5.0")

        self.assertEqual(1, status)
        self.assertIn("not a version", errors)
        self.assertFalse(self.output.exists())

    def test_an_empty_resource_fails(self):
        empty = self.write("publish/BRANDING.md", b"")

        status, errors = self.run_script("--resource", str(empty))

        self.assertEqual(1, status)
        self.assertIn("BRANDING.md", errors)
        self.assertFalse(self.output.exists())

    def test_a_missing_program_fails(self):
        self.program.unlink()

        status, errors = self.run_script()

        self.assertEqual(1, status)
        self.assertIn("borea", errors)
        self.assertFalse(self.output.exists())

    def test_an_icon_that_is_no_icns_fails(self):
        self.icon = self.write("borea.png", b"png")

        status, errors = self.run_script()

        self.assertEqual(1, status)
        self.assertIn(".icns", errors)
        self.assertFalse(self.output.exists())

    def test_an_existing_output_is_not_overwritten(self):
        self.output.mkdir()
        (self.output / "keep").write_bytes(b"x")

        status, errors = self.run_script()

        self.assertEqual(1, status)
        self.assertIn("exists already", errors)
        self.assertEqual(b"x", (self.output / "keep").read_bytes())

    def test_an_output_that_is_no_bundle_fails(self):
        self.output = self.folder / "Borea"

        status, errors = self.run_script()

        self.assertEqual(1, status)
        self.assertIn(".app", errors)


if __name__ == "__main__":
    unittest.main()
