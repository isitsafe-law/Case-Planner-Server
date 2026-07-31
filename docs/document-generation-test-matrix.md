# Document generation test matrix

This is the portable-first contract for document generation. The same cases should be rerun against the future SQL Server service after provider cutover.

| Area | Scenario | Expected result |
|---|---|---|
| Template catalog | Each active built-in template has a readable DOCX file | Catalog loads; no missing-template data-quality issue |
| Portable copy | Existing database contains an absolute path from an older package | Generation finds the matching current file and repairs the stored path |
| Required input | Required runtime input is blank | Generation returns a clear validation error; no output or history row is created |
| Optional input | Optional input is blank | Draft generates successfully |
| Missing case value | A merge field has no source value | Draft generates with a missing marker and reports the field |
| Unknown tag | Template contains an unregistered merge tag | Draft does not silently stop; the missing tag is reported |
| Sections | No optional sections selected | Base document generates without optional section text |
| Sections | Issue-tag section selected | Section text appears once and numbering remains valid |
| Review | Generated document is downloaded before filing | Output is stored as a draft and remains editable/reviewable by the user |
| History | Two generations of the same case/template occur quickly | Both history rows and files remain distinct |
| Storage failure | Export folder is read-only or unavailable | Portable validation fails and the UI reports a storage error |
| Backup/restore | Restore is requested from a valid backup | Current database is backed up first; restored database reopens and migrations run |
| Upgrade | Older portable database opens in a newer package | Schema upgrades complete; data-quality and template-path checks run afterward |

The current automated coverage includes built-in template generation, optional sections, missing values, repeated generation, and stale portable template paths. The remaining rows are the next expansion targets for integration or manual package smoke tests.
