# Borea
Borea is a cross-platform complete general content manager for Kitten Space Agency. It manages mods, mod packs, vehicles, game saves, and more. It is intended to be modifiable by changing out `Borea.Storage`, `Borea.Network`, and `Borea.App` so user can customize Borea. This repository will contain all the offical Borea files and releases.

# Downloads
Each release has one build per platform.

The builds include the .NET runtime and are self-contained, so there is nothing you need to install first.

| Platform | File |
| --- | --- |
| Windows | `Borea-<version>-win-x64.zip` |
| Linux | `Borea-<version>-linux-x64.tar.gz` |
| macOS, Intel | `Borea-<version>-osx-x64.tar.gz` |
| macOS, Apple silicon | `Borea-<version>-osx-arm64.tar.gz` |

The builds are not code signed, so the first start of each update takes an extra step on Windows and macOS.
Signing will be added at some point and is tracked in [issue #76](https://github.com/KSAModding/Borea/issues/76).

### Windows
1. Your browser might warn you that the file is not commonly downloaded. Keep it.
2. Before you unpack it, right-click the zip, open **Properties**, select **Unblock** on the **General** tab and confirm with **OK**.
3. Unpack the zip and start `Borea.App.exe`.
4. If you skipped step 2, Windows shows "Windows protected your PC". The reason is that it detects that the App is not commonly downloaded and not signed. Select **More info**, then **Run anyway**.
5. If Windows says that Smart App Control blocked Borea, there is no "Run anyway". Go back to step 2, unblock the zip, and unpack it again into a new folder.

The warning comes back with every new version.
Borea does not need administrator rights.

### Linux
Unpack the archive and start `Borea.App`.
The build carries the .NET runtime but not the system libraries it sits on.
All common desktop installation usually have them all. A minimal one needs:

- Debian and Ubuntu: `sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libssl3` plus the `libicu` package of your release, for example `libicu76`.
- Fedora: `sudo dnf install libX11 libICE libSM fontconfig libicu openssl-libs`.

The build needs glibc, so musl-based distributions such as Alpine are not supported.

### macOS
Unpack the archive and start Borea from Terminal:

```sh
tar -xzf Borea-<version>-osx-arm64.tar.gz
cd Borea-<version>-osx-arm64
./Borea.App
```

On an Intel Mac, use the `osx-x64` archive instead.

Do not unpack the archive by double-clicking it in Finder, and do not start `Borea.App` from Finder.

If that already happened, remove the download mark and start Borea from Terminal again: `xattr -dr com.apple.quarantine Borea-<version>-osx-arm64`.

Once it runs, Borea behaves like any other Mac program.

### Checksums and provenance

`SHA256SUMS.txt` in each release lists the checksum of every archive.

GitHub also holds a build provenance attestation for every published file, which ties it to the workflow run that built it.

With `gh` signed in, check one like this:

```sh
gh attestation verify <file> --repo KSAModding/Borea \
  --signer-workflow KSAModding/Borea/.github/workflows/release.yml
```

`Borea-<version>.cdx.json` is the software bill of materials, in CycloneDX JSON.
It includes the NuGet packages Borea.App is built from, with version, license and hash.
One list covers all four builds, so it holds the native asset packages of every platform.
The same list is attested to each archive, and you can use this command to prove that it belongs to one.
With `--format json`, the output includes the attested list.

```sh
gh attestation verify <archive> --repo KSAModding/Borea \
  --signer-workflow KSAModding/Borea/.github/workflows/release.yml \
  --predicate-type https://cyclonedx.org/bom
```

# Credits
- [MrJeranimo](https://github.com/MrJeranimo) - Original Creator and Developer

# Repository Structure
| Path | Description |
| --- | --- |
| `src` | Holds the source files for Borea |
| `test`| Holds the test files for Borea |
| `src\Borea.Core` | Contains all the core information about Borea's mods, mod packs, path providers, and more. Also contains the required interfaces to make a project compatible with Borea. |
| `src\Borea.Storage` | Contains all the code for storing the data from `Borea.Core` to the disk. |
| `src\Borea.Network` | Contains all the code for retrieving content from mod/content indexers and saves them to the disk. |
| `src\Borea.App` | A desktop level application that the user will interact will. Combines `Borea.Core`, `Borea.Storage`, and `Borea.Network` and handles the cross project references to make the full Borea project work smoothly. |

# Features
tba

# Roadmap
### Borea Pre-Release
- Mod Downloads
- Mod Packs

### Borea 1.0
- Saves
- Vehicles

### Once KSA supports it
- Multiplayer server setup
