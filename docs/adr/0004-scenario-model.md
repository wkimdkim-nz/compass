# ADR 0004 — Projections hold Bear/Base/Bull scenarios; snapshot vs. live inputs

**Status:** Accepted (amended 2026-09-17)
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

> **Amended 2026-09-17 (Phase 1 grilling; provider now SEC EDGAR + Finnhub, ADR 0010).** Four
> points the original left open, or that Phase 1's design changed. Every "FMP" above reads as
> "the fundamentals source" or "the quote source" (ADR 0010).
>
> 1. **"Fetched live" means "not older than the cache TTL".** The persistence rule is about the
>    *Projection*: a saved projection stores only the ticker and the Assumption Sets, and opening
>    it fetches base facts afresh, so it is never a stale answer. A short-lived **in-memory** cache
>    of provider responses (quote 15 minutes, fundamentals 24 hours) is infrastructure, not
>    projection state, and does not break that rule. Base facts are still never written to the
>    database.
> 2. **One projection per user per ticker.** A user's projection for a ticker is the one place
>    that thesis lives; two theses are two scenarios, not two projections. Uniqueness is
>    on `(user_id, ticker)` with the ticker upper-cased; the row also stores the company name and
>    the EDGAR CIK (null for manual entry) so a reload does not re-resolve the ticker. The key is
>    the ticker, not the CIK, on purpose: an issuer with several share classes (Alphabet,
>    Berkshire) has one CIK but a different price and share count per class, so each class is its
>    own projection, and manual-entry rows have no CIK to key on. `POST /api/projections` creates and
>    answers 409 if one exists; `PUT /api/projections/{ticker}` replaces and answers 404 if none.
>    The UI asks for confirmation before replacing, and the two verbs are what stop a client that
>    skipped the confirmation from overwriting silently. Entering a ticker that already has a
>    saved projection loads the **saved assumptions** over fresh base facts, marked visibly as
>    saved data, with a reset-to-defaults action. A saved projection is readable whether or not
>    the lookup can seed it: `GET /api/projections/{ticker}` returns the assumptions with empty
>    base facts and the lookup's reason when the ticker is unknown, the filer unsupported, or
>    the provider down, so manual-entry projections reload the same way every time. That route's
>    own 404 means only "no saved projection".
> 3. **Shares outstanding is the diluted weighted average**, current and historical, for the
>    reasons in ADR 0010. The "Snapshot (base facts)" above gains a fourth member, **current net
>    margin**, and a **figures-to date** (the period end the TTM figures run to), which the UI
>    shows so staleness is informed rather than silent.
> 4. **Scenario defaults, made exact.** Base growth = trailing revenue CAGR over the years
>    returned; **Bear = base − ½|base|; Bull = base + ½|base|** (identical to ½× and 1.5× for
>    positive growth, and correctly ordered when the company is shrinking); future net margin =
>    current margin for all three; dilution = share-count CAGR over the same years (negative for
>    buybacks); horizon 5; exit multiple = current P/E, or current P/S when TTM net income ≤ 0.
>    Fewer than two years of history gives **no defaults** rather than a CAGR over zero years.
>    Which fields were pre-filled versus edited is not tracked.
>
>    **Amended 2026-09-20 (Bear's floor, #73).** A base below −⅔ would put Bear below −100%, a
>    rate both calculators reject, so the default would open on an error. **Bear is held at
>    −100%**, the lowest rate they take. Seeding nothing was the alternative, and would have
>    thrown away a sound Base and Bull for the sake of one bound.
>
> The duplicated compute is kept, and made honest: both calculators use **double** and are tested
> against one hand-computed vectors file at the repo root, with the tolerance stated in the file.
> Persisted assumptions remain `numeric` in Postgres (ADR 0008); outputs are never stored.
