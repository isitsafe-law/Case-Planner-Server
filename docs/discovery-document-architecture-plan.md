# Discovery document architecture plan

## Audit findings

- Managed discovery language is stored in the `discovery_templates` table as immutable versioned rows (`stable_key`, `version`, `category`, `issue_tag_name`, `wording`, `enabled`, and ordering metadata). The Settings bulk editor groups those rows into a base document or issue-tag section.
- The case Documents tab calls the same server-side versioned preview/generation endpoints used by the Settings preview. It loads the active enabled version for the base set, reads the case issue tags, inserts matching tag blocks, resolves the merge-field registry, renumbers interrogatories and requests for production, and records generated-document provenance.
- Uploaded `.txt`/`.docx` files are handled by the custom-document-template upload path and retained under the local template directory. They are currently reusable custom documents, while approved discovery language is migrated into the managed versioned table rather than silently replacing it.
- Merge fields are defined by the shared registry in the server document service. Unknown fields and hard-coded numbered cross-references are surfaced as validation warnings before generation.
- Case issue tags come from the case workspace issue-tag query and are passed into the renderer; missing tag blocks are warnings rather than silent language insertion.
- Generated documents are stored as fixed records with rendered content, template/version provenance, included tags, generation metadata, and output path. Editing a later template version does not rewrite the stored snapshot.
- The remaining legacy surface is the individual discovery-template row table in Settings and the custom-template upload list. These are compatibility/administration views, not separate renderers. The next UI pass should replace the row table with the existing bulk section editor as the primary path and keep the row view only as a controlled maintenance fallback.

## Proposed target architecture

1. **Global template manager**: one navigation area for approved base templates, document types, issue-tag blocks, merge fields, validation, preview, version history, activation, and upload/import. Every save creates a new immutable `DocumentTemplateVersion`/discovery-template version.
2. **Shared renderer**: one service accepts a base version, selected issue-tag versions, a case merge context, and a document type. Settings preview, case preview, final generation, and export all call this service.
3. **Case Documents**: choose document type and active version, inspect missing fields/warnings, edit a case-specific draft, then save a `GeneratedDocumentSnapshot` containing rendered text and all source version IDs.
4. **Case Discovery**: remain focused on served/received discovery tracking, follow-up, and the manual case-level Discovery Complete flag; it does not administer global language.
5. **Compatibility boundary**: old seeded item records and static custom files remain readable for migration/history, but no active Generate action may bypass the shared renderer.

## Migration and rollout

- Keep existing versioned discovery rows and seed the approved combined interrogatories/requests-for-production and requests-for-admission files as initial active base versions.
- Map existing issue-tag rows to `IssueTagTemplateBlock` records keyed by document type and tag; preserve their versions and enabled state.
- Add explicit document type and source-version metadata to generated snapshots where absent, backfilling from the current active version at migration time.
- Validate every migrated template for unknown merge fields, insertion markers, signature/certificate blocks, numbering conflicts, and hard-coded references; report warnings without rewriting approved language.
- Redirect all case generation buttons to the shared renderer, then retire only the obsolete renderer after generated-document regression tests pass.

## Reusable components and replacement targets

Reuse the current merge-field registry, version-aware discovery preview/generation service, issue-tag query, generated-document repository, bulk editor, preview, and validation code. Replace the row-first Settings presentation with a section-first manager, and replace any remaining hard-coded case generation path with the shared renderer. This avoids creating a parallel template system.
