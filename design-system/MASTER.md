# ARDOT Condemnation Dashboard — Design System (Source of Truth)

Project: ARDOT Case Planner · Density: 8/10 (compact, data-dense) · Platform: desktop-first web
Status: **Proposed (Phase 2)** — becomes binding on approval. Every future session styles against this file.
Page-specific deviations: `design-system/pages/<page>.md` (none yet).

---

## 1. Identity: "Docket" — engineered civic

A state-agency litigation tool should feel like a precisely engineered public document: calm, exact,
authoritative, zero decoration. The visual voice comes from typography (an engineered grotesque +
a monospaced data face), a disciplined cool-neutral surface system, and one confident blue. Arkansas
crimson appears exactly once, as the brand tick in the app bar — it is never a semantic color.

Chosen over runner-up "Brief" (legal-paper: Source Serif display, warm neutrals, oxblood accent)
because this app is an operations console, not a reading surface: attorneys scan tables all day,
and the serif/warm-paper direction softens data contrast, complicates dark mode, and trades
scanability for atmosphere. See §10 for the full runner-up spec and reasoning.

## 2. Typography

| Token | Value |
|---|---|
| `--font-ui` | `"IBM Plex Sans", "Segoe UI", system-ui, sans-serif` |
| `--font-data` | `"IBM Plex Mono", Consolas, monospace` |

Bundle via `@fontsource/ibm-plex-sans` (400/500/600) and `@fontsource/ibm-plex-mono` (400/500) — no CDN.

**Scale (px)** — 7 steps, nothing else:

| Token | Size/line | Use |
|---|---|---|
| `--text-xs` | 11/16 | table headers (uppercase +0.06em), fine print |
| `--text-sm` | 12/16 | secondary cell text, meta, chips |
| `--text-base` | 13/20 | body, table cells, controls — the app's default |
| `--text-md` | 14/20 | emphasized body, form inputs |
| `--text-lg` | 15/22 | panel titles |
| `--text-xl` | 18/24 | page titles |
| `--text-2xl` | 22/28 | the single dashboard headline |

Weights: 400 body · 500 emphasis/labels · 600 titles/values. No 700.
Rules: `--font-data` + `font-variant-numeric: tabular-nums` on **every** numeric/date/case-number/currency
table cell and metric value. Uppercase only at `--text-xs` with letter-spacing. No italics in UI chrome.

## 3. Color

### Light (default)

| Token | Hex | Role |
|---|---|---|
| `--bg` | `#F3F6F9` | app background (cool-biased, chosen not default) |
| `--surface` | `#FFFFFF` | panels, tables, cards |
| `--surface-sunken` | `#EAEFF4` | wells, table stripe, input backgrounds in filters |
| `--border` | `#D8E0E9` | hairlines, table row rules |
| `--border-strong` | `#B9C6D4` | control borders |
| `--text` | `#16232F` | primary text |
| `--text-muted` | `#5A6B7D` | secondary (4.5:1+ on surface) |
| `--text-faint` | `#8595A6` | tertiary/meta, large sizes only |
| `--primary` | `#14538F` | actions, links, active states ("docket blue") |
| `--primary-hover` | `#0E3F6E` | |
| `--primary-soft` | `#E3EDF7` | selected rows, active chips |
| `--brand-crimson` | `#9E1B32` | app-bar brand tick ONLY — never semantic |
| `--ok` / `--ok-bg` | `#1E7A44` / `#E7F4EC` | served, done, complete |
| `--warn` / `--warn-bg` | `#A85B00` / `#FBF1E2` | due soon, waiting, draft |
| `--danger` / `--danger-bg` | `#B3261E` / `#FBEAE8` | overdue, failed, destructive |
| `--focus` | `#4C8FD6` | 2px outline, 2px offset — global, never removed |
| `--overlay` | `rgba(13,22,32,.55)` | modal scrim |

### Dark (`:root[data-theme='dark']`, plus `prefers-color-scheme` default)

`--bg #0F161D · --surface #16202B · --surface-sunken #111A23 · --border #263442 · --border-strong #35485C ·
--text #E6EDF4 · --text-muted #9DAFC0 · --text-faint #6E8093 · --primary #6FA8DC · --primary-hover #8FBCE6 ·
--primary-soft #1B324A · --ok #5FBF8A/#16311F · --warn #E0A458/#33270F · --danger #E58278/#3A1B17 ·
--focus #7FB4E8 · --brand-crimson #C24B60`. Elevation in dark = lighter surface, not shadow.

Rules: color never carries meaning alone — status = dot + text; charts get direct labels.
Contrast: AA (4.5:1 text, 3:1 UI) verified per pairing before merge.

## 4. Space, shape, elevation

- **Spacing scale (px):** 2, 4, 8, 12, 16, 24, 32. Component padding 8–12; between related 8; between groups 16–24; page gutter 24. Layout via flex/grid `gap` only.
- **Radius:** `--r-ctl: 4px` (controls, chips, cells) · `--r-panel: 8px` (panels, modals) · `--r-full: 999px` (avatar-ish only). Nothing else — the current 15 radii die.
- **Elevation:** borders do the work. One shadow token `--shadow-overlay: 0 8px 24px rgba(15,25,40,.18)` for modals/popovers/drawers only. Panels are flat with 1px border.
- **App width:** `min(1600px, 100vw - 32px)` — wider than today's 1440 (tables are the product).

## 5. Density standards (density 8)

| Element | Spec |
|---|---|
| Table row | 32px; 8px×10px cell padding; sticky header; `--surface-sunken` stripe optional per table |
| Table header | 26px, `--text-xs` uppercase, `--text-muted` |
| Control (input/select/button) | 30px default; 26px inline-in-table |
| Button | 30px, 12px side padding; icon-button 30×30 (hit area ≥36 via margin) |
| Status chip | 20px, dot 7px, `--text-sm`, radius `--r-ctl` |
| Metric tile | 64px: label `--text-xs` upper, value `--text-xl` data-font |
| Panel padding | 12px; panel title row 36px |
| Page vertical rhythm | 16px between panels |

## 6. Motion

120ms ease-out for state changes (hover, expand, tab switch); 160ms for overlays entering.
No decorative animation. `prefers-reduced-motion: reduce` disables all transitions.

## 7. Language & formatting

- **Dates:** `July 18, 2026` everywhere user-facing (full month, no ordinal). Table variant may use data font.
- **Date-times:** `July 18, 2026, 4:32 PM CT` — **always America/Chicago**, always suffixed `CT`. One formatter pair (`formatDate`, `formatDateTime`) — the four current ad-hoc formats die.
- **Currency:** `$1,234.56`, data font, tabular, right-aligned.
- **Empty values:** `—` (em dash), `--text-faint`. Never the string "Not set" repeated 40 times per screen.
- Terminology: Arkansas condemnation vocabulary (complaint in condemnation, order of possession, just compensation, deposit, tract). No internal jargon ("platform", "build-plan", version tags) in user-facing copy.
- Buttons say what happens: "File deadline", "Mark served". Errors: what happened + how to fix.

## 8. Component canon (each kills its parallel implementations)

Contracts are one-liners here; props finalized in Phase 3 alongside code.

1. **`Button`** — variant `primary|secondary|ghost|danger|link`, size `md|sm`; replaces 9 button styles.
2. **`DataTable`** — sticky header, density, tabular-nums numeric cells, selection model, empty-state slot, row expansion; replaces 3 table styles + 2 duplicate renderers + card-lists-as-tables.
3. **`StatusChip`** — dot + label, tone from a single status→tone map; replaces pill zoo.
4. **`StatusSelect`** — inline editable chip-styled select, persists + timestamps; replaces 5 inline-select variants.
5. **`MetricTile`** — static or toggle-filter; replaces metric-tile / dashboard-summary-card / docket-metric.
6. **`FilterBar`** — search (live), chip facets, selects, active-filter summary + clear; replaces 4 filter layouts.
7. **`Panel` / `Disclosure`** — flat panel; collapsible variant; replaces CollapsiblePanel + 3 ad-hoc collapse patterns.
8. **`Modal` / `Drawer` / `Popover`** — the only 3 overlay primitives; replace 7 overlay implementations.
9. **`ConfirmAction`** — inline two-step confirm (rows) or dialog (bulk/destructive); replaces 27 `window.confirm`.
10. **`EmptyState`** — title/hint/action + table-row mode; replaces 5 patterns.
11. **`InlineForm`** — compact label/field/submit grid for in-context quick actions.
12. **`SectionedForm`** — grouped form sections with sticky section nav; for the case editor.
13. **`CommandPalette`** — Ctrl+K: actions, navigation, case search; plus shortcut overlay (`?`).
14. **`AppBar` / `NavTabs`** — 48px bar (brand tick, wordmark, global search, ⌘K hint) + horizontal primary nav.
15. **`DateText`** — renders `formatDate`/`formatDateTime`; the only way dates reach the DOM.

## 9. Accessibility floor (blocking, per screen)

Focus-visible on everything interactive · full keyboard paths for every mouse path (incl. row-open) ·
`aria-label` on icon buttons · AA contrast both themes · no color-only meaning · hit areas ≥36px ·
`role="status"` on async feedback · Escape closes overlays, focus returns to invoker.

## 10. Runner-up (documented for the record): "Brief" — legal paper

Source Serif 4 (display) + Source Sans 3 (UI); warm neutrals (`#F6F4F0` bg, `#221E1A` ink);
oxblood `#7A2E2E` accent; radius 6/10; hairline double-rules under panel titles (brief caption style).
Strengths: unmistakable legal identity, beautiful in reports/print. Rejected because: (a) the daily
surface is dense tables where warm low-contrast grounds and serif display cost scanability;
(b) paper metaphor fights an already-good dark mode; (c) oxblood collides perceptually with danger
semantics in a deadline-heavy UI. Revisit its serif voice for **printed/exported reports** later.

## 11. Explored and rejected: "Control" — ops console

Slate + signal-orange, mono-forward. Rejected: reads as SRE tooling, not counsel; orange accent
collides with `--warn` in an app where "due soon" is the most common state on screen.
