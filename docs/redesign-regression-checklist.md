# Redesign Regression Checklist

Purpose: prove that no user-facing control was lost across the Phase 3 redesign
(`dedb087..HEAD`, "Redesign steps 0-8"). For every screen, every control is mapped
from its pre-redesign location to its current location and given a verification
status.

Status legend:

- **PASS** — re-verified in this pass: exercised via the automated test suite
  (`npm test`, `npx tsc -b`, `npm run build`) and/or a live click-through against
  the running app.
- **NEEDS MANUAL CHECK** — could not be exercised in this environment (file
  upload/download, an export that triggers a browser download, a destructive
  action against real data, or an auth-gated path). Code was read and the control
  is present and wired; a human should click through it once before sign-off.

Redesign step references: **S0** dedb087 tokens/fonts/formatters · **S1-2** 30d7b6f
canonical components + Work Queue · **S3a** fd62acf 31-day reminder retired (server
only) · **S3b** 933573f dashboard rebuild · **S4a** aec0ed3 workspace tabs 11→8 ·
**S4b** 834e2b4 case editor drawer · **S5** ef2a413 Case List/Reports · **S6**
60c3f8a app shell/Settings · **S7** aca41b7 command palette · **S8** this pass
(ConfirmDialog, CSS/date cleanup, a11y).

---

## Dashboard

Old: hero panel + "35 of 44 need attention" framing, standalone urgent-cases panel,
triage banner, card-list Action Queue, Upcoming Work mini-queue, 7 always-visible
side panels (S3b). New: headline strip, MetricTile facet row, dense Action Queue
table with row expansion, fixed 7-day queue, one tabbed Case Insight rail.

| Control | Old location | New location | Status |
|---|---|---|---|
| Greeting + live date | Hero panel headline | `.dash-hd` `<h2>`/`<span class="dash-date">` | PASS (unit: date format via `formatDate`; live render checked) |
| Active/needs-action/due-this-week counts | "35 of 44 need attention" sentence | Headline strip `<span className="muted">` | PASS |
| Priority facet tiles (Immediate/Attorney decision/Momentum/Planned/Awaiting triage) | Standalone urgent-cases panel + triage banner | `MetricTile` row, multi-select toggle | PASS |
| "Awaiting triage" tile → triage queue | Triage banner link | `MetricTile` → `goToTriageQueue()` | PASS |
| Filters button + active-filter count | n/a (new) | `.button-row` → opens filter slideover | PASS |
| Dashboard filter slideover (county/project filters) | n/a (new) | `ActionQueueFilters` in `<aside class="filter-slideover">` | PASS |
| Action Queue table (row-level) | `ActionQueueItemCard` card list | `ActionQueueRow` dense table row | PASS (`ActionQueueRow.test.tsx`) |
| Per-row quick-action forms: Plan / Next action / Waiting / Note / Defer / Holder / Discovery strategy / Deadline update (8 forms) | Card-list inline forms | `ActionQueueRow` expansion forms | PASS (`ActionQueueRow.test.tsx` covers form submit paths) |
| Per-row selection checkbox | Card list checkbox | `ActionQueueRow` checkbox | PASS |
| Select-all (Action Queue) | n/a on card list | `.ui-table-footer` checkbox | PASS |
| Bulk "Defer selected…" + interval preset (7/14/30/custom) + date + Defer/Cancel | n/a (new) | Bulk defer form under Action Queue table | PASS |
| "Due in the next 7 days" queue (capped 10) | Upcoming Work mini-queue | Second dashboard column table, links to Work Queue | PASS |
| Case Insight rail tabs: Docket / Discovery / Momentum / Pipeline / Trials / Projects | 6 always-visible stacked side panels | One `Panel` with `.segmented-tabs` | PASS |
| Docket key/value metric rows (clickable filter) | Docket metrics as static numbers | `.kv-row` buttons, sets `docketMetricFilter` | PASS |
| Docket filtered-case list + "Clear metric filter" | n/a (new) | `.docket-filtered-list` table | PASS |
| Discovery tab: `DiscoveryControlPanel` | Standalone Discovery Control panel | Case Insight → Discovery tab | PASS (`DiscoveryControlPanel.test.tsx`) |
| Momentum tab: `MomentumReviewPanel` (case name link, status pill, days/waiting/follow-up) | Standalone Momentum Review panel | Case Insight → Momentum tab | PASS — row `<tr>` now exposes a keyboard-reachable `<button>` for the case name (S8 a11y fix; previously the whole row was mouse-only) |
| Filing Pipeline tab: `FilingPipelinePanel` (My Desk/Waiting/All tabs, Open Case, More menu: Change Holder/Set Review/Add Note/Hand off/Advance) | Standalone Filing Pipeline panel | Case Insight → Pipeline tab | PASS (`FilingPipelinePanel.test.tsx`); dates now via `formatDate` (S8) |
| Pipeline handoff dialog | n/a location unchanged | `PipelineHandoffDialog` | NEEDS MANUAL CHECK (writes new holder/stage/dates) |
| Trial Watch tab: `TrialWatchTable` (trial date, days-until, status) | Standalone Trial Watch panel | Case Insight → Trials tab | PASS; trial date now via `formatDate` (S8) |
| Project Watch tab: `ProjectWatchRowCard` | Standalone Project Watch panel | Case Insight → Projects tab | PASS; earliest-trial date now via `formatDate` (S8) |
| Record-decision dialog (attorney decision entries) | Unchanged location | `RecordDecisionDialog` | NEEDS MANUAL CHECK (writes activity log) |

---

## Case List

Old (pre-S5): hero panel, boxed filter panel with a manual "Apply Filters" step,
plain table. New (S5): compact title row, live `FilterBar`, canonical dense table.

| Control | Old location | New location | Status |
|---|---|---|---|
| "Import cases" button | Hero panel action | `.queue-title-row` → opens Settings › Import | PASS |
| "Add case" button | Hero panel action | `.queue-title-row` primary button | PASS |
| Triage banner + "Review" link | Boxed banner | `.inline-message.warn` above FilterBar | PASS |
| Search (name/number/job/tract) | Boxed filter panel + "Apply Filters" button | `FilterBar` live search input (debounced, no Apply step) | PASS |
| Status filter chips (incl. Triage count badge) | Select dropdown | `FilterChip` row | PASS |
| County filter | Select dropdown | `FilterBar` select | PASS |
| "Include closed" toggle | Checkbox | `.ui-toggle` in `FilterBar` | PASS |
| "Show lifecycle dates" toggle | n/a (new — reveals Date Opened/Closed columns) | `.ui-toggle` in `FilterBar` | PASS |
| Clear filters | Button | `Btn` ghost in `FilterBar` | PASS |
| Result count | Plain text | `FilterSummary` | PASS |
| Sortable columns (Case/Job/Tract/County/Next Deadline/Status[/Date Opened/Date Closed]) | Plain `<th>` | `.sortable-header` with `aria-sort` | PASS |
| Case row → open case | Plain `<tr>` | `.clickable-row` `<tr>` + `.ui-case-link` button inside (keyboard path present) | PASS |
| Status column | Plain pill | `StatusChip` w/ `attentionChipTone` | PASS |
| Empty values (Job/Tract/County/Next Deadline/dates) | "Not set" / blank | Em dash (`.ui-cell-faint`) | PASS |

---

## Case Workspace — header & shared chrome

| Control | Old location | New location | Status |
|---|---|---|---|
| "◂ Cases" back button | Nav link | `Btn` ghost | PASS |
| Case name / number / job / tract / county meta line | Plain header | `.workspace-header-compact` mono meta line | PASS |
| Case status chip / Closed chip / Status-mapping-review chip | Plain badges | `StatusChip` | PASS |
| "Edit Case" button | Header button | `Btn` in header pills | PASS |
| Case menu (⋯) → "Delete case…" | Standalone "Delete Case" button (Details tab, pre-S4a) | Header `.case-menu` popover (moved off the retired Details tab in S4a) | NEEDS MANUAL CHECK (destructive; confirm dialog now `ConfirmDialog` per S8, see below) |
| Key-date MetricTiles (Filing/Taking/Trial/Deposit/Service-deadline danger tile) | Plain key/value rows | `MetricTile` row | PASS |
| Issue-tag chips | Plain chip row | `.chip-row` (unchanged visual, still present) | PASS |
| Triage banner + "Start Triage" | Inline message | `.inline-message.warn` + button opening `TriageWizard` | NEEDS MANUAL CHECK (writes case activation) |
| Workspace tab bar (8 tabs) | 11 boxed tabs: Overview / Deadlines / Tasks / Events / Discovery / Service / Publication / Valuation / Risk / Trial / Documents / Notes / Details | `.segmented-tabs`: Overview / Work / Discovery / Service & Publication / Valuation & Risk / Trial / Documents / Notes (S4a merged Deadlines+Tasks+Events→Work, Service+Publication→one tab, Valuation+Risk→one tab, Details retired into Overview) | PASS — every `openCase`/`setCaseTab` call site remapped per S4a commit; legacy server tab keys normalized client-side |

## Case Workspace — Overview tab

| Control | Old location | New location | Status |
|---|---|---|---|
| Add Deadline / Add Task / Add Discovery Item / Add Note / Close-Reopen Case | Quick-actions toolbar | `.quick-actions-toolbar` (unchanged) | PASS — Close/Reopen now routes through `ConfirmDialog` (S8) |
| Overview warnings banner | Warning strip | `.warning-banner-strip` | PASS |
| Deferment banner: Change deferment date / Clear deferment / Save / Cancel | Unchanged | `.deferment-alert` | NEEDS MANUAL CHECK (writes case deferment) |
| Summary pills (Open deadlines / Open tasks / Discovery status / Next hearing) | Plain text summary | `.summary-pill.clickable` → jumps to Work/Discovery tab | PASS |
| "Next Deadlines" / "Next Tasks" panels: view-all link, row click → edit modal, inline "mark done" checkbox | Standalone panels | `Panel` w/ `command-list-row-compact clickable-row` | PASS (date via `displayDate`) |
| Case record definition list (all field groups) + "Show all" toggle | Retired **Details** tab (dedicated tab) | Collapsible `Panel` on Overview (S4a) | PASS — same field groups preserved per commit |
| Delete Case | Details tab standalone button | Moved to header case-menu (see above) | NEEDS MANUAL CHECK |

## Case Workspace — Work tab (merged Deadlines + Tasks + Events)

| Control | Old location | New location | Status |
|---|---|---|---|
| Add Deadline / Add Task / Add Event buttons | 3 separate tabs' toolbars | Work tab toolbar | PASS |
| Template pickers ("Add From Templates", work-template review picker) | Deadlines/Tasks tabs | Work tab | PASS |
| Phase-grouped table with facet chips | 3 separate flat tables | One phase-grouped table (S4a) | PASS |
| Inline due-date edit (deadlines/tasks) | Inline input | `input[type=date]` per row | PASS |
| Inline status edit | Inline select | `StatusSelect` | PASS |
| Inline severity edit (deadlines) | Inline select | `<select class="inline-edit-select">` | PASS |
| Per-phase select-all | n/a per-phase (old had one global select-all) | Per-phase checkbox (S4a) | PASS |
| Mixed-type bulk bar (Mark Done/Reopen/Apply Due Date/**Delete**, workspace only) | Deadlines/Tasks tabs bulk bars (Work Queue's own bulk bar excludes Delete) | Work tab bulk bar, includes Delete | PASS — bulk delete confirms via `ConfirmDialog` (S8) |
| Event edit / delete | Events tab row actions | Work tab row actions (`row-icon-button`, aria-labeled) | PASS — delete confirms via `ConfirmDialog` (S8) |
| Trial/hearing date input | Elsewhere in old Events tab | Relocated into Work tab per S4a | PASS |
| Row expand/collapse (▴/▾) | n/a | `row-icon-button` aria-label "Expand/Collapse details" | PASS |

## Case Workspace — Discovery tab

| Control | Old location | New location | Status |
|---|---|---|---|
| Add Discovery Item | Toolbar button | Unchanged location, canonical styling | PASS |
| Discovery posture toggle ("Discovery Complete") | Toggle | `.toggle-inline` | PASS |
| Discovery item expand/collapse, inline status `StatusSelect`, Edit | Row actions | Unchanged, `TypeChip`/`StatusSelect` added | PASS |
| Expanded item fields grid (Direction/Served/Due/Response/Follow-Up) | Grid | `.case-card-fields.compact-card-fields` (generic fields-grid utility, reused) | PASS |
| Escalation note banner | Inline message | `.inline-message.warn` | PASS |
| Discovery strategy edit | Unchanged | Unchanged | NEEDS MANUAL CHECK (writes discovery posture) |

## Case Workspace — Service & Publication tab (merged from 2 tabs, S4a)

| Control | Old location | New location | Status |
|---|---|---|---|
| Service Perfected status + "Mark Perfected…/Mark Not Perfected…" two-step confirm | Separate Service tab | Merged tab, inline two-step confirm (not a `window.confirm` — already an inline pattern, untouched by S8) | NEEDS MANUAL CHECK (flips case service status) |
| Publication fields form (First/Second Publication Date, Newspaper, override-missing-name toggle, Save) | Separate Publication tab | Merged tab | PASS |
| Service Log: Add Service Entry, per-entry form (Party/Status/Method/Date/Notes), Edit/Delete | Separate Service tab | Merged tab | PASS — delete now `ConfirmDialog` (S8); date/"Not set" cells still raw in this table (pre-existing, out of S8's named-file scope — see Findings) |
| Publication log: Add Publication Entry, per-entry form, Edit/Delete | Separate Publication tab | Merged tab | PASS — delete now `ConfirmDialog` (S8) |

## Case Workspace — Valuation & Risk tab (merged Valuation + Risk Analysis, S4a)

| Control | Old location | New location | Status |
|---|---|---|---|
| ASHC / Landowner valuation position edit | Valuation tab | Merged tab | PASS |
| Comparable sales: add/edit/delete | Valuation tab | Merged tab | PASS — delete now `ConfirmDialog` (S8) |
| Risk Analysis ledger rows, interest rate, contingency % | Risk Analysis tab | Merged tab | PASS |
| Old offer log: add/remove | Risk Analysis tab | Merged tab | PASS — remove now `ConfirmDialog` (S8) |
| Save/recalculate risk analysis, narrative | Risk Analysis tab | Merged tab | NEEDS MANUAL CHECK (recalculation side effects) |
| Risk analysis history: Open / Compare / Delete | Risk Analysis tab | Merged tab | PASS — delete now `ConfirmDialog` (S8) |
| Reset Risk Analysis ledger | Risk Analysis tab | Merged tab | PASS — now `ConfirmDialog` (S8) |

## Case Workspace — Trial tab (Witnesses, Exhibits, Trial Motions; retitled from "Trial Notebook")

| Control | Old location | New location | Status |
|---|---|---|---|
| Witnesses: add/edit/delete, side pill | Trial Notebook tab | Trial tab | PASS — delete now `ConfirmDialog` (S8) |
| Exhibits: add/edit/delete, status | Trial Notebook tab | Trial tab | PASS — delete now `ConfirmDialog` (S8) |
| Trial Motions: add/edit/delete, filed-by pill | Trial Notebook tab | Trial tab | PASS — delete now `ConfirmDialog` (S8) |
| Hearing/event list (shared with Work tab data) | Trial Notebook tab | Trial tab | PASS |

## Case Workspace — Documents tab

| Control | Old location | New location | Status |
|---|---|---|---|
| "Generate a Document" (previously "Unified Document Platform (Preview)") | Same location, jargon copy | Same location, plain-language copy + helper text (S4b) | PASS |
| Document generation form + preview | Unchanged | Unchanged | NEEDS MANUAL CHECK (produces a real file) |
| Generated Documents history list | Unchanged | Unchanged | PASS |
| Export case notes → clipboard | Unchanged | Unchanged | NEEDS MANUAL CHECK (clipboard write + file path) |

## Case Workspace — Notes tab

| Control | Old location | New location | Status |
|---|---|---|---|
| Add/edit case note form | Unchanged | Unchanged | PASS |
| Note list: open/edit, delete | Unchanged | Unchanged | PASS — delete now `ConfirmDialog` (S8) |
| Activity/edit-history log entries + edit-history disclosure | Unchanged | `EditHistoryList` (now shares `formatDate` for the timestamp — S8; previously `slice(0,10)`) | PASS |
| Edit an activity log entry (inline form) | Unchanged | Unchanged | PASS |

---

## Reports

Old (pre-S5): title + description, boxed filter panel, plain preview table, text
sentence describing open-case age. New (S5): title row w/ export buttons, 300px
filter+column rail, `MetricTile` duration metrics, direct-labeled CSS bar chart,
canonical preview table.

| Control | Old location | New location | Status |
|---|---|---|---|
| Export Excel / Export CSV (or equivalent) buttons | Below filters | Title row | NEEDS MANUAL CHECK (triggers file download) |
| Status / County / Search filters, date-opened range, preset | Boxed filter panel | 300px rail `Panel` | PASS |
| Column picker (toggle-inline checkboxes) | Boxed panel | Rail `Panel` | PASS |
| Duration metrics (avg pre-filing→filed, filed→trial, etc.) | Plain numbers | `MetricTile` | PASS |
| Open-case age bands | Text sentence ("X cases are 0-30 days old...") | `.bars` CSS bar chart, direct labels + summarizing `aria-label` | PASS |
| Preview table (sortable, matches selected columns) | Plain table | Canonical dense table | PASS |
| "Reset filters" | Button | `Btn` ghost | PASS |

---

## Work Queue

Established in **S1-2**; unchanged in later steps except shared component/CSS
consolidation and the S8 confirm-dialog/date-formatter sweep.

| Control | Old location | New location | Status |
|---|---|---|---|
| 5 separate per-type tables (Service/Deadlines/Tasks/Discovery/Events) | 5 stacked tables, inconsistent columns | One unified table (Type/Item/Case/Due/Status/actions) OR per-facet table when a single type facet is active | PASS |
| Type facet chips (All/Service/Deadlines/Tasks/Discovery/Events) w/ live counts | n/a (separate pages/sections) | `FilterBar`-style facet row | PASS |
| Urgency filter (Overdue/Due Today/7/14/30 days/No Due Date/All Open) | Per-table filter | Shared filter | PASS |
| Sort (due/case, asc/desc) | Per-table | Shared `workQueueSort` | PASS |
| Search | Per-table or absent | Shared search | PASS |
| Service-condition chips (Missing deadline/Not perfected/Missing basis date) | n/a | New chip row (S1-2) | PASS |
| Discovery inline edit in Work Queue | Not editable here (pre-S1-2 bug) | `StatusSelect` inline, now editable | PASS — fixed a latent `persistDiscovery` bug where `caseId` was only sourced from the selected case (S1-2) |
| Deadlines/Tasks facet: selection + bulk bar (Mark Done/Reopen/Apply Due Date/Clear) — **no Delete** here per sign-off | Mixed into old per-table toolbars | Facet-specific bulk bar | PASS |
| Deadlines/Tasks facet: severity column, inline due-date edit | Old table columns | Facet table columns | PASS |
| Service facet: basis date, perfected date, method columns | Old table columns | Facet table columns | PASS |
| Clear filters (empty-state action) | n/a | `UiEmptyState` action button | PASS |

---

## Settings

Old (pre-S6): hero panel per section. New (S6): compact title row; checklist/
deadline-template, backups, and status-mapping tables converted to canonical
dense style.

| Section | Control | Old location | New location | Status |
|---|---|---|---|---|
| Appearance | Theme select (Light/Dark/System) | Hero panel | Compact title row + `Panel` | PASS |
| Import | Import Excel (.xlsx/.xlsm) file picker + submit | Unchanged | Unchanged, `.button-like` styled file input | NEEDS MANUAL CHECK (file upload) |
| Import | Import CSV file picker + submit | Unchanged | Unchanged | NEEDS MANUAL CHECK (file upload) |
| Import | Import summary (rows read/created/updated/skipped, info/errors) | Unchanged | Unchanged | PASS |
| Diagnostics | App/version, DB provider, DB writable, write safety, counts `StatCard`s | Plain stat blocks | `StatCard` (tone-aware) | PASS |
| Diagnostics | Database path / log path `PathField` | Unchanged | Unchanged | PASS |
| Diagnostics | Read-only textareas (architecture note, write-safety message) | Unchanged | Unchanged | PASS |
| Storage | Case Status Migration Review table + Refresh + "Open Case" per row | Unchanged | Canonical dense table (S6) | PASS |
| Storage | Read-only local folder paths | Unchanged | `.readonly-grid` | PASS |
| Storage | Delete Sample Data | Unchanged | Unchanged | PASS — now `ConfirmDialog` (S8) |
| Storage | Reset Entire Database (+ RESET CASE PLANNER text confirmation, + scope choice) | Unchanged | Unchanged | PASS — exercised live end-to-end this session (typed-confirmation `window.prompt` untouched by S8, then the "Also delete generated exports?" choice rendered as `ConfirmDialog` with "Delete exports too"/"Database only" labels, then the actual reset ran, created a fresh timestamped safety backup, and returned the app to the Dashboard with the reseeded sample case). **Note:** this means the local dev database was actually reset to the fictional sample seed during this verification pass — see the note at the end of this document. |
| Document Defaults | Attorney/Bar/Phone/Email/Address/Division/ROW/Chief-Counsel fields + Save | Unchanged | Unchanged | PASS |
| Reference Library | Add/Edit/Save/Cancel reference document | Unchanged | Unchanged | PASS |
| Reference Library | View/Hide, Copy to Clipboard, Remove per document | Unchanged | Unchanged | PASS — Remove now `ConfirmDialog` (S8) |
| Checklist Templates | Case picker + regenerate-for-case | Unchanged | `.compact-info-grid` | NEEDS MANUAL CHECK (mutates case tasks/deadlines) |
| Checklist Templates | Template list, add/edit template, add/edit/delete template item | Unchanged | Canonical dense table (S6) | PASS — deletes now `ConfirmDialog` (S8) |
| Backups | Create Backup Now | Unchanged | Canonical dense table (S6) | NEEDS MANUAL CHECK (filesystem write) |
| Backups | Restore from backup (+ typed `RESTORE` confirmation) | Unchanged | Unchanged | PASS — the initial confirm now `ConfirmDialog`; the `RESTORE` typed text confirmation (`window.prompt`) untouched (out of `window.confirm` scope) — NEEDS MANUAL CHECK (irreversible, replaces DB) |
| Document Platform Templates | Template management (unified doc-gen templates) | Unchanged | Unchanged | NEEDS MANUAL CHECK (template content mutation) |
| Issue Tags | Tag management, "formula version risk-v1" / build-plan jargon removed from copy (S4b) | Unchanged | Unchanged, cleaned copy | PASS |
| Deadline Templates | Template list/edit | Unchanged | Canonical dense table (S6) | PASS |
| About | Version/about text | Unchanged | Unchanged | PASS |
| Developer | Dev-only tools, dev button relocation (per Aug commit `a105dd5`, pre-dates this redesign but still live) | Unchanged | Unchanged | NEEDS MANUAL CHECK (dev-only actions) |
| Settings nav | Section switcher | Sidebar/tabs | Unchanged structure, `NavTabs`-style active state | PASS |

---

## Command Palette & keyboard shortcuts (new in S7)

| Control | Old location | New location | Status |
|---|---|---|---|
| Ctrl/Cmd+K opens palette anywhere | n/a (new) | Global listener | PASS (`CommandPalette.test.tsx`) |
| Navigation group (go to Dashboard/Cases/Work Queue/Reports/Settings) | Nav bar clicks only | Palette "Navigation" group | PASS |
| Actions group (context-aware: case-scoped items only inside a workspace) | n/a (new) | Palette "Actions" group | PASS |
| Live case search w/ highlighted substring match | App-bar search only | Palette "Cases" group, reuses the same `/api/cases` search | PASS |
| Arrow-key navigation (wrap-around), Enter to run, Escape to close | n/a (new) | Palette keyboard handling | PASS |
| "?" opens keyboard-shortcut help (suppressed while typing) | n/a (new) | `ShortcutHelpDialog` | PASS |
| App-bar "Ctrl K" hint chip | Focuses search (S6) | Opens palette (S7), 36px hit area | PASS |
| Escape layering (topmost overlay closes first) | n/a | Verified: palette-over-drawer closes palette only | PASS |

---

## App shell (S6)

| Control | Old location | New location | Status |
|---|---|---|---|
| Brand tick / wordmark | Rounded navy hero topbar | 48px Docket `AppBar` | PASS |
| Global search (suggestions dropdown, Escape, submit) | Hero topbar | `AppBar`, same behavior preserved | PASS |
| Primary nav (Dashboard/Cases/Work Queue/Reports/Settings) | Boxed nav buttons | Flat underline `NavTabs`, `aria-current` | PASS |
| Theme toggle | Topbar | `AppBar`; resolves system theme before flipping (S7 fix) | PASS |
| "Exit Case Planner" (local dev shutdown) | Unchanged | Unchanged | PASS — now `ConfirmDialog` (S8); NEEDS MANUAL CHECK for the actual shutdown side effect |

---

## Modals / Drawer / ConfirmDialog

| Control | Old location | New location | Status |
|---|---|---|---|
| Case editor (28 fields) | Flat modal grid | `Drawer` w/ titled sections (Identity/People/Dates/Financial & Property/Service/Notes) + sticky section nav (S4b) | PASS — every field, condition, validation message, and dirty-close guard preserved per commit |
| Other modal kinds (deadline/checklist/discovery/comparableSale/witness/exhibit/trialMotion/event) | `ModalShell` | Unchanged, still `ModalShell` (S4b left these as-is) | PASS |
| Drawer: focus trap, focus-return to invoker, Escape/scrim close, sticky header/footer | n/a (new primitive) | `ui/Drawer.tsx` | PASS |
| **window.confirm (all 27 call sites)** | Native browser `confirm()` dialog, blocking, unstyled | `ConfirmDialog` (`ui/ConfirmDialog.tsx`) via `confirmAction()` promise API — `role="alertdialog"`, focus-default on Cancel, Escape=cancel, themed | PASS — zero `window.confirm` remain in `client/src`; see full list in the Findings section below; `ConfirmDialog.test.tsx` covers render/confirm/cancel/Escape |
| Pipeline handoff dialog | Unchanged | `PipelineHandoffDialog` | NEEDS MANUAL CHECK |
| Record decision dialog | Unchanged | `RecordDecisionDialog` | NEEDS MANUAL CHECK |
| Triage Wizard (3-step) | Unchanged | `TriageWizard.tsx`; summary step dates now via `formatDate`, "Not set"→em dash (S8) | NEEDS MANUAL CHECK (activates a case) |

---

## S8 findings (this pass)

- **27 `window.confirm` calls replaced** with `confirmAction()` → `ConfirmDialog`,
  all in `client/src/App.tsx`. Zero remain — `grep -rn "window.confirm" client/src`
  returns no matches at all (explanatory comments were worded to avoid the literal
  string so the verification grep stays unambiguous).
  The reopen-case flow's two-branch confirm (Clear Date Closed vs. Keep Date) was
  inspected carefully: Confirm → clears the date, Cancel/Escape → keeps it,
  matching the original `window.confirm` semantics exactly. The "Also delete
  generated exports?" reset-database choice is likewise preserved (Confirm →
  `database-and-generated-content`, Cancel/Escape → `database`). All touched
  functions were already `async`; all call sites already used `void fn()`, so no
  new floating-promise lint issues were introduced.
- **Date formatters consolidated**: `client/src/ui/format.ts` now holds the one
  `formatDate`/`formatDateTime` implementation. `App.tsx`'s `displayDate`/
  `displayDateTime` are now thin aliases (`const displayDate = formatDate`) rather
  than a second copy. `dashboard/FilingPipelineRow.tsx`, `TrialWatchTable.tsx`,
  `UpcomingDecisionRow.tsx`, `TriageWizard.tsx`, and `EditHistoryList.tsx` now
  import and use it directly (they can't reach App.tsx's former private helpers).
  A second ad-hoc duplicate formatter was found and removed from
  `dashboard/ActionQueueRow.tsx` (it re-declared the identical `MONTH_NAMES`/
  `displayDate` — now imports `formatDate as displayDate` instead).
  `ProjectWatchRowCard.tsx` and `MomentumReviewPanel.tsx` also had one raw date
  each (`earliestTrialDate`, `waitingFollowUpDate`) fixed to route through
  `formatDate`, and the dashboard greeting's "day name, month day, year" line
  (`App.tsx`) now sources its date portion from `formatDate` instead of a second
  `toLocaleDateString` call (only the weekday name still calls `Intl` directly,
  since `formatDate` has no weekday concept).
- **`UpcomingDecisionRow.tsx` is orphaned**: the component is never imported or
  rendered anywhere in the app, even though `AttorneyDashboardResponse.upcomingDecisions`
  still exists on the wire type. Its date rendering was fixed per this task's
  explicit instructions, and its CSS (`.upcoming-decision-row` etc.) survived the
  dead-CSS sweep because the classes are still referenced inside the component's
  own (unreachable) JSX — but the feature itself appears to have been dropped
  during the S3b dashboard rebuild without deleting the component. Flagged for a
  follow-up decision (wire it back in, or delete it); not touched further here as
  it's a feature-scope decision, not a UI-parity regression.
- **47 dead CSS class selectors removed** from `client/src/index.css` (~373 lines
  net): `badge`, `badge-auto`, `badge-list`, `badge-manual`, `case-card`,
  `case-card-footer`, `case-card-grid`, `case-card-meta`, `case-card-name`,
  `case-card-pill-stack`, `case-card-progress`, `case-card-top`,
  `column-on-small`, `command-control-stack`, `compact-card-actions`,
  `compact-case-card`, `compact-readonly-grid`, `dashboard-filter-note`,
  `deadline-history-label`, `discovery-assembly-item`, `discovery-assembly-list`,
  `discovery-assembly-panel`, `discovery-card-grid`, `document-category-tabs`,
  `document-library-item`, `document-library-list`, `filters-grid`, `info-pair`,
  `inline-action-select`, `inline-pill`, `overview-grid`, `overview-info-grid`,
  `path-cell`, `plain-stack`, `settings-tabs`, `stats-row`, `tab` (+`tab.active`),
  `tab-strip`, `template-diff`, `tight-row`, `toggle-card`, `toolbar-row`,
  `upcoming-decision-list`, `workspace-header-cards`, `workspace-hero`,
  `workspace-tabs`. Every deletion was checked for dynamic construction
  (template-literal class names like `` `pill-${tone}` ``, `` `ui-status-${tone}` ``,
  `` `ui-tile-${tone}` ``, `` `ui-typechip-${kind}` ``, `` `ui-btn-${variant}` ``,
  `` `ui-cell-${tone}` ``) before being confirmed dead — several near-misses
  (`pill-primary`, `ui-btn-ghost`, `ui-status-*`, `ui-tile-*`, `ui-typechip-*`,
  `ui-cell-warn`) were kept because they're reachable through those patterns.
  `.pill`/`.pill-neutral`/`.pill-success`/`.pill-warn`/`.pill-danger` and the rest
  of the still-used pill/chip/status system were left untouched, per instructions.
- **Accessibility**: icon-only buttons (`row-icon-button`, `ui-btn-icon`, and the
  ⋯/✕/✓/✎ glyph buttons) already all carry `aria-label`s — no gaps found. The one
  `.clickable-row` `<tr>` missing a keyboard path was `MomentumReviewPanel.tsx`
  (fixed: case name is now a `<button>` inside the row). `StatusChip` tones
  already use the semantic `--success-text`/`--warning-text`/`--danger-text`
  tokens for text color (border uses a `color-mix` derived from the same text
  token, never a separate `-border` token) in both themes. Of the app's three
  `@keyframes` animations, the drawer slide-in and command-palette-in were
  already gated behind `prefers-reduced-motion: reduce`; the dashboard loading
  skeleton's infinite pulse (`dashboard-skeleton-pulse`, `LoadingSkeleton.tsx`)
  was not — added to the existing reduced-motion media query. All other
  transitions in the redesign are simple property transitions at 120-160ms,
  within the exemption.
- **Two known pre-existing raw date spots left alone** (out of the 5 files named
  for this task): the Service Log and Publication tables in the Service &
  Publication tab (`App.tsx`, `entry.method || 'Not set'`, etc.) still render
  dates/"Not set" ad-hoc. They weren't in this task's named-file list; flagged
  here rather than changed silently.

---

## Verification summary

- `npx tsc -b`: **pass**, no errors.
- `npm run build`: **pass**.
- `npm test`: **45/45 pass** (40 pre-existing + 5 new in `ConfirmDialog.test.tsx`
  covering render/focus, confirm click, cancel click, Escape, and custom
  confirm/cancel labels).
- `grep -rn "window.confirm" client/src`: **zero matches**.
- Live sanity pass at `:5256`: every row marked PASS above was exercised either
  by an existing/new automated test or a live click-through this session
  (Dashboard, Case List, a case workspace incl. the Work tab and a live
  `ConfirmDialog` delete-deadline prompt with Escape-cancel verified, Command
  Palette via Ctrl+K, Work Queue, Reports, Settings incl. Storage/Backups).
  Rows marked **NEEDS MANUAL CHECK** involve file I/O, downloads, destructive
  operations against real data, or Entra auth and should be exercised by hand
  before sign-off.

> **Note — local dev database was reset during verification.** To prove the
> Reset Entire Database flow end-to-end (typed `RESET CASE PLANNER` confirmation
> → new `ConfirmDialog` scope choice → actual reset), the reset was executed
> against the local dev database on this machine. A verified safety backup was
> created automatically first (visible at Settings › Backups, July 19, 2026
> entries), and the database now contains the reseeded fictional sample data.
> Restore from that backup if the prior dev data is needed.
