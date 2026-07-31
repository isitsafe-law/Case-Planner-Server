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
| Completeness audit | `GET /api/document-platform/templates/{key}/completeness` | Active template tags are classified as canonical, declared runtime inputs, or unknown before generation |
| Legacy tag capitalization | Generate a template containing `{{COUNTY}}` when the canonical tag is `County` | The value resolves normally; no false missing field is recorded |
| Sections | No optional sections selected | Base document generates without optional section text |
| Sections | Issue-tag section selected | Section text appears once and numbering remains valid |
| Review | Generated document is downloaded before filing | Output is stored as a draft and remains editable/reviewable by the user |
| History | Two generations of the same case/template occur quickly | Both history rows and files remain distinct |
| Storage failure | Export folder is read-only or unavailable | Portable validation fails and the UI reports a storage error |
| Backup/restore | Run Diagnostics > Test Backup / Restore, then generate a document | Backup passes integrity/schema checks; temporary restored copy opens; generation remains valid |
| Live restore | Restore is requested from a valid backup | Current database is backed up first; restored database reopens and migrations run before success is reported |
| Upgrade | Older portable database opens in a newer package | Schema upgrades complete; legacy case/party data survives; current DOCX generation remains available | Covered by `PortableUpgradeValidationTests` |

Generation failures include a request ID in the response and portable log so a user report can be correlated with the server diagnostic record.

The current automated coverage includes every active built-in template's completeness audit, built-in template generation, optional sections, missing values, repeated generation, and stale portable template paths. The remaining rows are the next expansion targets for integration or manual package smoke tests.
