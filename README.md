# Borea

Borea is a cross-platform complete general content manager for Kitten Space Agency. It manages mods, mod packs, vehicles, game saves, and more. It is intended to be modifiable by changing out `Borea.Storage`, `Borea.Network`, and `Borea.App` so user can customize Borea. This repository will contain all the offical Borea files and releases.

See [CONTRIBUTING.md](CONTRIBUTING.md) to contribute and [SECURITY.md](SECURITY.md) to report a security problem.

## Downloads

Each release has two archives per platform.
The App archive contains the desktop App and the `borea` command. Most users want this archive.
The CLI archive contains only the `borea` command. It is a smaller download for scripts and for computers without a desktop.
The `borea` command in both archives is the same build.

The builds include the .NET runtime and are self-contained, so there is nothing you need to install first.

Each archive also contains `LICENSE` and `THIRD-PARTY-NOTICES.txt` with the licenses of the third-party software in it.

| Platform | App and CLI | CLI only |
| --- | --- | --- |
| Windows | `Borea-<version>-win-x64.zip` | `Borea-Cli-<version>-win-x64.zip` |
| Linux | `Borea-<version>-linux-x64.tar.gz` | `Borea-Cli-<version>-linux-x64.tar.gz` |
| macOS, Intel | `Borea-<version>-osx-x64.tar.gz` | `Borea-Cli-<version>-osx-x64.tar.gz` |
| macOS, Apple silicon | `Borea-<version>-osx-arm64.tar.gz` | `Borea-Cli-<version>-osx-arm64.tar.gz` |

The builds are not code signed, so the first start of each update takes an extra step on Windows and macOS.
Signing will be added at some point and is tracked in [issue #76](https://github.com/KSAModding/Borea/issues/76).

### Windows

1. Your browser might warn you that the file is not commonly downloaded. Keep it.
2. Before you unpack it, right-click the zip, open **Properties**, select **Unblock** on the **General** tab and confirm with **OK**.
3. Unpack the zip and start `Borea.App.exe`. For the command line, run `.\borea.exe --help` in PowerShell in the unpacked folder of either archive.
4. If you skipped step 2, Windows shows "Windows protected your PC". The reason is that it detects that the App is not commonly downloaded and not signed. Select **More info**, then **Run anyway**.
5. If Windows says that Smart App Control blocked Borea, there is no "Run anyway". Go back to step 2, unblock the zip, and unpack it again into a new folder.

The warning comes back with every new version.
Borea does not need administrator rights.

### Linux

Unpack the App archive and start `Borea.App`.
For the command line, run `./borea --help` in the unpacked folder of either archive.
The build carries the .NET runtime but not the system libraries it sits on.
The App and the CLI both need the ICU and OpenSSL libraries.
Only the App also needs the X11, ICE, SM and fontconfig libraries.
All common desktop installation usually have them all. A minimal one needs these packages for the App:

- Debian and Ubuntu: `sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libssl3` plus the `libicu` package of your release, for example `libicu76`.
- Fedora: `sudo dnf install libX11 libICE libSM fontconfig libicu openssl-libs`.

For the CLI alone, install only `libssl3` and the `libicu` package on Debian and Ubuntu, or `libicu openssl-libs` on Fedora.

The build needs glibc, so musl-based distributions such as Alpine are not supported.

### macOS

Unpack the archive and start Borea from Terminal:

```sh
tar -xzf Borea-<version>-osx-arm64.tar.gz
cd Borea-<version>-osx-arm64
./Borea.App
```

On an Intel Mac, use the `osx-x64` archive instead.

For the command line, run `./borea --help` in the same directory.
The matching `Borea-Cli-` archive contains only this command.

Do not unpack the archive by double-clicking it in Finder, and do not start `Borea.App` from Finder.

If that already happened, remove the download mark and start Borea from Terminal again: `xattr -dr com.apple.quarantine Borea-<version>-osx-arm64`.

Once it runs, Borea behaves like any other Mac program.

### Checksums and provenance

`SHA256SUMS.txt` in each release lists the checksum of every archive and every software bill of materials.

GitHub also holds a build provenance attestation for every published file, which ties it to the workflow run that built it.

With `gh` signed in, check one like this:

```sh
gh attestation verify <file> --repo KSAModding/Borea \
  --signer-workflow KSAModding/Borea/.github/workflows/release.yml
```

`Borea-<version>.cdx.json` is the software bill of materials for the App archives, in CycloneDX JSON.
The App archives also contain `borea`, so this file lists the NuGet packages of the App and of the CLI.
`Borea-Cli-<version>.cdx.json` is the software bill of materials for the CLI archives and lists only the packages of the CLI.
Each file includes the version, license and hash of every package.
Each list is attested only to the archives that it describes, and you can use this command to prove that it belongs to one.
With `--format json`, the output includes the attested list.

```sh
gh attestation verify <archive> --repo KSAModding/Borea \
  --signer-workflow KSAModding/Borea/.github/workflows/release.yml \
  --predicate-type https://cyclonedx.org/bom
```

## Credits

- [MrJeranimo](https://github.com/MrJeranimo) - Original Creator and Developer

## Contributing translations

You do not need to write C# to improve an existing translation. See the [localization guide](docs/localization.md) for instructions to correct text or propose a new language.

## Repository Structure

| Path | Description |
| --- | --- |
| `src` | Holds the source files for Borea |
| `test`| Holds the test files for Borea |
| `src\Borea.Core` | Contains all the core information about Borea's mods, mod packs, path providers, and more. Also contains the required interfaces to make a project compatible with Borea. |
| `src\Borea.Storage` | Contains all the code for storing the data from `Borea.Core` to the disk. |
| `src\Borea.Network` | Contains all the code for retrieving content from mod/content indexers and saves them to the disk. |
| `src\Borea.Composition` | The composition root. Builds every service from the saved settings, once, for `Borea.App` and `Borea.Cli`. |
| `src\Borea.App` | A desktop level application that the user will interact with. Gets its services from `Borea.Composition`. |
| `src\Borea.Cli` | The command line interface, `borea`. A thin wrapper over the same services, for scripts and for machines without a desktop. |

## Command line

`borea` runs Borea's operations from a script. Every read command takes `--json`.
The exit code is 0 when the command completed, 1 when the operation failed and the reason is on stderr, and 2 when the command line did not parse.

| Command | Does |
| --- | --- |
| `borea settings show` | Print where the game and the mod loaders are, and the release channel. |
| `borea settings set game <directory>` | Point Borea at the game installation. |
| `borea settings set loader <loader-id> <directory>` | Point Borea at an installed mod loader. |
| `borea settings set channel <channel>` | Choose which release statuses install and update offer: `stable` (the default), `testing` or `dev`. |
| `borea game version` | Print the installed build and the current public build the master server reports. |
| `borea instance list` | Print every instance and mark the active one. |
| `borea instance create <name>` | Create an empty instance. |
| `borea instance rename <instance> <new-name>` | Give an instance a new name. |
| `borea instance delete <instance>` | Delete an instance and its folder. |
| `borea instance activate <instance>` | Make an instance the active one. |
| `borea instance scan <instance>` | Print the mod folders that Borea did not install, and whether the content index lists them. |
| `borea instance adopt <instance> <folder> --archive <path>` | Record a mod folder that Borea did not install as the index release its archive matches. |
| `borea enable <mod-id> [--instance <instance>]` | Make the game load a mod. |
| `borea disable <mod-id> [--instance <instance>]` | Stop the game from loading a mod. |

## Features

tba

## Roadmap

### Borea Pre-Release

- Mod Downloads
- Mod Packs

### Borea 1.0

- Saves
- Vehicles

### Once KSA supports it

- Multiplayer server setup
