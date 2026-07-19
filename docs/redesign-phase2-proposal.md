# UI/UX Redesign — Phase 2 Proposal

Date: 2026-07-18 · Status: **awaiting approval — nothing ships until sign-off**
Companion artifacts: `design-system/MASTER.md` (token/system source of truth) · `docs/redesign-mockups.html` (5 navigable screens, light/dark) · `docs/redesign-phase1-audit.md` (findings)

Each numbered proposal below is independently acceptable — veto any one without stalling the rest.

---

## 1. Design system: "Docket" (winner) vs "Brief" (runner-up)

**Winner — Docket (engineered civic).** IBM Plex Sans UI + IBM Plex Mono for every number, date, case number, and currency value; cool blue-biased neutrals; one confident docket blue `#14538F`; Arkansas crimson as a brand tick only; square 4/8px radii; borders over shadows; 32px table rows; 7-step type scale. Full spec in `design-system/MASTER.md`.

Why bet on it: this is an operations console — the identity has to come from *precision*, not atmosphere. Plex is an engineered public-works face (credible for a DOT agency), its mono sibling makes docket numbers and dates read as *records*, and the disciplined neutral system keeps 61-row tables scannable. It is confident without being trendy: no gradients, no glassmorphism, no rounded-2xl cards.

**Runner-up — Brief (legal paper).** Source Serif 4 display + Source Sans 3, warm paper neutrals, oxblood accent, brief-caption double rules. Unmistakably "law," gorgeous in print — but serif display + warm ground costs contrast in dense tables, fights dark mode, and oxblood collides with danger semantics in a deadline-heavy UI. Documented in MASTER.md §10; worth revisiting for exported/printed reports.

## 2. Per-screen proposals (see mockups)

### 2.1 Dashboard — REBUILD (mockup: "Dashboard")
- One priority model (the Action Queue's 4 levels). The old urgent/attention panel and Upcoming Work **die as siblings** and return as: clickable metric-tile facets (Immediate / Attorney decision / Momentum / Planned / Triage) filtering **one queue table**, plus a "Due in the next 7 days" item table below.
- Queue rows: 1 line + sub-line, priority stripe, contextual quick actions, expandable inline form (mocked on the Kraft Farm row) replacing the 7-form card.
- Case Insight survives as the right rail (Docket/Discovery/Pipeline/Trials/Projects mini-tabs, metric list drills in place).
- **Server-side (approved answer #2):** retire/recalibrate the "Service status reminder (31 days after filing)" attention rule that currently flags 80% of the docket; service urgency derives from the 120-day deadline window instead.

### 2.2 Case List — RESKIN (mockup: "Case List")
Single filter bar, everything live (search included — "Apply Filters" dies); status chips with triage count; dense sticky-header table; mono dates/numbers; status dot-chips; row count in bar.

### 2.3 Case Workspace — tab REBUILD + reskin (mockup: "Case Workspace")
- **8 tabs:** Overview · **Work** (Deadlines + Tasks + Events merged; type chips, phase grouping with progress, same inline controls and bulk bar) · Discovery · Service & Publication · Valuation & Risk (current Risk Analysis tab, clearer name) · Trial (Trial Notebook) · Documents · Notes.
- **Details tab dies** (approved answer #4): read-only record becomes a proper "Case record" section on Overview (definition list, not fake inputs); Danger-zone delete moves to the case ⋯ menu; Edit opens the new sectioned editor.
- Header: name + mono meta line + status chip + issue-tag chips + key-date tile strip (adds Service deadline when unperfected).
- Case editor modal → **SectionedForm** drawer (Identity / People / Dates / Financial & Property / Service / Notes) — every current field preserved.
- Documents tab: dev copy removed (approved answer #3) — "Generate a Document" + "Generated Documents"; generation logic untouched.

### 2.4 Work Queue — REBUILD (mockup: "Work Queue")
One unified table (Type · Item · Case · Due · Status · contextual action) with type-facet chips + urgency + search + sort. Same StatusSelect as the workspace (fixes discovery inconsistency). Single-type facets reveal type-specific columns (Service: basis date, method, perfected). Bulk bar appears for Deadlines/Tasks facets. Per-row Delete leaves the global queue (stays in workspace) — flagged relocation, sign-off item.

### 2.5 Reports — RESKIN (mockup: "Reports")
Left rail (Filters + Columns summary), right: metric tiles, open-case-age bar chart (direct-labeled), preview table, exports top-right.

### 2.6 Not mocked (governed by MASTER.md directly)
Settings (KEEP/RESKIN per audit), Triage Wizard (KEEP), small modals (RESKIN), Discovery/Service/Trial/Notes/Documents tab bodies (RESKIN in place).

## 3. Canonical component library (contracts)

| Component | Contract (one line) |
|---|---|
| `Button` | variant primary/secondary/ghost/danger/link · size md/sm · replaces all 9 button styles |
| `DataTable` | sticky header, 32px rows, tabular-nums cells, selection, row-expansion, empty-state slot |
| `StatusChip` | dot + label; tone resolved from one status→tone map shared app-wide |
| `StatusSelect` | chip-styled inline `<select>`; persists on change and stamps `updatedAt` |
| `MetricTile` | label + data-font value; static, or toggle-filter with `aria-pressed` |
| `FilterBar` | live search + chip facets + selects + result count + clear; the only filter layout |
| `Panel` / `Disclosure` | flat bordered panel; collapsible variant with persisted open state |
| `Modal` / `Drawer` / `Popover` | the only three overlays; focus-trapped, Esc-closes, focus-returns |
| `ConfirmAction` | inline two-step confirm for row actions; dialog for bulk/destructive; kills 27 `window.confirm` |
| `EmptyState` | title + hint + optional action; table-row mode; kills 5 patterns |
| `InlineForm` | compact label/field/submit row for in-context quick actions |
| `SectionedForm` | grouped sections + section nav; case editor and any future long form |
| `CommandPalette` | Ctrl+K actions/navigation/case search + `?` shortcut overlay (approved answer #6) |
| `AppBar` / `NavTabs` | 48px brand bar + horizontal primary nav (top nav retained — tables need width) |
| `DateText` / formatters | `July 18, 2026` · `July 18, 2026, 4:32 PM CT` (America/Chicago always) — approved answer #5; only path for dates to reach the DOM |

## 4. Navigation / IA
- App-level nav unchanged (Dashboard · Cases · Work Queues · Reports · Settings) — the muscle memory is fine; the pages behind it change.
- Workspace tabs 11 → 8 as above.
- Command palette adds keyboard-first navigation across all of it.

## 5. Migration order & coexistence

| Step | Scope | Coexistence story |
|---|---|---|
| 0 | Tokens + fonts + formatters: new CSS custom properties over the existing names, @fontsource, `formatDate`/`formatDateTime` swapped in-place | Whole app shifts to new palette/type at once — old layouts, new skin. This is deliberate: mid-rebuild screens share one visual language, so old-vs-new never jars. |
| 1 | Canonical components built alongside old CSS (no screen rewrites yet) | Invisible to users |
| 2 | **Work Queue** rebuild (smallest REBUILD, proves DataTable/FilterBar/StatusSelect) | Old queue deleted same commit |
| 3 | **Dashboard** rebuild + server-side attention-rule recalibration | Old panels deleted same commit |
| 4 | **Workspace tab consolidation** (Work tab, Details death, sectioned editor) + per-tab reskins | Tabs convert in one commit to avoid a mixed tab bar |
| 5 | Case List + Reports reskins | Per-screen commits |
| 6 | Settings reskins + KEEP-screen token sanity pass | Per-section commits |
| 7 | Command palette + shortcut overlay | Additive |
| 8 | Sweep: delete dead CSS, verify 0 `window.confirm`, run per-screen functionality regression checklists | Final |

Each step: batch commit per screen, pre-delivery checklist (§9 of MASTER.md + heuristic pass), explicit control-by-control regression list against the old screen.

## 6. Sign-off items (removals/relocations needing explicit approval)
1. Details tab dies — record moves to Overview section + editor (content fully preserved).
2. Per-row **Delete** absent from global Work Queue tables (still in workspace).
3. Dashboard's standalone "Cases Flagged Urgent / Attention" panel and "Upcoming Work" panel replaced by queue facets + "Due in 7 days" table.
4. "31 days after filing" service reminder rule retired server-side (replaced by 120-day-window urgency).
5. Hero panels with descriptive paragraphs removed from all five pages (titles + counts remain).

---

**Review the mockups (`docs/redesign-mockups.html` — open in any browser, toggle dark mode), veto what you don't like, approve the rest. Phase 3 starts only on your word.**
