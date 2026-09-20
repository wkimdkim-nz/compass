---
version: 1
slug: "web-src-app-tsx"
primary_target: "web/src/App.tsx"
related_targets: ["docs/design/mockups/landing.html","docs/design/mockups/calculator.html"]
---

# Phase 1 surfaces: `/` landing and `/projections/{ticker}` calculator

Scope: the two Phase 1 pages, one visual world. Landing is Persuade, calculator is Operate. Decided in issue #69 (2026-09-18); recorded for later agents in `docs/design/phase1-look.md`.

Audience and job: a reviewer arriving cold to judge craft and honesty; a beginner with a ticker and a hunch, on a phone, who needs a pre-filled Bear / Base / Bull range and a reason for every number. Signed-out visitors can use the ticker box; submitting routes through sign in and on to the calculator.

Proof: provenance only. Filed figures, the figures-to date, named sources. No testimonials, counts, benchmarks, or accuracy claims; sample tickers marked illustrative.

Constraints: mobile-first; light and dark both WCAG 2.2 AA; Tailwind with the project's own tokens, no component library; percents (8, not 0.08, to one decimal: ADR 0011) and millions in the UI; no per-input warnings; the disclaimer beside the output and in the footer.

Anti-goals: broker terminal, generic SaaS template, decorative charts, prediction wording.

## Direction contract

THESIS: The calculator is set exactly like the audited financial statement its numbers come from, so provenance is the aesthetic. It refuses the fintech dashboard of cards, tabs and coloured charts, and the SaaS hero with feature cards.

OWN-WORLD: White statement paper and near-black ink; single rule above a subtotal, double rule under a total; negatives in parentheses; the period-end date printed over the figures. One accent, the auditor's blue pencil, on every edit and the one primary action. Dark is the microfiche negative: pale ink on charcoal, same rules, accent lightened. One grotesque family (Archivo, width axis) at text width for copy and condensed for totals; tabular lining figures everywhere. No cards, no shadows, no icons.

STORY: A visitor sees a statement page, reads where each figure came from and when, changes a guess in blue pencil, and watches the total under the double rule change. They leave knowing the X follows from their own assumptions.

FIRST VIEWPORT: Landing: a statement cover block, max 40rem, centred. Title "Compass", a rule, the one line on what Compass does, a small units note, then the ticker box as a ruled entry line with the blue "Project" action. Sign in and register in a quiet header rule. Nothing else above the fold; a marked-illustrative specimen of the output rows sits below it. Calculator (phone): company and ticker as the heading, figures-to date and sources on the next line; base facts as opening lines; assumption rows in three columns Bear / Base / Bull with P/E or P/S per column; single rule; the X row as the total under a double rule; CAGR and future price beneath; disclaimer as the footnote; Save or Update below.

FORM: The Audited Statement, my top-ranked grounded candidate, taken as IMPECCABLE'S PICK over the rolled Passage Chart (candidate 7). Seed key 9389f387, scope direction, mode operate, code-led.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance.

## Open decisions

- Whether blue pencil is the right single accent; the design note may compare one alternative.
- Face licensing and self-hosting for Archivo belong to the token ticket.
