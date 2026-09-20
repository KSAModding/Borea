"""States the single file settings of the published programs, read from MSBuild.

macOS crashes with an AccessViolationException when a self-contained single
file is compressed (dotnet/runtime#123324), so compression must stay off for
every macOS runtime and on everywhere else.
"""

from __future__ import annotations

import json
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PROJECTS = ["src/Borea.App/Borea.App.csproj", "src/Borea.Cli/Borea.Cli.csproj"]


def properties(project, names, runtime=None):
    command = ["dotnet", "msbuild", str(ROOT / project), "-nologo", "-p:Configuration=Release"]
    command += [f"-getProperty:{name}" for name in names]
    if runtime is not None:
        command.append(f"-p:RuntimeIdentifier={runtime}")
    result = subprocess.run(command, capture_output=True, text=True, cwd=ROOT, check=False)
    if result.returncode != 0:
        raise AssertionError(f"{' '.join(command)} failed: {result.stdout} {result.stderr}")
    text = result.stdout.strip()
    if len(names) == 1:
        return {names[0]: text}
    return json.loads(text)["Properties"]


class SingleFileSettings(unittest.TestCase):
    def test_every_published_runtime_is_one_single_file(self):
        for project in PROJECTS:
            with self.subTest(project=project):
                values = properties(project, ["PublishSingleFile", "RuntimeIdentifiers"])
                self.assertEqual("true", values["PublishSingleFile"].lower())
                self.assertTrue(values["RuntimeIdentifiers"], "the project names no runtime")

    def test_compression_is_off_on_macos_and_on_everywhere_else(self):
        for project in PROJECTS:
            runtimes = properties(project, ["RuntimeIdentifiers"])["RuntimeIdentifiers"].split(";")
            for runtime in [runtime for runtime in runtimes if runtime]:
                with self.subTest(project=project, runtime=runtime):
                    value = properties(project, ["EnableCompressionInSingleFile"], runtime)
                    compressed = value["EnableCompressionInSingleFile"].lower() == "true"
                    if runtime.startswith("osx"):
                        self.assertFalse(compressed, f"{runtime} compresses the single file, see dotnet/runtime#123324")
                    else:
                        self.assertTrue(compressed, f"{runtime} does not compress the single file")


if __name__ == "__main__":
    unittest.main()
