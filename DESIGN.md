---
name: ARDOT Case Planner
description: "Docket" — an engineered-civic design system for ARDOT condemnation casework.
colors:
  docket-blue: "#14538F"
  docket-blue-deep: "#0E3F6E"
  docket-blue-mist: "#E3EDF7"
  docket-navy: "#0E2E4E"
  cool-fog: "#F3F6F9"
  paper-white: "#FFFFFF"
  cool-mist: "#EAEFF4"
  hairline-blue: "#D8E0E9"
  control-blue-gray: "#B9C6D4"
  ink-navy: "#16232F"
  slate-muted: "#5A6B7D"
  arkansas-crimson: "#9E1B32"
  meadow-green: "#1E7A44"
  meadow-green-bg: "#E7F4EC"
  amber-clay: "#A85B00"
  amber-clay-bg: "#FBF1E2"
  brick-red: "#B3261E"
  brick-red-bg: "#FBEAE8"
  focus-ring: "#4C8FD6"
typography:
  ui:
    fontFamily: "\"IBM Plex Sans\", \"Segoe UI\", system-ui, sans-serif"
  data:
    fontFamily: "\"IBM Plex Mono\", Consolas, monospace"
  page-title:
    fontFamily: "{typography.ui.fontFamily}"
    fontSize: "18px"
    fontWeight: 600
    lineHeight: "24px"
  panel-title:
    fontFamily: "{typography.ui.fontFamily}"
    fontSize: "15px"
    fontWeight: 600
    lineHeight: "22px"
  body:
    fontFamily: "{typography.ui.fontFamily}"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: "20px"
  label:
    fontFamily: "{typography.ui.fontFamily}"
    fontSize: "11px"
    fontWeight: 600
    lineHeight: "16px"
    letterSpacing: "0.06em"
rounded:
  ctl: "4px"
  panel: "8px"
  full: "999px"
spacing:
  xs: "2px"
  sm: "4px"
  md: "8px"
  lg: "12px"
  xl: "16px"
  "2xl": "24px"
  "3xl": "32px"
components:
  button-primary:
    backgroundColor: "{colors.docket-blue}"
    textColor: "#FFFFFF"
    rounded: "{rounded.ctl}"
    padding: "0 12px"
    height: "30px"
  button-primary-hover:
    backgroundColor: "{colors.docket-blue-deep}"
  button-secondary:
    backgroundColor: "{colors.paper-white}"
    textColor: "{colors.ink-navy}"
    rounded: "{rounded.ctl}"
    padding: "0 12px"
    height: "30px"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.docket-blue}"
    rounded: "{rounded.ctl}"
    padding: "0 12px"
    height: "30px"
  button-danger:
    backgroundColor: "{colors.paper-white}"
    textColor: "{colors.brick-red}"
    rounded: "{rounded.ctl}"
    padding: "0 12px"
    height: "30px"
  status-chip-ok:
    backgroundColor: "{colors.meadow-green-bg}"
    textColor: "{colors.meadow-green}"
    rounded: "{rounded.ctl}"
    height: "20px"
    padding: "0 8px"
  status-chip-warn:
    backgroundColor: "{colors.amber-clay-bg}"
    textColor: "{colors.amber-clay}"
    rounded: "{rounded.ctl}"
    height: "20px"
    padding: "0 8px"
  status-chip-danger:
    backgroundColor: "{colors.brick-red-bg}"
    textColor: "{colors.brick-red}"
    rounded: "{rounded.ctl}"
    height: "20px"
    padding: "0 8px"
  metric-tile:
    backgroundColor: "{colors.paper-white}"
    rounded: "{rounded.panel}"
    padding: "10px 12px"
    height: "64px"
  panel:
    backgroundColor: "{colors.paper-white}"
    rounded: "{rounded.panel}"
    padding: "0.95rem 1.1rem 1.1rem"
---

# Design System: ARDOT Case Planner

## Overview

**Creative North Star: "Docket — engineered civic"**

A state-agency litigation tool should feel like a precisely engineered public document: calm, exact, authoritative, zero decoration. The voice comes from three deliberate choices working together — an engineered grotesque paired with a monospaced data face, a disciplined cool-neutral surface system, and exactly one confident accent blue. Arkansas crimson appears exactly once in the entire system, as the brand tick in the app bar; it is never reused as a semantic or decorative color anywhere else.

This identity was chosen over a "Brief" legal-paper direction (Source Serif display, warm neutrals, oxblood accent) specifically because the daily surface is dense tables that attorneys scan all day: the warm/serif direction softened data contrast, complicated dark mode, and traded scanability for atmosphere. It was also chosen over a slate-and-signal-orange "ops console" direction, rejected because it read as SRE tooling rather than counsel, and its orange collided with the warning color in a deadline-heavy interface. Both rejections are durable: neither direction should resurface as a "modernization."

**Key Characteristics:**
- One accent color carries every action and active state; nothing else competes with it.
- Status is never color-only — a dot plus text label together carry every state.
- Panels are flat with a single hairline border; elevation is a last resort, reserved for true overlays.
- Every numeric, date, case-number, or currency value renders in the monospaced data face with tabular figures.
- Sixteen selectable themes share one semantic contract (success/warning/danger/focus never change meaning across them), so "which theme is active" never changes what a color communicates.

## Colors

The palette is disciplined and cool: one blue carries all action and emphasis, three semantic colors carry all status meaning, and Arkansas crimson is spent once, deliberately, on the brand mark.

### Primary
- **Docket Blue** (`#14538F`): all primary actions, links, active states, and the one accent this system permits itself. Used sparingly — most of any given screen is neutral.
- **Docket Blue Deep** (`#0E3F6E`): hover/active state for Docket Blue (primary button hover, link hover).
- **Docket Blue Mist** (`#E3EDF7`): selected-row backgrounds, active filter chips, and the soft fill behind primary-toned status chips.

### Neutral
- **Cool Fog** (`#F3F6F9`): app background — deliberately cool-biased rather than a default gray, so the white surfaces above it read as distinct panels.
- **Paper White** (`#FFFFFF`): every panel, card, table, and input surface.
- **Cool Mist** (`#EAEFF4`): sunken wells, table stripe, filter input backgrounds.
- **Docket Navy** (`#0E2E4E`): the app bar background — the one surface that inverts to dark even in the Light theme.
- **Hairline Blue** (`#D8E0E9`): every panel border and table row rule.
- **Control Blue-Gray** (`#B9C6D4`): input and button borders — one step stronger than the hairline, since controls need to read as touchable.
- **Ink Navy** (`#16232F`): primary text.
- **Slate Muted** (`#5A6B7D`): secondary text — meta lines, helper text, table sub-labels. Verified 4.5:1+ on Paper White.

### Named Rules
**The One Tick Rule.** Arkansas Crimson (`#9E1B32`) appears in exactly one place: the brand tick in the app bar. It is never a status color, never a hover state, never reused for emphasis — the moment it appears twice, it has stopped being a brand mark.

**The Dot-Plus-Text Rule.** Status is never carried by color alone. Every status chip pairs a colored dot with a text label; every chart uses direct labels rather than a color-only legend. This is a hard accessibility floor, not a style preference — it is verified per screen.

### Status (semantic, shared across every theme)
- **Meadow Green** (`#1E7A44` on `#E7F4EC`): served, done, complete, resolved.
- **Amber Clay** (`#A85B00` on `#FBF1E2`): due soon, waiting, draft, needs review.
- **Brick Red** (`#B3261E` on `#FBEAE8`): overdue, failed, and destructive actions.
- **Focus Ring** (`#4C8FD6`): the global 2px focus outline (2px offset). Never removed, never replaced with an outline-color-only treatment.

### Dark and high-contrast states
Dark mode (`data-theme="dark"`) inverts to a true dark cool-neutral ground (`#0F161D` background, `#16202B` surface) and lifts Docket Blue to `#6FA8DC` for sufficient contrast on dark; elevation in dark mode is a lighter surface tone, never a shadow. Two accessibility-mode themes (`high-contrast` and `high-contrast-light`, dark and light polarity respectively) push every pairing past AAA — 7:1 for normal text, 3:1 for UI — rather than the AA floor the other themes hold to; their status colors are chosen for relative-luminance separation as well as hue, so someone with reduced color perception gets a lightness cue on top of the dot-plus-text rule. Beyond these four canonical states, thirteen additional personalization themes exist (three pastel-light variants — Pastel Blue, Pastel Sage, Pastel Lavender — and ten dark-family hue variants — Deep Navy, Forest, Slate, Sunset, Rose, Ocean, Plum, Amber, Carbon, Arctic). Every one of them reuses the exact same semantic contract (status colors, focus ring role, text-on-surface pairing logic) and only swaps background/surface/primary/border/app-bar hue — so switching themes is always a re-skin, never a re-meaning.

## Typography

**UI Font:** IBM Plex Sans (with "Segoe UI", system-ui, sans-serif fallback)
**Data Font:** IBM Plex Mono (with Consolas, monospace fallback)

**Character:** An engineered grotesque paired with a monospaced data face — the pairing itself is the point. IBM Plex Sans reads as precise and civic without feeling corporate; IBM Plex Mono exists for exactly one job, making every number in the system align and compare cleanly. Both are self-hosted via `@fontsource`; there is no CDN dependency and no fallback to a generic system sans as the primary choice.

### Hierarchy
- **Page title** (600, 18px/24px): the page-level heading; one per page.
- **Panel title** (600, 15px/22px): panel and modal headers.
- **Emphasized body** (500, 14px/20px): form inputs, emphasized values.
- **Body** (400, 13px/20px): the app's default — table cells, controls, body copy.
- **Secondary** (400, 12px/16px): meta text, chips, secondary cell content.
- **Label** (600, 11px/16px, uppercase, +0.06em tracking): table headers and fine print only. This is the only size that is ever uppercased.

### Named Rules
**The Tabular Everything Rule.** Every numeric, date, case-number, and currency value in a table cell or metric tile renders in the data font with `font-variant-numeric: tabular-nums` — no exceptions. Numbers must align down a column and compare at a glance.

**The No-Italics Rule.** UI chrome never uses italics. No 700-weight type exists anywhere in the system — 600 is the ceiling.

## Layout

Desktop-first, deliberately dense (density 8 of 10): this is an operations console where attorneys scan tables all day, not a marketing surface with room to breathe. The app width caps at `min(1600px, 100vw - 32px)` — wider than a typical 1440px ceiling, because wide tables are the actual product. Layout is flex/grid `gap` only; margin-based spacing hacks don't belong in this system.

**Spacing scale (px):** 2, 4, 8, 12, 16, 24, 32 — seven steps, nothing between them. Component internal padding runs 8–12px; gaps between related controls are 8px; gaps between control groups are 16–24px; the page gutter is 24px; vertical rhythm between stacked panels on a page is 16px.

**Density standards:**
- Table row: 32px, with 8px × 10px cell padding, sticky header, optional `--surface-muted` stripe.
- Table header row: 26px, label typography, muted text color.
- Control (input/select/button): 30px default height, 26px when inline-in-table.
- Button: 30px tall, 12px side padding; icon-only buttons are 30×30 with hit area extended to ≥36px via margin.
- Status chip: 20px tall, 7px dot.
- Metric tile: 64px tall.
- Panel padding: ~0.95rem/1.1rem (roughly 15px/18px); panel title row is 36px.

## Elevation & Depth

Borders do the work, not shadows. Panels are flat at rest with a single 1px hairline border and no shadow — the system deliberately has no resting-state elevation. Exactly one shadow token exists in the entire system, reserved for true overlays: modals, popovers, and drawers. Dark mode conveys depth by lifting surface lightness rather than adding shadow.

### Shadow Vocabulary
- **Overlay shadow** (`0 8px 24px rgba(15, 25, 40, .10)` in Light, deepening to `rgba(0,0,0,.6)` in the high-contrast dark theme): the only shadow role in the system — modals, popovers, and drawers exclusively.

### Named Rules
**The Flat-at-Rest Rule.** A panel earns a shadow only by becoming an overlay (modal, popover, drawer). A panel sitting in the normal page flow is always flat with a border; if it needs to stand out, that's a border-color or background change, never a shadow.

## Shapes

Two radii, no more: `4px` for every control, chip, and table cell (buttons, inputs, status chips, type chips), and `8px` for every panel, card, and modal. A `999px` full-round radius exists only for avatar-like circular elements. Corners are consistent enough that "which radius does this get" is never a judgment call — controls get 4px, containers get 8px.

## Components

### Buttons
- **Shape:** 4px radius, 30px height (26px for the small/inline-in-table variant), 12px side padding (8px for small).
- **Primary:** solid Docket Blue background, white text; hover deepens to Docket Blue Deep. This is the only button variant with a filled background at rest.
- **Secondary:** white surface, hairline-strength border, hover shifts border and text color to Docket Blue rather than filling the background.
- **Ghost:** transparent at rest, Docket Blue text; hover fills with Docket Blue Mist. Used for low-emphasis inline actions.
- **Danger:** white surface with Brick Red text and border at rest; hover fills with the Brick Red background tint. Danger only changes color, never layout — a destructive action should never visually jump.

### Status Chips
- **Style:** pill-adjacent with a 4px radius (not fully rounded), 20px tall, a 7px solid dot at `currentColor` plus a text label — the dot-plus-text rule applies here first.
- **Tones:** ok (Meadow Green), warn (Amber Clay), danger (Brick Red), neutral (muted text on a subtle surface), primary (Docket Blue on Docket Blue Mist). Tone is chosen from a single status-to-tone mapping function, never assigned ad hoc per screen.

### Type Chips
- **Style:** uppercase, 11px, 600 weight, +0.04em tracking, 1px/6px padding, 4px radius — visually distinct from Status Chips (no dot, always uppercase) so the two never get confused despite similar coloring. Used to mark a work item's kind (deadline, task, event, service) rather than its state.

### Metric Tiles
- **Style:** 64px tall, white surface, 8px radius, hairline border. Label sits uppercase and muted above a large tabular-numeral value in the data font; an optional muted unit suffix trails the value, and an optional hint line sits below.
- **Interactive variant:** when given a click handler, the tile becomes a toggleable filter facet (`aria-pressed`) — border and text shift to Docket Blue when active. The same component serves both static display and filter-toggle duty; there is no separate "clickable metric tile" component.

### Panels
- **Corner Style:** 8px radius, hairline border, no shadow at rest (see Elevation & Depth).
- **Background:** Paper White surface on the Cool Fog page background.
- **Header:** ~0.9rem/1.1rem padding, bottom hairline border separating it from the body; header and body padding are distinct scale steps, not one uniform inset.
- **Collapsible variant:** the same flat panel shape, with a disclosure control in the header; used throughout Settings and case-workspace detail sections rather than a separate accordion component.

### Filter Bar
- **Style:** single flat bar (hairline border, 8px radius) holding a live search input, chip-style facet toggles, and a right-aligned active-filter summary in muted text. Facet chips use the same visual language as Status Chips' neutral tone but with an "on" state that switches to Docket Blue Mist background and Docket Blue text/border — the same on/off treatment used by Metric Tiles' filter-toggle variant, so "this is currently filtering the view" reads consistently across both components.

### Empty States
- **Style:** left-aligned, generous internal padding (24px vertical, 16px horizontal) relative to its container, a medium-weight title in body text color, an optional muted hint line, and an optional action below. Has a dedicated table-row mode (renders as a full-width `<tr>`) so an empty table never needs a bespoke "no results" row built by hand.

## Do's and Don'ts

### Do:
- **Do** keep Docket Blue as the only accent color competing for attention on any screen — supporting UI should recede into the neutral scale.
- **Do** pair every status indicator with a text label; color alone never carries a state.
- **Do** render every number, date, case number, and currency value in the data font with tabular figures.
- **Do** keep panels flat (border only) at rest; reserve the overlay shadow for modals, popovers, and drawers.
- **Do** treat all sixteen themes as re-skins of one semantic contract — a new theme adds background/surface/primary/border hues, never a new meaning for success/warning/danger/focus.
- **Do** use full month names with no ordinal for dates (`July 18, 2026`), and always suffix date-times with `CT` (America/Chicago) — there is exactly one formatter pair (`formatDate`/`formatDateTime`) for this.
- **Do** render an unset value as an em dash (`—`) in muted text rather than a repeated string like "Not set".

### Don't:
- **Don't** reuse Arkansas Crimson anywhere except the app-bar brand tick — not as a second accent, not as a danger color, not as emphasis.
- **Don't** introduce a new radius value outside 4px (controls) / 8px (panels) / 999px (circular). The system deliberately killed a larger radius set.
- **Don't** use italics anywhere in UI chrome, or any font weight heavier than 600.
- **Don't** add decorative animation. Motion is 120ms ease-out for state changes and 160ms for overlays entering — functional only, and fully disabled under `prefers-reduced-motion: reduce`.
- **Don't** revive the "Brief" legal-paper direction (serif display, warm neutrals, oxblood accent) or the "Control" ops-console direction (slate + signal-orange) — both were deliberately evaluated and rejected for this product; see `design-system/MASTER.md` §10–11 for the full reasoning if either resurfaces as a proposal.
