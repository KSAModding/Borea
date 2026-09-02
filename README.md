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
| `src\Borea.Composition` | The composition root. Builds every service from the saved settings, once, for `Borea.App` and later `Borea.Cli`. |
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
