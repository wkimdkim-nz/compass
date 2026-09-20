# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

**Primary, in this order:**

1. **Reviewers of the work** — employers, peers, and anyone reading the repo or opening
   <https://financial-compass.dev> to judge how a React + .NET + Postgres + AWS product was built.
   They arrive cold, may find the API half deliberately torn down (see Operating Context), and
   judge craft, coherence, and honesty rather than feature count.
2. **Beginner retail investors** sizing up one US-listed company. They have a ticker and a
   hunch, not a valuation vocabulary. Their job: turn a guess about growth, margin, multiple,
   dilution, and horizon into a future price and a return multiple, and understand *why* each
   input matters. They are prone to over-trusting precise-looking numbers (ADR 0006).

Compass is shipped as a credible beginner tool, but the reviewer is the audience that matters most
today. The product owner is a single developer based in New Zealand.

## Product Purpose

Compass is a stock-projection calculator that **teaches** while it computes. A user types a US
ticker, gets Base Facts and three seeded Scenarios (Bear / Base / Bull) pre-filled from the
company's own SEC filings, edits the assumptions, and watches the X (return multiple), CAGR, and
future price update live. Saving keeps only the assumptions; base facts are always fetched fresh.

Success:

- A beginner opens a pre-filled, editable range instead of blank fields, and leaves knowing what
  each input means and where its number came from.
- A reviewer sees a small, complete, honest product whose seams (auth, same-site delivery,
  deploy, market data) are all proven end to end.

## Positioning

**A teaching tool first.** Every input is explained and anchored to where its number comes from
(the glossary's input guide is the source; the Phase 2 education page is the planned home). The
calculator is the exercise; understanding the inputs is the product. A "1000X"-style sheet, a
broker widget, or a spreadsheet gives a number; Compass gives the number *and* the reasoning
scaffold around it, presented as a hypothetical range rather than a prediction.

Supporting mechanisms (true today or specified for Phase 1):

- Base facts and scenario defaults derive from **SEC EDGAR filings** (fundamentals) and
  **Finnhub** (quote), with a visible **figures-to date** so staleness is informed, not silent
  (ADR 0010).
- Outputs are a **Bear / Base / Bull range**, never one authoritative figure (ADR 0004).
- A **"not financial advice"** disclaimer sits beside the output and in the footer (ADR 0006).

## Operating Context

- **Deployed app:** <https://financial-compass.dev>, an SPA served from S3 + CloudFront with
  `/api/*` path-routed to an ECS service. The **app stack is destroyed between work sessions**
  (ADR 0009): between sessions the page loads but register/sign-in fails with an origin error.
  This is the designed state, and any first-visit or error surface must treat it as such, not as
  an outage.
- **Session:** register / sign in with email + password (min 8 chars, no composition rules);
  `HttpOnly` same-site cookie; no email confirmation, no password reset, no email at all
  (ADR 0007). Repeated failed logins lock the account.
- **Local dev:** Docker Postgres, `dotnet run` (API on :5111), `npm run dev` (Vite on :5173,
  `strictPort`). `.claude/launch.json` defines `api` and `web`.
- **Data sources:** SEC EDGAR `companyfacts` normalised by Compass (TTM revenue, TTM net income,
  diluted weighted-average shares, 3–5 years annual history); Finnhub free tier for the price.
  In-memory cache: quote 15 min, fundamentals and ticker map 24 h.
- **Documents that govern the product:** `docs/glossary.md` (vocabulary and the core formula),
  `docs/roadmap.md` (ordered phases), `docs/adr/` (decisions; 0004, 0006, 0007, 0009, 0010 are
  the live ones), `spec/projection-vectors.json` (planned; the executable formula both
  calculators assert against). GitHub issues are the tracker; Phase 1 spec is #65, design
  decision is #69.

## Capabilities and Constraints

**Shipped:** register, sign in, sign out, session check (Phase 0), and the API to look a ticker
up (#73) and to save, replace and load a projection (#74). The current UI is an unstyled walking skeleton and carries no visual authority.

**Specified for Phase 1 (roadmap, #65–#77):**

- `/` landing: a ticker box and one line on what Compass does.
- `/projections/{ticker}` calculator: a **table** — rows are inputs, columns are Bear / Base /
  Bull; base facts and the figures-to date above; outputs as the last rows with **X as the
  headline** and CAGR and future price beneath; disclaimer under the output row. Every base fact
  and assumption editable in place; P/E vs P/S chosen per scenario; saved-data indicator and
  reset-to-defaults action; Save (new) vs Update with a confirmation modal (existing); 404 / 422 /
  503 messages that leave the form usable. Live X/CAGR on every keystroke.
- **Mobile-first**, phone / tablet / desktop. **Light and dark both required.**
- Whole-number percents and millions in the UI; decimals and raw values on the wire.
- One projection per user per ticker (ticker upper-cased); base facts never stored.

**Hard constraints:**

- **US domestic SEC filers only.** Unknown ticker → 404; known-but-unsupported filer → 422 with a
  plain reason; quote failure alone → 200 with null price; fundamentals failure → 503. Anything
  outside scope is manual entry, a first-class path (an un-pre-filled form, not a separate mode).
  NZX/ASX are not covered and no free source exists; do not imply otherwise.
- Numbers are hypothetical and assumption-driven; never present them as predictions.
- No per-input warnings (grilling round 2); the disclaimer is the guardrail.
- Stack is fixed: Vite + React 19 + TypeScript + Redux Toolkit / RTK Query + react-router;
  Tailwind via the Vite plugin with the project's own token set is the Phase 1 styling decision.
  No component library until Phase 2 pages outgrow tokens.

**Terminology (use these; see glossary for the full list):** Projection, Assumption Set, Base
Facts, Scenario (Bear / Base / Bull), Horizon, Exit Multiple (P/E or P/S), Dilution Rate, CAGR,
the "X", Figures-to date, Company (not "stock"), ticker (the handle, not the entity), Ticker
lookup, Fundamentals source, Quote source. Avoid: "snapshot", "FMP", "the provider", "JWT",
"token", "CORS", "api subdomain", "staging".

**Undecided (recorded, not invented):** the look of the landing and calculator pages (#69 owns
it); how the three scenario columns behave on a phone; Phase 2 education-page structure;
projection list / edit / delete.

## Brand Commitments

- **Name:** Compass. **Domain:** financial-compass.dev. Nothing else is fixed: no logo, colours,
  typography, or defined voice exist yet.
- The Lefebvre "1000X" calculator is the acknowledged lineage of the "X" headline; it is a
  reference for the concept, not a visual or brand commitment.

## Evidence on Hand

- Real product vocabulary and formula: `docs/glossary.md`.
- Real, verified deployment: Phase 0 was proven in a browser on 2026-09-08 (#46); nine-assertion
  smoke script at `scripts/smoke-deployed.sh`.
- Real data-source research: `docs/research/fmp-free-tier.md`,
  `docs/research/edgar-finnhub-alternatives.md` (EDGAR coverage observed; Finnhub quote from docs
  only, never seen live).
- **Absent, must not be fabricated:** testimonials, user counts, customers, press, benchmarks,
  pricing, accuracy claims, any statement about NZX/ASX coverage, any real market figures until
  the lookup ships. Sample tickers used in mock-ups should be marked illustrative.

## Product Principles

1. **Teach the input, not just the output.** Every number has a name, a source, and an anchor;
   the X is the headline but the assumptions are the content.
2. **A range, never a verdict.** Bear / Base / Bull always ship together; precision is framed as
   hypothetical, and the disclaimer is adjacent to the figures, not buried.
3. **Provenance is visible.** Filed facts, the figures-to date, and saved-vs-fresh state are shown,
   so staleness and overrides are informed decisions.
4. **Manual entry is a first-class path.** Out-of-scope or failed lookups leave a usable, honest
   form; errors explain and never dead-end.
5. **Small, complete, honest.** Prove the seams end to end before adding features; the codebase
   and the deployed app are themselves the portfolio artifact a reviewer judges.

## Accessibility & Inclusion

**WCAG 2.2 AA** is the required standard for all UI work. Light and dark themes are both required
and both must meet it. Numeric outputs must remain legible and scannable at phone widths.
