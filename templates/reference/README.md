# Reference Library source files

This folder holds the plain-text source files for the Settings → Reference Library
feature. The `.txt` files are excluded from version control because they contain real
prior-case litigation content (actual names, testimony, and figures), not generic
templates.

The app expects these four files here, read live at request time
(`GetReferenceLibraryAsync` in `CasePlannerRepository.cs`):

- `JuryInstructions.txt`
- `OpeningStatement_Reyes.txt`
- `DirectExamination_MaxwellFanning.txt`
- `DirectExamination_ChesBartlett.txt`

If this repo is cloned fresh, restore these files from the original source documents
before the Reference Library section will show real content.
