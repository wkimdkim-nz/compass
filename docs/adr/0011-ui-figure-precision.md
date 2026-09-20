# ADR 0011 — Percent fields keep one decimal; the current net margin is printed, not typed

**Status:** Accepted
**Date:** 2026-09-20

## Context

The roadmap, the Phase 1 spec (#65) and the design note all said "whole-number percents" in the UI.
Building the calculator (#75, PR #92) showed the phrase carrying two meanings. The intended one is
the unit: a percent field holds 8, not 0.08. Read as a precision rule it does harm: the form holds
text and a save sends what the text says, so a seeded 1.3% a year of buybacks shown as "(1)" would
save as a different assumption from the one the filings gave, without the user touching it.

The same documents list the current net margin among the editable base facts. No formula takes it:
the projection runs on price, revenue, shares and the *future* net margin. An editable field wired
to nothing lets a user type a figure and watch nothing change.

## Decision

- A percent field shows and accepts a percent (8, not 0.08) to **one decimal**. Whole values still
  print whole: 8, not 8.0. Decimals on the wire, as before.
- The **current net margin is printed, not typed**. It stays among the base facts as the figure the
  future net margin is set against, and shows a dash when the lookup gave none. Price, TTM revenue
  and diluted shares stay editable, since those are what manual entry needs.

## Consequences

- "Whole-number percents" in the roadmap, the design note and `web/README.md` is reworded to point
  here. The glossary's "Editable" for base facts now excepts the current net margin.
- What a field shows is what a save sends. A value finer than its field prints (a P/E of 28.04
  shown as 28) is saved as shown; that is accepted, the inputs are guesses.
- The calculator mock-up still draws the current net margin as a field; the mock-up is a record of
  the chosen look, not of this decision.
- Were a later formula to take the current net margin (say, a margin path from today's to the
  future one), it would become a field again, by a new ADR.
