# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Three co-equal roles within ARDOT's Legal Division, each with a distinct role-routed dashboard rather than a shared view with permissions bolted on: Attorneys (own individual condemnation case files through the litigation lifecycle), Legal Assistants (case preparation, service/publication tracking, event prep, attorney-facing reminders — Entra-role-routed to their own dashboard, not a renamed copy of the attorney one), and Managers/Administrators (Chief Counsel, Deputy Chief Counsel, and other manager-tier staff — division-wide oversight via Division Overview). No single persona is primary; design tradeoffs should not default to "attorney-first."

## Product Purpose

Case/docket management for ARDOT eminent-domain (condemnation) litigation. The case is the primary work unit — a case may represent one tract within a larger job. Tracks the full litigation lifecycle end to end (pre-filing pipeline through resolution/closure) plus the supporting work around it: deadlines, checklists, discovery, hearings, witnesses, risk analysis/valuation, document generation, and role-specific dashboards.

## Positioning

Currently a feasibility/pilot build, not a committed replacement. ARDOT's Legal Division is evaluating whether this internally-built system can replace Lawtoolbox (an existing external case-management tool) — no cutover decision has been made yet. This is why recent work has run periodic "leanness audits" that actively cut dead or low-value functionality: the goal is a fair, uninflated comparison against Lawtoolbox on real capability, not scoring points by accumulating unused features. Positioning should be treated as provisional and revisited once the pilot concludes either way.

## Operating Context

Currently a local, single-user ASP.NET Core + React application (SQLite storage) bound to `localhost`, mid-migration toward a centrally hosted, multi-user architecture (SQL Server + Microsoft Entra auth). The case workflow stages are: Pipeline (pre-filing assignment, drafting, review, revision, signatures) → Filed / Service Pending → Active Litigation → Settlement Pending → Trial Preparation → Resolved / Closed. Imported historical cases pass through a dedicated Triage-and-Activate screen before entering that pipeline. Distinct dashboards exist today for Attorney, Legal Assistant, and Manager (Division Overview).

## Capabilities and Constraints

- No cloud or external API calls, no Microsoft Word/Excel automation (COM), no production database access from this development environment.
- Blank dates stay blank; `1900-01-01` is treated as blank and must never be "corrected" as if it were a bug.
- Document generation (Case Summary, Case Review Memo, and the unified document-template platform) merges natively into `.docx` via DocumentFormat.OpenXml — no Word automation, no third-party templating engine.
- Microsoft Entra auth is scaffolded but disabled by default. SQLite preview mode is unauthenticated and unrestricted — it is not a substitute for testing Entra role visibility.
- SQL Server is a pilot-only backing store behind a flag, off by default; SQLite remains the deliberately guarded active provider until migration/cutover is complete.
- CSV/XLSX import column order and mapping are fixed — never renamed or reordered without adding explicit mapping logic.
- Outlook/calendar integration is a real long-term goal but not yet scoped or approved — not to be built opportunistically.
- Domain terminology must match actual Arkansas eminent-domain/condemnation practice (e.g. "complaint in condemnation," not a generic "petition"), not generic litigation or another jurisdiction's terms.

## Brand Commitments

Internally referred to as "Case Planner" / "ARDOT Case Planner." A design system already exists (`design-system/MASTER.md`, the "Docket" identity — IBM Plex Sans/Mono, cool-neutral surfaces, one confident blue, Arkansas crimson used exactly once as a brand tick) recording confirmed visual decisions. That system is out of scope for this file; `/impeccable document` is the path to bring it into DESIGN.md.

## Evidence on Hand

`README.md` is the maintained, frequently-updated feature/status record — treat it as more current than other docs for what is SQLite-only vs. SQL-Server-piloted and for the day-to-day feature list. `design-system/MASTER.md` exists with confirmed tokens, type/spacing scale, and a canonical component list. This is internal Arkansas state-agency software, not a marketed product: no customer testimonials, pricing, or marketing evidence exist and none should be fabricated for any surface.

## Product Principles

1. **Provider-neutral dual persistence.** Nearly every domain area has a store/service interface with a SQLite implementation (active) and a SQL Server implementation (pilot, reconciled against SQLite ahead of cutover) — new work follows that shape rather than adding SQLite-only logic in isolation.
2. **Lean over feature-complete.** Functionality earns its place against a fair Lawtoolbox comparison; orphaned, unmounted, or low-value surfaces get cut on a recurring basis rather than accumulating.
3. **Role-distinct, not role-generic.** Attorney, Legal Assistant, and Manager dashboards are purpose-built views over the same underlying case data, each solving that role's actual daily workflow — not one dashboard with visibility toggles.
4. **Domain terminology is exact.** Arkansas eminent-domain/condemnation terms of art must be used correctly everywhere copy appears — UI labels, generated documents, and code alike.
5. **Blank and placeholder data are meaningful.** A blank date or an unset field is a real, intentional state, not a display bug to "fix."

## Accessibility & Inclusion

No formally mandated accessibility standard is confirmed for this project (no WCAG/Section 508 requirement has been established). High-contrast theme variants (Dark and Light) already ship as user-selectable themes in Settings → Appearance — treat that as the current accessibility floor rather than inventing a stricter compliance target.
