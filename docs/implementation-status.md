# Comprehensive UX revision status

- 2026-07-16: IT review package CasePlannerIT_Handoff_2026-07-16 was published as self-contained
  win-x64 and zipped. It includes the built client, SQL scripts, production configuration template, IT/Entra/
  migration documentation, templates, and import samples, with no development database or credentials.
  Packaged health and home-page smoke checks passed.
- 2026-07-16: Reference Library is now editable from Settings. Users can add, edit, and remove documents;
  metadata is stored in a shared-folder sidecar and content remains plain text in the configured reference
  folder. Temporary CRUD verification passed without leaving a test document behind.
- 2026-07-16: Normal CSV/XLSX imports now resolve through a provider-selected import service, including the
  SQL Server case-catalog importer. The reference-library route now uses an explicit file-system store, making
  its shared-folder dependency clear rather than coupling it to the SQLite repository.
- 2026-07-16: Case-notes export now uses a provider-neutral workspace query and shared document storage.
  Its authorization check is assignment-aware, and the existing text format and filename convention are
  preserved for both SQLite and SQL Server.
- 2026-07-16: Child-record authorization lookups for notes, hearings, checklist items, deadlines, comparable
  sales, witnesses, exhibits, and trial motions now use a provider-selected SQL lookup contract. SQL uses a
  whitelisted table map and ignores soft-deleted rows, removing those repository calls from normal routes.
- 2026-07-16: The canonical one-row-per-case publication summary now uses a provider-selected store.
  Migration 019 adds SQL `rowversion`, authenticated updater identity, and the unique case index. Normal
  publication GET/PUT routes no longer call the SQLite repository directly, and the client retains the
  replacement token after saves. Home verification matched 1/1 summaries; a stale case-40 update returned
  HTTP 409, and its original values and imported metadata were restored.
- 2026-07-16: Discovery posture, pipeline handoffs, and activity history now use provider-selected stores.
  SQL posture edits and activity edits require `rowversion`; handoff creation requires the current case token
  and returns its replacement. Migration 018 adds operational-history concurrency and actor attribution.
  Live case-40 verification matched posture, 0/0 handoffs, and 3/3 activity rows; a stale posture write
  returned HTTP 409. Global activity/document reconciliation remains clean at 68 activities and 6 documents.
- 2026-07-16: Document composition now has provider-selected SQLite and SQL Server implementations. Normal
  standard/discovery previews, custom previews/DOCX generation, and risk narratives route through the shared
  boundary. Shared rules cover managed discovery insertion/renumbering, semantic warnings, merge tokens, and
  narrative selection. Live case-40 SQLite/SQL comparisons matched exactly; server tests pass 118/118 and
  client tests pass 24/24.
- 2026-07-16: Normal discovery-template/base, custom-template, organization-default, checklist-template/item,
  and deadline-template administration now uses provider-selected stores. SQL updates, activations, and
  retirements retain `rowversion` conflict protection, and the client now sends those tokens. Live catalogs
  matched: 10 discovery items, 35 checklist templates with matching item totals, 6 deadline templates, matching
  organization defaults, and no custom templates. Server tests pass 118/118; client tests pass 24/24 and the
  production client build succeeds.
- 2026-07-16: Frequent case quick actions now use a provider-selected service: next action, waiting/clear,
  defer/clear/bulk defer, holder, priority, trial-track, and short-note updates. SQL writes require the current
  case `rowversion`, return the replacement token, write audit events, and retain activity entries for actions
  that historically recorded them. The client sends and immediately applies returned tokens. A controlled
  case-40 priority change returned HTTP 409 for a stale retry and restored the original value successfully.

## Implemented

- Active-only automation and attention gating with a separate resumable import-triage queue/wizard.
- Dashboard triage engine, attorney work queues, quick waiting/holder/defer actions, and bulk defer.
- Automatic activity events for core quick actions and editable activity entries with immutable edit history.
- Status-first case workspace, Recent Activity, issue tags in workflow/discovery, grouped discovery accordions, and compact discovery rows.
- Persistent global type-ahead case search and unambiguous Cases navigation.
- Template-backed checklist generation, deadline generation, duplicate protection, source labels, and deadline override history.
- Service update wording distinguishes record updates from actual service dates.
- Backup-before-write and 20-file retention remain intact.
- Canonical case-level publication storage with separate first/second dates, publication name, perfected state, update metadata, validation, and legacy-row migration.
- Dedicated deferment storage (`deferred_until`, reason, timestamp, and user), queue suppression until the return date, clearing, and activity history.
- Database-backed discovery template items with immutable versions, base/tag/track selection, enable/disable, ordering, duplication, default restoration, validated merge fields, editable case preview, duplicate suppression, and immutable generation snapshots recording versions and tags.
- Structured task/deadline provenance with migrated legacy source values, stable template identity/version, source stage, generation timestamp/user, visible source labels, and three-way stage-advance generation choice.
- Case-level Discovery Complete status with completion timestamp/user, audit events for complete/reopen, preserved individual discovery items, and suppression of general discovery-attention warnings while specific deadlines remain eligible.
- New manual cases default to Pipeline; Pipeline/Triage are gated from live task/deadline generation until activation/advancement.
- Attorney Action Queue no longer exposes Record Decision or Assign Holder as primary actions; the banner now includes a shared Light/Dark/System theme control.
- Attached authoritative discovery templates were verified against repository files; preview generation now renumbers interrogatories and requests for production independently.
- Settings are organized into grouped categories with a persistent left navigation on desktop and a compact horizontal navigation on narrow screens.
- The Cases page now uses consolidated Case Status as its primary filter and API query; legacy stage/track filters are removed from day-to-day navigation but remain preserved for history and template compatibility.
- Discovery document previews now detect and display hard-coded numbered cross-reference warnings after automatic renumbering, so affected text can be reviewed before saving.
- Appearance Settings now exposes Light, Dark, and Use System Setting consistently with the top-banner control; System mode responds to live OS theme changes.
- Discovery bulk editing validates empty blocks, unsupported type markers, Base/Tag mixing, and blank wording before creating a new template version.
- Settings now exposes the consolidated-status migration review report with legacy values and direct links to open flagged cases.
- Pipeline dashboard classification now uses consolidated Case Status as the primary source, suppresses service tracking for Pipeline/Triage, defaults new Pipeline cases to Legal Assistant, exposes holder/review/note fields in the case editor, and removes the permanent top-banner theme selector while preserving Settings theme support.
- Docket insight metrics are now actionable buttons that reveal the exact matching case list with status, holder, next review, next action, and Open Case controls.
- Attorney Action Queue cards now expose issue-specific shortcuts for discovery strategy and Pipeline holder changes; the obsolete Status-menu Record Decision action and queue handler are removed.
- Trial insight is now a compact chronological card list focused on trial date, days remaining, status, primary warning, next required action, and Open Case.
- Pipeline advancement now requires a filing date and offers all/review/none task-and-deadline generation choices before service tracking begins.
- Queue items missing a discovery strategy now provide an inline strategy selector that saves and refreshes the dashboard without opening the full case workspace.
- Overdue deadline queue items now carry their related deadline ID and support Mark Complete directly from the Attorney Action Queue.
- Overdue deadline queue items also support inline due-date updates with a required reason and preserved change history.
- The redundant Decisions insight tab is removed; decision work now has one primary home in the Attorney Action Queue.
- Pipeline case-editor changes to holder, next review date, and note now create concise PipelineUpdated activity entries.
- Legacy stage advancement controls were removed from the case workspace; consolidated Case Status is now the only primary advancement UI.
- Migration review was moved from Document Defaults into the administrative Storage / Paths settings area.
- Case interrogatory preview/generation now routes through the versioned discovery-template service used by Settings preview, preserving active template versions and issue-tag selection.
- Attorney Action Queue planning is consolidated into a single Plan Next Step control with action, waiting, and revisit choices; standalone defer/wait/next-action buttons are retired.
- Work Queue urgency filtering is now correctly placed beside category filters, supports combined filtering across all work types, and shows an explicit active-filter summary with clear control.
- Valuation & Risk ASHC and Landowner positions now use compact Add/Edit progressive disclosure instead of always-visible empty forms.
- Trial Notebook now uses a responsive two-column layout with trial materials on the left and the preparation checklist on the right.
- Pipeline rows now keep Open Case as the primary action and group holder, note, handoff, and filing advancement under a compact More menu.
- Old Offers now uses a compact chronological history with an Add Offer form that opens only when needed.
- Risk Analysis Ledger numeric columns now use right-aligned tabular formatting, controlled widths, and spacing that prevents large currency values from colliding.
- Empty Trial Notebook witness, exhibit, and motion sections now use compact Add controls instead of reserving empty tables.
- Discovery Template Settings now labels the highest enabled version for each stable template as Active and older versions as Historical.
- Dashboard summary-card filters now show a filled active state, explicit filtered-by/count text, and a visible clear-filter action.
- Work Queue Discovery rows now support Record Response directly, alongside Open Case, with response-date capture and immediate refresh.
- Pipeline insight rows now show only pre-filing fields: assigned date, holder, next review, note/next action, last updated, and compact actions; legacy stage/flag columns are removed.
- Plan Next Step now offers one-click Revisit in 30 days alongside custom revisit dates.
- Work Queue active urgency summaries now include the live matching-item count for the selected category/filter combination.
- Pipeline More actions now include Set Next Review Date, saving a focused review action without opening the case.
- Add Note from Action Queue and Pipeline now creates persisted Case Note records with source-specific titles and companion activity entries.
- Dashboard filter presentation now uses only the selected summary card’s filled/checkmarked state; the oversized filtered-state banner has been removed.
- Recent Activity is now displayed as a read-only audit trail without casual click-to-edit behavior.
- Discovery now has a compact Overview with directly editable strategy and manual completion status; the larger panel is reserved for supporting details.
- Trial Notebook empty sections now have exactly one Add control; header Add buttons appear only after records exist.
- Empty comparable-sale sections now use compact messaging without reserving an empty table; Add Comparable Sale remains available.
- Case Note activity entries now have a subtle navigation affordance that opens the Notes tab; non-linked audit entries remain non-interactive.
- Documents now shows active versioned discovery-section count and case issue tags before interrogatory generation.
- Administrative migration review now displays the unresolved-case count directly in its heading.
- Open task rows no longer render the stray circular status marker; only completed tasks show a checkmark.
- Work Queue now supports a global urgency filter (overdue, today, 7/14/30 days, no due date, all open) combined with category and service filters, with visible active-filter state and reset.
- The Work Queue Service section now uses the global urgency filter only; the redundant service-specific dropdown was removed.
- Pipeline case headers now show the consolidated Case Status without the legacy stage label, and empty Pipeline cases no longer show a misleading unset Deposit tile.
- Discovery Overview places Strategy and Discovery Complete on one compact row and only shows completion metadata when complete; the persistent open-state explanation was removed.
- Generated test build `CasePlannerWeb_TestBuild_2026-07-12-final4` passed the client build, client tests (24/24), and packaged `/api/health` check.
- Risk Analysis saves now append immutable history snapshots with analysis date and formula version; the case Risk tab lists and reopens prior snapshots for review while retaining the live ledger.
- Work Queue Service conditions are available as compact chips for Missing deadline, Not perfected, and Missing basis date, without restoring a second dropdown.
- Risk Analysis now uses progressive disclosure: the main tab shows a compact current/history summary and opens the full calculator only when requested; the editor retains Save, Reset, narrative generation, and Excel export.
- Generated test build `CasePlannerWeb_TestBuild_2026-07-12-final6` passed client/server tests and packaged health/history endpoint checks.
- Active case lists no longer present legacy Stage or Track columns as current classifications; empty Service queue results now use a compact state and the urgency control uses a smaller inline footprint.
- Generated test build `CasePlannerWeb_TestBuild_2026-07-12-final7` passed the packaged health and risk-history endpoint checks.
- Risk Analysis snapshots now retain explicit analysis date, interest rate, and contingency-fee percentage inputs; the calculation engine and Excel export use those values with backward-compatible 6%/30% defaults.
- Generated test build `CasePlannerWeb_TestBuild_2026-07-12-final8` passed client tests (24/24), server tests (87/87), and packaged health/history endpoint checks.
- Saved Risk Analysis entries now support confirmed Delete, Open, and Compare actions; snapshots persist the canonical last qualifying Key Scenario and deletion records a concise activity entry.
- Work Queue supports due-date/case-name sorting alongside urgency, category, and service-condition filters. Case Insight opens on Docket in the requested order, Discovery rows expose direct Edit/status controls, and Trial Prep has a compact empty state and selection-gated bulk toolbar.
- Generated folder build `CasePlannerWeb_TestBuild_2026-07-12-final9` passed client tests (24/24), server tests (89/89), and packaged home/health/history endpoint checks.
- Risk Analysis Excel exports now left-align the top case-summary block and apply subtle banding to populated scenario rows without changing numeric-table alignment or formulas.
- Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final10` passed client tests (24/24), server tests (89/89), and packaged home/asset/health/history endpoint checks.
- Generated text documents now retain rendered content, template provenance, issue-tag version metadata, and draft/final flags in `document_exports`; the Documents tab can reopen the saved snapshot without re-running the generator.
- Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final11` passed client tests (24/24), server tests (90/90), and packaged home/asset/health/history/document-content endpoint checks.
- Case document previews now carry active template-version and issue-tag-version provenance into saved generated-document snapshots; final folder build `CasePlannerWeb_TestBuild_2026-07-12-final12` passed client tests (24/24), server tests (90/90), and packaged home/asset/health/history/document-content/provenance checks.
- DOCX exports are now correctly download-only in the Documents UI; only retained text snapshots expose the reopenable text-draft action. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final13` passed client tests (24/24), server tests (90/90), and packaged home/asset/health/history checks.
- Requests for Admission generation now uses the approved static 26-request base together with managed issue-tag admission blocks, numbering, merge resolution, warnings, and template-version metadata. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final14` passed client tests (24/24), server tests (90/90), and packaged RFA preview/home/asset/health/history checks.
- Discovery Template Settings now presents the complete bulk editor as the primary workflow; the legacy row-level editor is retained only under an explicit Advanced item maintenance disclosure. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final15` passed client tests (24/24), server tests (90/90), and packaged RFA/home/asset/health/history checks.
- Trial Prep bulk due-date editing now uses a compact explicit Change Due Date popover with Apply/Cancel instead of a permanently visible full-size date field. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final16` passed client tests (24/24), server tests (90/90), and packaged RFA/home/asset/health/history checks.
- Work Queues now include compact case/item Search that composes with category, urgency, service-condition filters, sorting, and live counts. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final17` passed client tests (24/24), server tests (90/90), and packaged RFA/home/asset/health/history checks.
- Case document previews now offer separate Save Draft and Finalize Document actions, with Draft/Finalized status visible in generated-document history. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final18` passed client tests (24/24), server tests (90/90), and packaged home/asset/health/document-snapshot checks.
- Static built-in and uploaded custom text previews now emit deterministic file-version provenance in their generated-document snapshots. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final19` passed client tests (24/24), server tests (90/90), and packaged RFA/version-metadata/home/asset/health/history checks.
- Interrogatories preview now uses the approved combined base file with caption, definitions, full base language, signature/certificate, managed issue-tag insertion, and independent renumbering. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final20` passed client tests (24/24), server tests (90/90), and packaged Interrogatories/RFA/home/asset/health checks.
- Settings Preview now delegates to the same combined Interrogatories renderer used by Case Documents, with matching approved-base output and version metadata. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final21` passed client tests (24/24), server tests (90/90), and packaged Settings/Case preview comparison, home, asset, and health checks.
- Base Discovery bulk editing now saves immutable full-document versions in `discovery_base_versions`; the active managed version is used by both Settings and Case previews, with approved-file fallback. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final22` passed client tests (24/24), server tests (90/90), and packaged base-version/preview/home/asset/health checks.
- Requests for Admission now uses the same managed full-document base-version path with approved-file fallback. Clean final folder build `CasePlannerWeb_TestBuild_2026-07-12-final24` passed client tests (24/24), server tests (90/90), and read-only packaged RFA/Interrogatories/home/asset/health checks.
- Custom template uploads now preserve prior files and create immutable `_vN` versions; the saved-template list displays the version. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final25` passed client tests (24/24), server tests (90/90), and packaged custom-template/list, Interrogatories, home, asset, and health checks.
- Case Insight now persists the selected tab in browser storage while defaulting to Docket for a new browser. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final26` passed client tests (24/24), server tests (90/90), and packaged Interrogatories/home/asset/health checks.
- Discovery base templates now expose immutable version history in Settings; restoring a historical version creates a new active version, preserving every prior snapshot while making version recovery explicit.
- Plain-text custom templates can now be opened and edited in the application; saving creates a new immutable version while preserving the uploaded source. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final28` passed client tests (24/24), server tests (90/90), and packaged home/custom-template/history/risk-health/asset checks.
- Uploaded custom-template versions now expose an explicit active-version control; activation is persisted separately from immutable files and is shown in the manager. Final folder build `CasePlannerWeb_TestBuild_2026-07-12-final29` passed client tests (24/24), server tests (90/90), and packaged active-template/history/health/asset checks.
- Discovery generation now reports non-blocking warnings when a case issue tag has no enabled Interrogatories/RFP or Requests-for-Admission block, while still allowing preview and generation.
- Discovery Template Settings now includes a base-template validation action for unknown merge fields, missing issue-tag insertion markers, and missing signature/certificate blocks.
- Requests for Admission output now renumbers all base and issue-tag requests continuously after insertion, ignoring source numbers typed in the editable template.
- Discovery rendering resolves supported semantic references (`[[REF:interrogatory-7]]`, `[[REF:production-3]]`, and `[[REF:admission-4]]`) and warns on unresolved reference tokens.
- Uploaded DOCX custom templates can be opened in the in-app editor through a safe extracted-text view; saving creates a new immutable TXT version and leaves the original DOCX unchanged.
- Discovery base-version history now supports non-destructive Load for review in addition to restore-as-new-active-version.
- Discovery base-version history now provides a compact read-only comparison against the active version.
- Merge Tags now supports search and shows a selected-case/organization sample value for each field.
- Added server coverage for discovery-template validation; the suite now passes 92/92.
- Direct DOCX custom-document exports now retain the custom template version in generated-document provenance metadata.
- Generated text and DOCX snapshots now retain the serialized merge-field values used during generation.
- Document snapshot tests now verify merge-field provenance alongside rendered content and template versions.
- Case custom-document generation now displays the selected custom template version and Active status before preview/download.
- Custom template uploads and edits now flag unknown placeholders in the saved-template manager without blocking review or generation.
- Custom-template warning pills and historical-version comparison output now use dedicated accessible styling.
- Final documentization now requires explicit confirmation before finalizing a document that still contains missing-value placeholders; drafts remain available without the confirmation.
- Custom template upload now accepts an optional managed template name, preserving the source extension and immutable versioning.
- Checklist template seeds, migration, generation matching, and Settings labels now use consolidated workflow statuses instead of legacy stage names; existing template rows are migrated without rewriting historical checklist-item provenance.
- Removed the unreachable legacy issue-tag renderer from the connected Interrogatories generation path; the approved/managed renderer remains authoritative.
- Corrected managed issue-tag insertion so `{{IssueTagInterrogatories}}` survives token filling and receives the selected case's enabled tag blocks; added fixture coverage using an approved combined template.
- Semantic discovery validation now warns when a numeric reference exceeds the rendered document's available item count.
- Managed upload names now normalize an entered filename extension instead of duplicating it.
- Newly uploaded custom templates now open immediately in the in-app review editor, including extracted-text review for DOCX uploads.

## Partially implemented

- Checklist and deadline templates are editable in Settings; deadline additions use a compact, expandable review picker with summary counts, duplicate comparison, filters, inline due-date edits, and responsive narrow-screen layout.
- Service/publication facts are edited in one compact Case Record section with a single service-perfected control; legacy status data remains read-only for compatibility.
- The consolidated user-facing Case Status projection, migration report, status-aware dashboard, and Pipeline advancement workflow are implemented while legacy stage/track fields remain preserved for backward compatibility.
- Discovery bulk editing is available for the authoritative base set and each configured issue tag, with full-section text editing, typed blocks, versioning, and preview; individual item editing remains available for fine-grained maintenance.
- User/editor identity is unavailable because this is a single-user local application with no authentication subsystem; timestamps and reasons are stored, identity fields are deferred.
- Attorney Action Queue selection now uses a dedicated left-side checkbox column aligned with the Select all toolbar, with additional spacing before the first queue card.
- Imported-case triage now asks for one consolidated Workflow Status instead of separate Track and Current Stage steps; activation uses that status while legacy fields remain derived for compatibility.
- Triage progress saves now retain the imported case's Triage state until the final Activate step, preventing the wizard from closing when historical dates are saved.
- Bulk Attorney Action Queue defer now defaults to 7 days and offers 14 days, 30 days, or a custom date.
- Case deferments now render as prominent overview alerts with change-date and clear actions; overview warnings are limited to overdue deadlines and overdue tasks.
- Reporting/upcoming-work architecture is documented in `docs/reporting-upcoming-work-architecture.md`. Dashboard Upcoming Work currently provides a compact 5/10 filtered preview over existing queue records; a server-side limited query and full saved-report builder remain the next phases.
- Added `server/CasePlanner.Web.Server/Properties/PublishProfiles/PortableWinX64.pubxml` and `scripts/publish-portable.ps1` for explicit self-contained single-file win-x64 publishing.
- Added `/api/dashboard/upcoming-work`; the dashboard now consumes its limited result set while retaining the existing Work Queue action handlers.
- Reports phase started with a top-level read-only report builder: consolidated open-case filters, selectable core columns, sorting, preview, Open Case links, and CSV export. Saved definitions, relative-date filters, grouping, and Excel export remain follow-up work.
- Portable builds now expose a global Exit Case Planner control backed by loopback-only, one-time-token shutdown endpoints and `IHostApplicationLifetime.StopApplication()`; no tray launcher exists in the current package, so no tray icon was added.
- Reports now export `.xlsx` through the existing ClosedXML stack, and the Columns and layout editor is collapsible.
- Dashboard panels now render in the requested order: Attorney Action Queue and Case Insight first, followed by Upcoming Work. Fresh databases seed one clearly fictional `SAMPLE-CASE-001` record only; existing databases are never reseeded. Data Management now offers recognized-sample deletion and an advanced typed full reset that creates a verified backup, optionally clears generated exports, reruns migrations, and reseeds the sample case.
- Full reset now clears SQLite tables in place inside a transaction, avoiding Windows file-lock failures from replacing the live database file; reset errors are returned as readable validation messages instead of raw 500 responses.
- Release 1.0.16 expands the fresh-install demo seed to four fictional cases spanning Pipeline, Filed / Service Pending, Active Litigation, and Trial Preparation; sample detection and deletion cover the complete set.

## Not implemented and reason

- Jurisdiction-researched discovery form expansion was not added. The application is intentionally offline/local and the repository contains no approved Arkansas form corpus; adding unreviewed legal language would violate the attorney-review requirement.
- True multi-user authorization and editor identity are not available because the application has no account/authentication model.

These limitations are explicit so a release review can distinguish working acceptance paths from future schema/UI work.
- Document builder foundation now exposes an explicit `IDocumentPipeline` boundary for preview validation and snapshot persistence. The initial adapter delegates to the existing provider-selected composition and generated-document services, preserving current behavior while creating the seam for renderer and SQL metadata extraction.
- Standard and custom document previews, plus custom DOCX generation, now route through the pipeline boundary. Existing endpoints remain compatible while validation and renderer selection can be centralized in the next pass.
- Saved generated-document requests now route through `IDocumentPipeline`; legacy preview endpoints preserve their original response shape while using the centralized pipeline internally.
- Added `IDocumentValidator` to centralize missing-merge-value and finalized-document checks before snapshots are saved.
- Discovery template previews now use the same pipeline boundary, completing the current preview-path consolidation.
- Risk-analysis narrative generation now uses the same pipeline boundary; all primary document preview/generation routes are centralized.
- Added an explicit document-renderer registry seeded from the built-in template catalog. The pipeline now rejects unsupported standard document kinds before invoking a renderer.
- Added read-only `/api/document-renderers` discovery endpoint for future UI and administration clients.
- Renderer discovery now includes document titles and required manual fields, so clients can build generation forms from server metadata.
- Pipeline input validation now rejects missing document kinds, titles, and custom template keys before composition or persistence.
- Added `IDocumentRenderer` and routed all built-in document kinds through registry-selected renderer instances. The initial renderer delegates to the existing provider-selected composition service, preserving output while enabling per-document extraction and tests.
- Interrogatories now has a dedicated `InterrogatoriesRenderer` registry entry, establishing the first document-specific extraction point without changing generated output.
- Requests for Admission now has a dedicated `RequestsForAdmissionRenderer` registry entry, preserving its existing numbering and issue-tag behavior.
- Judgment and settlement document kinds now use explicit built-in renderer instances rather than an untyped composition fallback.
- Custom text and DOCX generation now use an explicit `ICustomDocumentRenderer` boundary, preserving the existing template storage and export behavior.
- Extracted shared discovery-renderer support for issue-tag warnings, duplicate suppression, and continuous numbering into `DiscoveryRendererSupport`.
- Extracted semantic reference resolution and hard-coded cross-reference warnings into `DiscoveryRendererSupport` as well.
- Removed the duplicated discovery helper implementations from `DocumentCompositionService`; the extracted support is now the single active path.
- Interrogatories and Requests for Admission now execute through `DiscoveryDocumentRenderers`; the shared composition service only coordinates workspace/template loading for these kinds.
- Removed the legacy Interrogatories and Requests for Admission rendering methods from `DocumentCompositionService`; the extracted renderer classes are now authoritative.
- Added regression tests for extracted discovery numbering and semantic-reference warnings.
- Stability audit: client tests pass 24/24, client production build passes, server Release web build passes with 0 warnings/errors, and the existing compiled server test assembly passes 92/92 via `dotnet vstest`. Rebuilding the test/migrator projects still requires NuGet restore access.
- Added `/api/document-catalog`, a unified read model combining built-in documents, uploaded custom templates, and active discovery-item availability for case-level Documents workflows.
- Case Documents now loads the unified catalog and uses its active built-in entries for the generation selector; client tests (24/24) and production build pass after the change.
- Case-level discovery assembly now supports searchable inclusion/exclusion of active tagged items and additional case-specific lines. Selections are request-scoped and do not alter shared master items.
- Added `/api/discovery-sets` and case-level quick selection of default discovery sets, using the same shared discovery item records.
- Added template rescan support (`POST /api/document-catalog/rescan`) and a Rescan Templates action in the case Documents tab.
- Added case Documents catalog search and category filtering for available generation templates.
- Added a built-in document Word export path (`POST /api/cases/{id}/generate-document-docx/{kind}`) using Open XML; it produces a valid `.docx` download from the assembled preview while binary generation-history persistence remains the next storage step.
- Added an Open XML validity regression test for built-in Word output.
- Built-in `.docx` exports now persist binary output paths and generated-document history through `IBinaryGeneratedDocumentService` for both SQLite and SQL Server providers; local endpoint smoke returned HTTP 200.
- Built-in and uploaded custom DOCX generation now accept a user-selected output filename, sanitize it server-side, and persist the resulting `.docx` under the case's document storage path; the refreshed local package passed health, catalog, and valid-DOCX smoke checks.
- Added a DOCX regression test covering merge tokens split across Word runs while preserving unrelated run formatting. The test source is ready, but the complete test-project rebuild still requires the missing .NET 10 NuGet packages listed in the build handoff notes.
- Added `scripts/local-package-smoke.ps1` and packaged `verify-local.ps1` so a local or IT reviewer can independently verify server startup, catalog availability, and valid DOCX generation; the packaged check passed with five catalog entries.
- Added SQL Server migration `020_reference_library.sql` and a provider-selected `SqlServerReferenceLibraryStore`; centralized reference-document edits now have a database boundary while SQLite retains its local file-backed behavior.
- Added `scripts/export-reference-library-sql.ps1`, which generated a reviewable 94 KB seed script from the current local reference files for controlled IT import.
- Fixed local reference-library deletion semantics with tombstones: removing a built-in document no longer causes a misleading “file not found” entry to reappear; saving it explicitly restores the document.
- Added a regression test for reference-library delete/restore behavior; rebuilding the complete test project still requires the unavailable .NET 10 NuGet packages.
- Added reference-library reconciliation to `/api/database/cutover-readiness` and exposed `/api/database/reconciliation/reference-library`, comparing keys, metadata, and text before SQL activation.
- Unified the case Documents tab around category-first selection and removed the case-level Search Templates, Custom Templates, Saved Custom Templates, and Merge Tags panels. Uploaded-template administration now lives under Settings → Document Templates.
- Case Summary and Case Review now use the binary DOCX generation/history path; the packaged smoke verifier confirms valid DOCX output for discovery, summary, and review.
- Extracted custom text-template merge rendering into `CustomDocumentRenderSupport`, leaving composition responsible for loading workspace and template data only.
- Extracted pure DOCX merge/token generation into `CustomDocxRenderSupport`; file storage and generated-document persistence remain transactional in the composition service.
- Added focused pipeline validation tests covering finalized placeholders, draft behavior, and duplicate missing-token normalization.
## 2026-07-16 corrective document pass

- Extended the existing custom-template/catalog contract with description, category, tags, visibility,
  ownership, and default-output metadata; custom uploads now flow through the same catalog shape as built-ins.
- Added `Manage My Templates` alongside `Add Template` in the case Documents utility row.
- Restored the copyable merge-field registry in both case Documents and Settings â†’ Document Templates.
- Verified client production build and server Release build after these changes.
- Added SQL migration `021_unified_document_template_metadata.sql` with additive catalog metadata and normalized
  document/discovery tag link tables; deployment instructions are in `docs/sql-server-migration.md`.
- SQL Server custom-template reads now consume the additive metadata columns when migration 021 is present.
- Added explicit `DateOpened` to the case model, SQLite/SQL Server persistence, case editor, and document merge
  registry. `ClosedDate` remains user-controlled; invalid closed-before-opened values are rejected. Migration 022
  adds the SQL Server column and reporting indexes.
- Expanded the Reports page with inclusive Date Opened/Date Closed range filters, Reset Filters, lifecycle columns,
  and calculated age/duration days used by the existing CSV and Excel exports. The current report page still reads
  the already-loaded case catalog; server-side pagination/query execution remains the next scale-up step for very
  large datasets.
- Extended `CaseCatalogQuery` and `GET /api/cases` with inclusive `dateOpenedFrom`, `dateOpenedTo`, `dateClosedFrom`,
  and `dateClosedTo` parameters. Both SQLite and SQL Server providers apply those predicates in the database query.
- Reports now call the filtered case API when lifecycle filters change and show compact matching/open/closed,
  average closed-duration, and average open-age metrics. CSV/Excel export uses the filtered result set.
- Added median, shortest, longest, and missing-date exclusion metrics with an explicit calculation note on the
  Reports page.
- Added editable opened-date presets for Last 30/90 days, Last 6/12 months, This calendar year, and Previous
  calendar year.
- CSV and Excel lifecycle exports now include report title, generation timestamp, and active date/status filters;
  Excel writes the metadata above the tabular header.
- Case imports now recognize the optional `DATE OPENED` column for both SQLite and SQL Server import paths; the
  existing record-created timestamp remains unchanged.
- Refreshed the generated CSV/XLSX import templates and sample rows to demonstrate the explicit Date Opened column.
- Added `/api/reports/lifecycle-readiness`, an access-filtered migration-readiness summary showing missing opened
  dates, closed cases without closure dates, and invalid date order without silently backfilling existing records.
- Added a Reports-page `Check Lifecycle Data` action that displays the readiness counts directly to administrators.
- Added an optional `Show lifecycle dates` toggle to the main case list, with sortable Date Opened and Date Closed
  columns when enabled.
- Closing a case now prompts for Date Closed instead of silently assigning today; reopening prompts whether the
  active closure date should be cleared while preserving the prior value in audit history.
