# Discovery and document merge fields

Templates use `{{TokenName}}`. The preview step reports missing tokens before generation.

Database-backed discovery items use the validated snake-case fields below:

- `{{case_name}}`
- `{{case_number}}`
- `{{job_number}}`
- `{{tract_number}}`
- `{{county}}`
- `{{landowner_name}}`
- `{{filing_date}}`
- `{{trial_date}}`
- `{{deposit_amount}}`
- `{{owner_demand}}`
- `{{appraiser_name}}`

Unknown discovery fields are rejected before a new template version is saved.

Automatically populated case fields:

- `{{County}}`
- `{{CaseNumber}}`
- `{{JobNumber}}`
- `{{Tract}}`
- `{{ProjectName}}`
- `{{DefendantNames}}`
- `{{DepositAmount}}`
- `{{FilingDate}}`
- `{{WholePropertyAcres}}`
- `{{AcquisitionAcres}}`
- `{{TaxAmount}}`

Organization defaults:

- `{{AttorneyName}}`
- `{{BarNumber}}`
- `{{AttorneyPhone}}`
- `{{AttorneyEmail}}`
- `{{OrgAddressLine1}}`
- `{{OrgAddressLine2}}`
- `{{DivisionHeadName}}`
- `{{RowSectionHeadName}}`
- `{{ChiefLegalCounselName}}`

Template-specific manual fields are displayed by the generation form and are discoverable through the template-tag catalog. Unknown tokens render as `[MISSING: TokenName]` and appear in the missing-token list. Generated files are snapshots; later template edits do not change an existing export.

Issue-tag additions are appended to the standard discovery set, de-duplicated by the content builder, numbered consistently, and flagged for attorney review. They are drafting support, not jurisdiction-specific legal advice.
