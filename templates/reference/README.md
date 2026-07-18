# Reference Library source files

This folder holds the plain-text source files for the Settings → Reference Library
feature (`FileReferenceLibraryStore` in `server/CasePlanner.Web.Server/Persistence/ReferenceLibraryStore.cs`).
Any `.txt` file placed here shows up in the Reference Library automatically; `.txt` files
are excluded from version control (see `.gitignore`) since reference content is real,
firm-specific litigation material (names, testimony, figures), not generic templates.

There are no built-in defaults - the Reference Library starts empty on a fresh clone or
install. Add documents through Settings → Reference Library in the app, or drop `.txt`
files directly in this folder.
