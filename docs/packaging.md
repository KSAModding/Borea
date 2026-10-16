# Packaging Borea

Borea updates itself unless a file called `borea-package.txt` lies next to the `borea` program.
Its first line names the package manager, for example `winget`, and Borea then points the user at that manager instead of updating.
Ship `LICENSE` and `THIRD-PARTY-NOTICES.txt` with the program, because the notices carry the licenses of the packages inside it.

On macOS, the App is the bundle `Borea.app`.
The marker is `Borea.app/Contents/Resources/borea-package.txt`, and `LICENSE` and `THIRD-PARTY-NOTICES.txt` go there too, where the App reads them.
A package that adds the marker must sign the bundle again, for example with `codesign --force --deep --sign - Borea.app`.
