# Borea
Borea is a cross-platform complete general content manager for Kitten Space Agency. It manages mods, mod packs, vehicles, game saves, and more. It is intended to be modifiable by changing out `Borea.Storage`, `Borea.Network`, and `Borea.App` so user can customize Borea. This repository will contain all the offical Borea files and releases.

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
| `src\Borea.Composition` | The composition root. Builds every service from the saved settings, once, for `Borea.App` and `Borea.Cli`. |
| `src\Borea.App` | A desktop level application that the user will interact with. Gets its services from `Borea.Composition`. |
| `src\Borea.Cli` | The command line interface, `borea`. A thin wrapper over the same services, for scripts and for machines without a desktop. |

# Command line
`borea` runs Borea's operations from a script. Every read command takes `--json`.
The exit code is 0 when the command completed, 1 when the operation failed and the reason is on stderr, and 2 when the command line did not parse.

| Command | Does |
| --- | --- |
| `borea settings show` | Print where the game and the mod loaders are. |
| `borea settings set game <directory>` | Point Borea at the game installation. |
| `borea settings set loader <loader-id> <directory>` | Point Borea at an installed mod loader. |
| `borea game version` | Print the current public build the master server reports. |
| `borea instance list` | Print every instance and mark the active one. |
| `borea instance create <name>` | Create an empty instance. |
| `borea instance rename <instance> <new-name>` | Give an instance a new name. |
| `borea instance delete <instance>` | Delete an instance and its folder. |
| `borea instance activate <instance>` | Make an instance the active one. |
| `borea enable <mod-id> [--instance <instance>]` | Make the game load a mod. |
| `borea disable <mod-id> [--instance <instance>]` | Stop the game from loading a mod. |

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
