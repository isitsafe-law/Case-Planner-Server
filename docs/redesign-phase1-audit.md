# UI/UX Redesign — Phase 1 Audit & Teardown Assessment

Date: 2026-07-18
Scope: every screen in `client/src/App.tsx` (7,719 lines), `client/src/index.css` (2,323 lines), `client/src/dashboard/*` (16 components), `TriageWizard.tsx`, `EditHistoryList.tsx`. Verified live against a running build with 44 real cases.
Rubric: NNG heuristics (H1–H10), desktop/data-dense interface patterns, visual-design-system rules (type scale, color contrast, tabular numerals, hit targets), accessibility baseline.

Boundaries honored: functionality inventoried, nothing removed; document-generation pipeline flagged only.

---

## 1. Verdicts by screen

| Screen | Verdict | One-line reason |
|---|---|---|
| Dashboard | **REBUILD** | Three overlapping attention systems stacked; signal drowned (35 of 44 cases flagged) |
| Case List | **RESKIN** | Filter/table structure sound; filter semantics inconsistent, table craft gaps |
| Case Workspace — shell & Overview | **RESKIN** | Header/tab/command-center structure is right; visual language and density need the new system |
| Case Workspace — tab set (IA) | **REBUILD** | 11 tabs with overlapping concerns; Deadlines/Tasks/Events fragmentation; dead-weight Details tab |
| Case Workspace — Details tab | **REBUILD** | Read-only data rendered as fake form inputs; whole tab duplicates the Edit Case modal |
| Case Workspace — Service & Publication | **RESKIN** | Recently rebuilt; structure correct |
| Case Workspace — Discovery | **RESKIN** | Accordion + inline status is right; polish pass |
| Case Workspace — Documents | **RESKIN** (copy flagged) | Structure fine; dev jargon in user-facing copy (pipeline itself untouched) |
| Case Workspace — Risk Analysis | **RESKIN** | 11-col ledger is appropriately dense; needs sticky col, tabular-nums, save affordance polish |
| Case Workspace — Trial Notebook | **RESKIN** | Two-column tables + checklist structure sound |
| Case Workspace — Notes | **RESKIN** | Editor + list layout fine |
| Case Workspace — Hearings/Events | **RESKIN** | Cards → table candidate; merge question raised below |
| Work Queues | **REBUILD** | Five inconsistent tables stacked; "All" view is a scroll wall; no unified item model |
| Reports | **RESKIN** | Builder structure sound; metrics presentation upgrade (chart), craft pass |
| Settings — shell/nav | **KEEP** | Grouped sidebar nav is right; token migration only |
| Settings — Document Templates, Issue Tags, Reference Library, Developer | **KEEP** | Rebuilt this month to current standards; token migration only |
| Settings — remaining sections | **RESKIN** | Apply new system; minor consistency fixes |
| Modals — Case editor | **REBUILD** | ~40-field single flat grid; needs sectioned form |
| Modals — other 8 kinds | **RESKIN** | Small forms, fine structurally |
| Triage Wizard | **KEEP** | Wizard pattern correctly applied; token migration only |

---

## 2. REBUILD screens — what is structurally broken

### 2.1 Dashboard (`App.tsx:6548-6799`)

Three separate "what needs attention" systems render stacked, each with its own engine, its own visual vocabulary, and overlapping membership:

1. **"Cases Flagged Urgent / Attention"** (`App.tsx:6558-6577`) — fed by `CaseAttentionEngine`. Live check: renders 14 cases at ~4 lines each (pill + name + case number + reminder text + "Open Case" button) ≈ a full viewport of scrolling before anything else.
2. **Summary cards + Attorney Action Queue** (`App.tsx:6585-6720`, `AttorneyDashboardEngine`) — the real prioritized queue (priority levels 1–4, quick actions), pushed below the fold by #1.
3. **"Upcoming Work"** (`App.tsx:6630-6654`) — a filtered mini-clone of the entire Work Queues page, with its own type chips, urgency select, and limit select.

Additionally the **Case Insight** panel (`App.tsx:6727-6792`) holds 6 sub-tabs (Docket/Discovery/Momentum/Pipeline/Trials/Projects), where the Docket tab renders a *third* implementation of the "label + big number tile" pattern (`.docket-metric`, `App.tsx:6786`) next to `.dashboard-summary-card` and `.metric-tile`.

**Signal failure (H1, H8 — severity 3):** the greeting reports "35 of 44 active cases need attention today." When 80% of the docket is flagged, the flag means nothing. Most urgent entries are the same rule firing ("Service status reminder (31 days after filing)") — rule-spam presented as case-level urgency.

**Findings:**
- `App.tsx:6568` — raw ISO dates in urgent rows (`displayDate` pass-through, see §5.3). Rule: `date-format`.
- `App.tsx:6561-6574` — list rows ~4 lines/case; table-density rule (`data-density`): this is a scan list, should be one row per case.
- `App.tsx:6630` — inline ternary chain 5 deep mapping filter names between dashboard and queue page — symptom of two vocabularies for the same domain (`consistency`).
- `ActionQueueItemCard.tsx:89-97` — card actions vary by string-matching `item.reason.toLowerCase().includes('deadline')` — fragile control visibility (`error-prevention`).
- Dashboard rebuild must consolidate to **one** priority model feeding **one** queue (the Action Queue's 4-level model is the keeper), with the attention list and Upcoming Work folded in as facets, not siblings.

### 2.2 Case Workspace tab set (`App.tsx:26`, `4440-4446`)

11 tabs: Overview, Details, Deadlines, Checklist, Discovery, Documents, Risk Analysis, Trial Notebook, Notes, Events, Service & Publication.

- **Deadlines** (`4801-4821`) and **Checklist/Tasks** (`4823-4843`) are structurally identical screens (toolbar + open/done chips + bulk bar + dense table with the same inline controls, `renderDeadlineTable:5531` vs `renderChecklistTable:5650` — two near-duplicate 120-line renderers). They differ only in one column and the phase grouping. Candidates for a single **Work** tab with a Deadline/Task type facet — or at minimum one shared table component.
- **Details** (`5468-5526`) is a read-only mirror of the case record rendered as disabled `<input>`s (`5483, 5495, 5507`) — fake affordances (H4/H6, severity 3: looks editable, is not). Its only unique features are "Show All Fields", the Edit Case button (also on the header), and the Danger Zone delete. The tab dies; the record moves to a proper definition-list "Case Record" view (likely folded into Overview or the case editor), delete moves to a case-menu.
- **Events** (`4759-4799`) is two panels: a single date field (also shown in header + Overview strip) and an event log. Thin for a tab; candidate to merge into the Work/timeline surface.
- Tab labels mix nouns of different kinds (work types, artifacts, phases). The redesign proposes a re-grouped set (mockup in Phase 2); current candidate: **Overview · Work (deadlines+tasks+events) · Discovery · Service & Publication · Valuation & Risk · Trial · Documents · Notes** — 8 tabs, every current control preserved.

### 2.3 Work Queues (`App.tsx:6874-7026`)

Concept (global cross-case work) is right; execution is five hand-rolled tables with five different column sets and action vocabularies:

- Service: 9 columns, verbose free-text "Timing" cell (`6936`), `Method | Status` mashed into one cell (`6938`).
- Deadlines/Tasks: reuse the workspace tables including per-row *Delete* and bulk bars — destructive actions of debatable value in a global scan view.
- Discovery: no inline status control (unlike the workspace) — same entity, different affordances (`consistency`, severity 2).
- Hearings: 5 columns, no urgency signal.
- "All" filter stacks all five full tables vertically — a scroll wall with five sticky-less headers (`data-density`, severity 3).

Rebuild: one unified queue table (Type · Case · Item · Due · Status · Action) with type/urgency facets and consistent inline actions; per-type extra columns appear only when a single type is selected.

### 2.4 Case editor modal (`App.tsx:5996-6113`)

~40 fields in one flat `form-grid` inside a modal — identity, counsel, dates, deposit, acreage, tax, service, notes all interleaved. Sectioned form (or drawer with grouped sections mirroring the Details groupings that already exist at `4352-4379`) required. Validation exists (good: `validateCaseDraft:1235`, inline field errors `6001`).

---

## 3. Parallel implementations (all die → one canonical each)

| Pattern | Current implementations (file:line) | Canonical replacement |
|---|---|---|
| Buttons | 9 styles: default, `.primary`, `.link-button`, `.compact-action-button`, `.row-icon-button` (`index.css:1260`), `a.button-like` (`index.css:117`), `.chip`, `.segment`, `.danger-button` | `Button` (variant: primary/secondary/ghost/danger/link, size: sm/md) + `ToggleChip` |
| Tables | 3 CSS variants (`table`, `.compact-table`, `.dense-table`) + 2 near-duplicate renderers (`renderDeadlineTable:5531`, `renderChecklistTable:5650`) + card-lists standing in for tables (hearings `4779`, action queue) | `DataTable` (density prop, sticky header, tabular-nums, empty-state slot, selection model) |
| Inline status control | pill-select (`4690`, `5852`), plain `.inline-edit-select` (`5610, 5627, 5741`), quick-change select (`4864`), card select (`ActionQueueItemCard.tsx:191`) | `StatusSelect` (tone-mapped, timestamped) |
| Confirmation | 27× `window.confirm` (e.g. `2375, 2387, 2519, 2685`) + 1 custom inline confirm (`4645-4653`) | `ConfirmAction` (inline two-step for row actions; dialog for bulk/destructive) |
| Empty states | 5 patterns: `EmptyState` component, `.compact-empty-state` (`5330, 5350`), `.upcoming-work-empty` (`6640`), bare `<p>` (`4509`), table colSpan rows (`4316, 5572`) | `EmptyState` (title/hint/action, table-row mode) |
| Overlays | `ModalShell:7666`, filter slideover (`6610-6626`), `PipelineHandoffDialog`, `RecordDecisionDialog`, bulk-date popover (`5778`), search dropdown (`5942`), TriageWizard overlay | `Modal`, `Drawer`, `Popover` (3 primitives, all others compose) |
| Collapse/disclosure | `CollapsiblePanel:7657`, discovery accordion (`5813-5884`), Case Insight tabs (`6728`), summary-card toggle filters (`6592`), docket metric toggles (`6786`), "Show All Fields" (`5473`) | `Disclosure` + `Tabs` + `FilterTile` |
| Metric tiles | `.metric-tile` (workspace `4417`, risk `5281`, reports `6858`), `.dashboard-summary-card`, `.docket-metric` (`6786`) | `MetricTile` (interactive/static variants) |
| Inline quick forms | 7 forms in `ActionQueueItemCard:99-212`, activity edit (`4556`), bulk defer (`6685`) | `InlineForm` wrapper (label/field/submit grid) |
| Date rendering | `displayDate:1068` (raw ISO passthrough), `displayDateTime:1094` (locale-dependent), `.slice(0,19).replace('T',' ')` (`5104`), `.slice(0,10)` (`5131, 5329`), raw `item.reviewDate` (`ActionQueueItemCard.tsx:81`) | `formatDate` / `formatDateTime` (single, unambiguous, see §5.3) |

---

## 4. Interaction cost of frequent tasks (current → target)

| Task | Current | Target after redesign |
|---|---|---|
| Mark task done (from dashboard) | 1 click (Upcoming Work "Mark Done") — but only if visible under current filter | 1 click, from the single unified queue |
| Mark task done (in case) | 1 click (✓ or checkbox) | unchanged — this is already right |
| Change discovery status (in case) | 2 clicks (open accordion group → select) | 2 clicks; group remembers open state |
| Change discovery status (global queue) | impossible inline — "Record Response" or open case (3+ clicks) | 2 clicks (same `StatusSelect` as workspace) |
| Add event | 2 clicks + modal (Events tab → Add Event) | 2 clicks; no regression, lighter form |
| Log service attempt | 2 clicks + 5-field form | unchanged structurally |
| Find the urgent case | scroll past 14 four-line rows; cognitively: 35/44 flagged | top row of one queue, one priority model |
| Defer a case for 30 days | 3 clicks (card → Plan next step → Revisit in 30 days) | 2 clicks |
| Edit one case field (e.g. opposing counsel) | 2 clicks + find field in 40-field modal + save | 2 clicks + sectioned form section |

---

## 5. App-wide craft & accessibility baseline

### 5.1 Visual language (rule: `design-tokens`, `type-scale`)
- **No type scale**: 20+ distinct font sizes in `index.css` (0.75/.78/.8/.82/.85/.86/.88/.9/.92/.95/1/1.15/1.2/1.25/1.3/1.4rem + `11px` + clamp), several written both with and without leading zero. Violates 6–8-style limit.
- **15 distinct border radii** (0.35rem→24px). No radius tokens.
- **Font**: `"Segoe UI", Tahoma, Geneva, Verdana` (`index.css:2`) — Windows-default stack; part of the "grew, wasn't designed" look. One `"Courier New"` outlier (`index.css:1705`).
- Spacing is ad-hoc rem values + utility classes (`top-gap`, `top-gap-small`) — no scale.
- Dark mode exists and is token-based (`index.css:43-77`) — **keep**; a documented contrast fix at `index.css:15-17` shows care already.

### 5.2 Numerals (rule: `number-tabular`)
`font-variant-numeric: tabular-nums` appears exactly once (`index.css:1222`, risk ledger). Missing from: case list, all five queue tables, reports preview, metric tiles, summary cards, currency cells everywhere.

### 5.3 Dates (rule: `date-format` — legal work, must be unambiguous)
- `displayDate` (`App.tsx:1068`) returns the raw stored string — renders ISO `2025-09-14` when set, `'Not set'` otherwise. Unambiguous but machine-flavored; inconsistent with…
- `displayDateTime` (`App.tsx:1094`) → `toLocaleString()` — **machine-locale-dependent**, can render `7/18/2026` (ambiguous day/month to a reader who doesn't know the locale). Severity 3 for legal records.
- Plus 3 ad-hoc slice formats (§3 table). One formatter pair, one format (proposal: `Jul 18, 2026` / `Jul 18, 2026 4:32 PM`), everywhere.

### 5.4 Hit targets (rule: `touch-target-size`)
- `.row-icon-button` 32×32px (`index.css:1260-1264`) — under 44px guideline; acceptable for mouse-first desktop but standardize at ≥36px with padding-extended hit area.
- Checkbox cells in dense tables are bare checkboxes (~16px) — extend row-level hit area.

### 5.5 Keyboard & focus
- Focus-visible outline is global and real (`index.css:134-140`) — **good baseline, keep**.
- `aria-label`s consistently present on icon buttons and row controls — good.
- **Clickable rows** (`.clickable-row` `onClick` on `<tr>`, e.g. `App.tsx:4318`) are not keyboard-reachable; inner link-button saves it on case list but not on command-list rows (`4512` — checkbox is reachable, row-open is not). Rule: `keyboard-nav`, severity 2.
- No keyboard shortcuts, no command palette, for an all-day tool (H7). Phase 2 will propose scope.
- Modal close is blocked when dirty with only a status-message explanation (`5982-5987`) — no visible dirty indicator; minor (H1).

### 5.6 Copy & language (H2)
- Dev jargon in user-facing copy: "Unified Document Platform (Preview)", "Rebuilt generation pipeline (build-plan steps 4-6)" (`App.tsx:4991-4992`), "formula version risk-v1" (`5132`). *Pipeline itself untouched — flagged as copy.*
- Hero panels on all 5 pages spend 3 lines re-explaining the page to a daily user (`4239-4249`, `6550-6556`, `6834-6837`, `6876-6882`, `7030-7036`) (H8).
- Arkansas condemnation terminology is otherwise used correctly (just compensation, order of possession context, Ark. Code Ann. § 27-67-316(e) citation at `5299`).

### 5.7 Consistency misc
- Case list: three filters apply instantly, Search requires "Apply Filters" — the helper text documents the inconsistency instead of fixing it (`4291`) (H4, severity 2).
- Generated Documents table renders raw `createdAt.slice(0,19)` timestamps (`5104`).
- `attentionLabels`, priority labels, queue type labels — three vocabularies for urgency across dashboard/queues.

---

## 6. Functionality inventory safeguard

Every control catalogued in this audit (all buttons, inline selects, bulk bars, forms, exports, toggles per screen) is preserved in the Phase 2 proposals; the per-screen regression checklists in Phase 3 will enumerate them explicitly. Two items are *relocations requiring sign-off*: the Details tab (content relocates, tab dies) and per-row Delete in global queue tables (proposed workspace-only). No removals without explicit approval.

---

## 7. Clarifying questions (batched, per mandate)

1. **Attention model consolidation.** The dashboard rebuild wants one priority model (the Action Queue's) absorbing the attention-flag list and Upcoming Work. That means the "35 of 44 need attention" framing and the standalone urgent panel disappear into facets of one queue. Confirm this IA direction before I mock it.
2. **Rule calibration.** Most "Urgent" flags are one rule ("Service status reminder (31 days after filing)"). Is recalibrating *when* that rule fires in scope (server-side change), or is the redesign display-layer only?
3. **Documents tab copy.** The pipeline is frozen, but its panel copy ("Unified Document Platform (Preview)", "build-plan steps 4-6") is user-facing UI. May I retitle/rewrite copy without touching generation logic?
4. **Tab consolidation.** OK to *propose* (with mockups): merge Deadlines + Tasks (+ Events?) into one Work tab; kill the read-only Details tab and relocate its content? Vetoable per-screen in Phase 2.
5. **Date format.** `Jul 18, 2026` everywhere (my recommendation) or keep ISO `2026-07-18`?
6. **Keyboard investment.** Command palette + shortcut overlay: in scope for Phase 3, or over-engineering for this user base?

---

*Phase 2 (design-system candidates + mockups) follows on your answers to #1 and #4; the rest can proceed on my recommendations if you don't object.*
