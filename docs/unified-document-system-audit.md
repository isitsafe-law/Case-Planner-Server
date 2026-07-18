# Unified document system audit and implementation note

## Audit summary

Before this pass, the case Documents tab exposed separate Generate Documents, Merge Tags, Custom
Templates, Saved Custom Templates, and Generated Documents panels. Discovery administration lived in a
separate Settings page, while built-in, uploaded, and discovery generation used different visible entry
points. Case summary/review used a legacy text-generation route, and generated history exposed storage
paths.

The implementation also contains compatibility seams that remain intentionally provider-specific:

- `IDocumentPipeline` and `DocumentCompositionService` assemble built-in, discovery, and custom previews.
- `ICustomTemplateAdministration` / `SqlServerCustomTemplateStore` manage uploaded template versions.
- `IDiscoveryTemplateAdministration` / `SqlServerDiscoveryTemplateStore` manage reusable discovery content.
- `IGeneratedDocumentService` preserves legacy text/draft operations; binary `.docx` output uses
  `IBinaryGeneratedDocumentService` and one generated-history table/model.
- `IReferenceLibraryStore` is file-backed for SQLite and SQL-backed after migration 020.

These seams remain for safe migration and SQL provider cutover, but the UI no longer presents them as
separate case-level systems.

## Changes in this pass

- Case Documents is now category-first: Discovery, Pleadings, Correspondence, Memoranda, Depositions,
  Reports, Settlement, Trial, Administrative, and Other.
- Search Templates was removed from the primary case workflow.
- Custom Templates and Saved Custom Templates panels were removed from the case Documents tab.
- Uploaded templates are administered under Settings → Document Templates and appear in the unified
  catalog returned by `/api/document-catalog`.
- Case Summary and Case Review are compact utilities at the top of Documents.
- Summary/review generation now creates `.docx` bytes and records them through the same binary history path.
- Generated history is one table/list and no longer exposes server filesystem paths in the case UI.
- Discovery administration is labeled Discovery Content inside the Document Templates area; case assembly
  remains in Documents.
- Folder refresh is described as a configured-folder operation; normal users use web upload for templates.
- The existing catalog models now carry description, category, tags, visibility, owner, and default-output
  metadata so uploaded records have one shape with built-in entries. Current SQLite/folder-managed records
  default to `Other`/`Personal` until the SQL metadata migration is applied; they still appear immediately in
  the unified catalog without a rescan.
- Merge fields are available from both the case utility row and Settings â†’ Document Templates through the
  same grouped, copyable registry endpoint (`/api/template-tags`).

## Storage and migration

- Built-in templates remain catalog code plus approved source files.
- Uploaded DOCX templates remain versioned source files with catalog metadata.
- Reusable discovery content remains versioned discovery data and is assembled at case-generation time.
- Generated files are stored through `IDocumentStorage` and binary generated-document history.
- SQL migration `020_reference_library.sql` centralizes editable reference-library content; the seed helper
  is `scripts/export-reference-library-sql.ps1`.
- SQL custom-template rows currently retain their existing version/history columns; the new metadata fields are
  additive model fields and are intentionally non-destructive. A follow-up SQL migration should persist
  administrator-edited category/tag/description metadata as normalized catalog data during IT cutover.

## Remaining compatibility work

The old preview/save endpoints remain route-compatible wrappers over `IDocumentPipeline` so existing clients
do not break during migration. The legacy text-generation service methods remain for draft compatibility,
but the user-facing summary, review, built-in DOCX, and custom DOCX actions now use the unified binary output
and history path. Full SQL activation, Entra production configuration, and clean full-solution restore still
require the connected IT build/database environment.
