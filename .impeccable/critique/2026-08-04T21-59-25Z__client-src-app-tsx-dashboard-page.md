---
target: the dashboard (Attorney Dashboard, client/src/App.tsx page==="dashboard")
total_score: 23
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 3
timestamp: 2026-08-04T21-59-25Z
slug: client-src-app-tsx-dashboard-page
---
Method: dual-agent (A: design-review sub-agent · B: detector-and-browser-evidence sub-agent)

## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 2 | Async actions like "Mark complete" don't show a busy/disabled state, while sibling "Resolve reminder" does |
| 2 | Match System / Real World | 3 | Domain vocabulary is solid, but "Jury trials," "Trials," and "Trial-track matters" read as three overlapping concepts on one screen |
| 3 | User Control and Freedom | 2 | Filters slideover has no Escape-to-close, no `aria-modal`, no focus trap |
| 4 | Consistency and Standards | 1 | Two coexisting visual languages on one page (new `ui-*` primitives vs. legacy `.pill`/raw cards) |
| 5 | Error Prevention | 3 | Explicit save/cancel on inline forms; double-submit guarding is inconsistent |
| 6 | Recognition Rather Than Recall | 3 | "Why it's here" queue copy is strong; Case Insight's 6 tabs carry no counts/badges |
| 7 | Flexibility and Efficiency | 2 | Global ⌘K palette exists but isn't surfaced on this page; only one bulk action ("Defer selected") |
| 8 | Aesthetic and Minimalist Design | 3 | Density-8 mostly earns it; undercut by the legacy/redesigned split |
| 9 | Error Recovery | 3 | `ErrorState` gives message + Retry, functional but generic |
| 10 | Help and Documentation | 1 | No help affordance anywhere on this page |
| **Total** | | **23/40** | **Acceptable** |

## Design Specificity Verdict

**LLM assessment**: Partially grounded, not fully. Every color, radius, and spacing value traces exactly to DESIGN.md's tokens, and copy ("Filing Pipeline," "Discovery," "Handoff") is genuinely domain-specific rather than generic SaaS-speak. But the page is visibly two design systems stitched together: the Action Queue, KPI strip, and Overdue/Due tables use the new `ui-*` primitives (dot+text `StatusChip`, 4px-radius `Btn`, `TypeChip`); four of the six Case Insight tabs (`MomentumReviewPanel`, `FilingPipelinePanel`, `TrialWatchTable`, `ProjectWatchRowCard`) instead render raw `<article>` cards and legacy fully-rounded `.pill` badges with no dot — a pattern the codebase's own CSS comments flag as "legacy... until later screens migrate." Scrolling from the queue into Case Insight, the visual language visibly changes.

**Deterministic scan**: The bundled detector ran clean (exit 0, zero findings) across all 16 dashboard-related component files, verified against a positive control to confirm it wasn't silently no-op'ing. The live-page overlay (injected into the running app, not a static file scan) told a different story: **16 anti-pattern findings, every one a font-size violation** — 12 instances of text rendering at 9.36–10.66px and 3 more at 10.14–11.96px, all below DESIGN.md's own documented type floor (`--text-xs` = 11px, "nothing else" per the type scale). Three of these were independently re-verified via direct `getComputedStyle` calls rather than trusting the overlay alone. This is a case of the detector catching something the design review's manual pass missed: the affected text is exactly the KPI-tile hint lines and Case Insight tab labels the design review separately flagged as under-emphasized — the overlay quantifies that they're not just visually de-prioritized, they're rendering under the system's own legibility floor.

**Visual overlay status**: Injection succeeded and captured real findings, but the live-server helper was stopped afterward per the critique process's cleanup step — there is no overlay currently visible in a browser tab for you to look at. Re-run `/impeccable live` on this page if you want to see the highlighted findings interactively.

## Overall Impression

The bones are right and the token discipline is real — this isn't a "close enough" implementation of DESIGN.md, it's the actual system, verified computationally. The single biggest problem is that the Action Queue — the page's entire reason for existing — silently filters out half its own priority levels by default, with no meaningful visual signal, on every single load. Close behind that: the best, most consolidated part of the page (Case Insight) has been placed where almost nobody will scroll to see it.

## What's Working

1. **Token discipline is real, not approximate.** Every color, radius, and spacing value checked against DESIGN.md matched exactly, including computed contrast ratios for all four semantic colors. This is a system living up to its own spec, which is rarer than it sounds.
2. **The Action Queue's "Why it's here" / "Recommended" copy is genuinely good UX writing.** It answers the attorney's first question — why am I looking at this? — before they ask, in domain-specific language ("Recommended: Resolve the missed deadline immediately").
3. **Case Insight's tab consolidation is the right instinct.** Folding seven formerly always-visible side panels into one tabbed context view is a real progressive-disclosure win. It's just been placed where the grid buries it (see Priority Issues).

## Priority Issues

**[P0] Action Queue silently hides half its priority levels by default.**
*Why it matters*: `activeQueueTiles` initializes to only levels 1 and 2 — "Review status" and "Planned work" items never appear on first load. The only signal is a small muted footnote below the table, and the empty-state copy ("Nothing needs attorney judgment right now") doesn't distinguish "filtered" from "actually empty." For deadline-tracking software, a queue that can silently read as "all clear" when it isn't is the worst kind of false reassurance.
*Fix*: Default to all four priority levels active, or make the filtered state visually loud (a persistent chip row above the table, not a footnote below it) and change empty-state copy to "0 of N shown" when filters are active.
*Suggested command*: `/impeccable clarify` (copy/signal) or `/impeccable harden` (default-state correctness)

**[P1] Case Insight — the highest-density, most-consolidated panel on the page — renders below the fold.**
*Why it matters*: Measured live at 1280×720: it sits at `top=808px` in a 401px-wide column, because the CSS grid's named areas pair it with the least urgent table on the page ("Due in the next 7 days") rather than with the Action Queue the JSX visually implies it sits beside. The panel built specifically to consolidate seven other panels gets the worst real estate on the page.
*Fix*: Give `insight` its own full-width row directly under the Action Queue, or reorder the grid areas so it appears above Overdue/Planning.
*Suggested command*: `/impeccable layout`

**[P1] Focus ring explicitly removed on a live interactive control.**
*Why it matters*: `.dashboard-due-date-link:focus-visible { outline: none; }` governs "Change due date," used in both the Overdue and Due-this-week tables on this exact page. DESIGN.md states the focus ring is "never removed, never replaced with an outline-color-only treatment" — this control does both.
*Fix*: Restore `outline` on `:focus-visible`; keep the hover-only border/color treatment for mouse users.
*Suggested command*: `/impeccable audit` (or direct fix — this is a one-line CSS change)

**[P1] Widespread text renders below the system's own 11px legibility floor.**
*Why it matters*: The live-page detector found 15 instances of text between 9.36px and 11.96px — KPI-tile hint lines, all six Case Insight tab labels, and the jury-trial summary card — all under DESIGN.md's documented `--text-xs` floor (11px, "nothing else" per the 7-step type scale). This isn't a stylistic choice; it's smaller than the system's own smallest defined size, likely from compounding relative (em/%) sizing somewhere in the cascade.
*Fix*: Audit the cascade producing these sizes (likely nested relative units multiplying down) and clamp every text node to `--text-xs` (11px) as an actual floor, not an aspiration.
*Suggested command*: `/impeccable audit` or `/impeccable typeset`

**[P2] Two coexisting visual languages on one page.**
*Why it matters*: `StatusChip`/`Btn`/`TypeChip` (dot+text, 4px radius) run the Action Queue and tables; `MomentumReviewPanel`, `FilingPipelinePanel`, `TrialWatchTable`, and `ProjectWatchRowCard` instead render legacy `.pill` badges (999px radius, no dot, color-only) and raw `<article>` cards with inline buttons. The codebase's own CSS comments already flag `.pill` as legacy pending migration.
*Fix*: Port these four sub-components to `StatusChip`/`Btn` — this page is the natural place to finish that migration.
*Suggested command*: `/impeccable polish`

## Persona Red Flags

**Alex (Power User)**:
- Can't triage-and-act with confidence in under 60 seconds, because the fastest path (the default queue view) may already be silently missing half the priority levels (P0 above).
- No bulk action beyond "Defer selected…" — no bulk complete, no bulk reassignment — despite a "Select all" checkbox that implies more bulk power exists than actually does.
- The app-bar's ⌘K command palette is a real win, but nothing on the dashboard itself hints it's there.
- An expanded queue row can surface 6-7 action buttons at once — more scanning than an expert wants for routine triage.

**Sam (Accessibility-Dependent User)**:
- Loses the standard focus ring entirely on "Change due date" — the one control most relevant to deadline management on this page (P1 above).
- The Filters slideover has no `aria-modal`, no focus trap, and no Escape-to-close; a keyboard/screen-reader user can tab past it into the backdrop-obscured page behind it.
- The 15 instances of sub-11px text (P1 above) hit low-vision and screen-magnification users hardest — this is the same finding, seen through a different persona's failure mode.
- `.pipeline-watch-card` / `.trial-watch-card` are clickable `<article>` elements with `tabIndex="-1"` — not keyboard-reachable themselves (mitigated by duplicate buttons inside, but the visual affordance oversells what keyboard/AT users actually get).
- The Due-in-7-days table colors overdue dates red with no icon or label distinguishing them from not-yet-due rows in the same table — a direct color-only-meaning case DESIGN.md's own "Dot-Plus-Text Rule" exists to prevent.

## Minor Observations

- `ui-typechip-discovery` uses a purple (`#5B3E96`/`#EFE9F9`) that appears nowhere in DESIGN.md's documented palette — a sixth, undocumented color.
- The Docket tab's KV rows color only the numeric value by tone, with no dot — inconsistent with `StatusChip`'s pattern used two panels over.
- `amber-clay` on its background measures 4.51:1 contrast — technically clears AA (4.5:1) but by a hair; worth a slightly deeper shade before this ships wider.
- "Mark complete" doesn't disable during its request while the sibling "Resolve reminder" button does — inconsistent double-submit guarding between functionally similar actions.
- A `401 Unauthorized` on `/api/auth/me` appears in console/network logs — expected behavior in the app's unauthenticated SQLite preview mode, not a design defect, flagged only for completeness.
- The "Trials" tab can read "No trial-track cases" while the KPI strip and "Next jury trial" card both reference a real, dated jury trial for the same dataset — likely a legitimate distinction between "jury trial" and "trial track" as separate fields, but it reads as a contradiction without an in-product explanation.

## Questions to Consider

1. If the Action Queue's default view already hides two of four priority levels, what is actually being measured when the page's own headline says "1 need action now" — computed before or after that hidden filter?
2. Case Insight was built specifically to make "the queue plus exactly one context view at a time" the whole page — given where the grid actually places it, was that layout ever checked at a real viewport size, or only reviewed in the JSX?
3. Is "Trial Track" a superset, subset, or entirely separate concept from "Jury Trial" — and if attorneys need to hold that distinction in their head across three differently-worded UI elements, is that complexity earning its keep?
