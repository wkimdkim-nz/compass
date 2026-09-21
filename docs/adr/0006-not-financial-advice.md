# ADR 0006 — "Not financial advice" disclaimer

**Status:** Accepted (amended 2026-09-21)
**Date:** 2026-07-01

## Context

The calculator outputs precise-looking return multiples ("47.3X", "23% CAGR") derived entirely from
user guesses (growth, margin, exit multiple). The precision reads as authoritative; the target
audience is beginner investors who may over-trust it. We deliberately do not warn on individual
inputs (grilling round 2), so a plain disclaimer is the minimum honest guardrail.

## Decision

Show a **"not financial advice"** disclaimer:

- Persistently in the app **footer**.
- Adjacent to the **projection output** (near the X / CAGR figures), framing them as illustrative,
  not predictive.

Copy is short and plain: projections are hypothetical, based on assumptions the user chose, and are
for educational purposes only — not investment advice.

## Consequences

- One shared UI component reused in both locations.
- Reinforced by presenting Bear/Base/Bull as a *range* (ADR 0004) rather than a single number.
- No legal review implied; this is good-faith framing, not a compliance program.

> **Amended 2026-09-21 (Phase 1 close-out, #77).** Built as decided (#75), with one detail the
> decision left open: the one shared component, `Disclaimer`, carries **two wordings**, since the
> two places are read differently. Under the calculator's output rows it is a footnote addressed
> to the user ("Hypothetical. These figures follow from the assumptions above, which you chose.
> They are not a prediction and not investment advice."); in the footer of every page, signed in
> or not, it is the general statement (hypothetical, from assumptions the user chooses, for
> education only, not investment advice). The component is the only place either is worded.
