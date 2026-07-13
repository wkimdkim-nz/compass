# ADR 0004 — Projections hold Bear/Base/Bull scenarios; snapshot vs. live inputs

**Status:** Accepted
**Date:** 2026-07-01

## Context

For beginner safety we present a *range* (Bear / Base / Bull) rather than one authoritative number
(see grilling round 1). A market-data API (FMP, ADR 0005) is now in iteration 1, which pre-fills
base facts and derives sensible scenario defaults — so the earlier "manual entry v1 + frozen
snapshot" plan is superseded by a single, simpler persistence model below.

## Decision

Data shape:

- A **Projection** belongs to a user and targets one company (ticker + name).
- A Projection holds a **Snapshot (base facts)**: current price, TTM revenue, shares outstanding.
- A Projection has up to three **Scenarios** (Bear / Base / Bull). Each Scenario is one
  **Assumption Set**: growth rate, future net margin, exit multiple (P/E or P/S), dilution rate,
  horizon. Outputs (future price, X, CAGR) are *computed*, not stored (or cached only).

Persistence rule (single model, since FMP is in iteration 1):

- Persist **only the user-set variables** (the ticker + the Assumption Sets). **Base facts are
  fetched live from FMP** at load time and pre-fill the form; they are *not* stored. A saved
  projection therefore reflects current market data each time it is opened — not a frozen result.
- Base-fact fields remain **editable**: FMP pre-fills them, but the user can override any value
  (and this is the fallback for a ticker FMP does not cover). "Manual entry" is just an
  un-pre-filled form, not a separate mode.

## Consequences

- Schema (app DB, ADR 0003): `projections` (1) → `scenarios` (≤3). `projections` stores the ticker
  and user identity; it does **not** persist base facts (price/revenue/shares) — those are live.
- Scenario defaults are derived from FMP history (Base ≈ trailing 3–5yr revenue CAGR; Bear ≈ ½×;
  Bull ≈ 1.5×; future margin seeded from current margin) so a beginner opens pre-filled, editable
  scenarios instead of blank fields.
- Compute is duplicated: **TS** computes live in the browser for instant preview as the user types;
  **.NET** recomputes authoritatively (the formula is small enough that duplication is cheap). Both
  read the glossary formula as the single spec.
