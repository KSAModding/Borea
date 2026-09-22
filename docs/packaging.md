# Packaging Borea

Borea updates itself unless a file called `borea-package.txt` lies next to the `borea` program.
Its first line names the package manager, for example `winget`, and Borea then points the user at that manager instead of updating.
Ship `LICENSE` and `THIRD-PARTY-NOTICES.txt` with the program, because the notices carry the licenses of the packages inside it.
