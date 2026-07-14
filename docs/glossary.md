# Glossary — Compass (stock projection calculator)

The shared language for this project. If a term here is fuzzy, the feature built on it will be fuzzy too. Update as decisions land.

## Domain terms

- **Projection** — A saved calculation that turns a set of *assumptions* about a company's future into an estimated future share price and return. The core entity of the app.
- **Assumption Set** — The user-supplied inputs that drive a projection: growth rate, future margin, exit multiple, dilution, horizon. These are *guesses*, not facts.
- **Base Facts** — The company's actual current figures: current price, TTM revenue, shares outstanding. Fetched **live from FMP** (ADR 0005) each time a projection loads; not persisted. Editable, so the user can override or handle a ticker FMP doesn't cover.
- **Horizon** — The number of years into the future being projected (e.g. 5 or 10).
- **Exit Multiple** — The valuation multiple applied at the end of the horizon. Either **P/E** (price/earnings, applied to net income) or **P/S** (price/sales, applied to revenue).
- **Dilution Rate** — Annual % increase in share count from new share issuance. Growth companies dilute; ignoring it overstates per-share returns.
- **CAGR** — Compound Annual Growth Rate. The annualized return implied by the projection: `(future_price / current_price)^(1/years) − 1`.
- **The "X"** — Total return multiple: `future_price / current_price`. The headline number (a "10X" = 10× your money) that names Lefebvre's "1000X" style of calculator. Precise-looking but built on guesses — see Disclaimer.
- **Disclaimer** — The "not financial advice" framing shown in the footer and beside the projection output (ADR 0006). Reminds users the X/CAGR figures are hypothetical, assumption-driven, and educational only.
- **Scenario** — A named variant of an assumption set (Bear / Base / Bull) for the same company. A Projection has up to three. Defaults are derived from FMP history (ADR 0004/0005).

## The core formula (P/E path)

```
future_revenue     = current_revenue × (1 + growth_rate)^horizon
future_net_income  = future_revenue × future_net_margin
future_shares      = current_shares × (1 + dilution_rate)^horizon
future_market_cap  = future_net_income × exit_PE
future_price       = future_market_cap / future_shares
X (return)         = future_price / current_price
CAGR               = X^(1/horizon) − 1
```

P/S path: `future_market_cap = future_revenue × exit_PS`, then divide by `future_shares`.

## Input guide (source for the Phase 2 education page)

Each input, where the number comes from, and how a beginner should anchor it. Where the app can
derive a value from FMP it pre-fills it — the user only overrides when they expect the future to
differ from the past.

### Base facts (FMP pre-fills; editable)

- **Current share price** — *What:* today's price per share. *Where:* FMP quote; any broker, Google,
  Yahoo Finance. *Basis:* it's a fact, not an assumption — the denominator of the "X".
- **Revenue (TTM)** — *What:* trailing-twelve-month sales. *Where:* FMP income statement; 10-K/10-Q,
  stockanalysis.com, Macrotrends. *Basis:* the starting point growth compounds from.
- **Shares outstanding (diluted)** — *What:* total diluted share count. *Where:* FMP; 10-K cover /
  weighted-average diluted shares, Macrotrends, Yahoo Statistics. *Basis:* fact; future value is this
  grown by the dilution rate.
- **Current net margin** — *What:* net income ÷ revenue today. *Where:* FMP income statement.
  *Basis:* the reference point for choosing a *future* margin.

### Assumptions (the guesses — pre-filled from history, meant to be edited)

- **Horizon (years)** — *How to choose:* the holding period you're evaluating, commonly 5 or 10.
  Longer = more compounding but less reliable.
- **Revenue growth / yr** — *Anchor on:* the company's own trailing 3–5yr revenue CAGR (FMP history),
  cross-checked against analyst estimates and the size of its market (TAM). *Sanity:* >40%/yr is rarely
  sustained past a few years; the bigger the revenue base, the harder high growth gets.
- **Future net margin** — *Anchor on:* current margin plus where mature peers in the same sector land.
  *Sanity:* software matures high (20–40%), retail/hardware low (single digits). Rising from a low
  current margin assumes operating leverage — reasonable for scaling companies, optimistic for others.
- **Exit multiple (P/E or P/S)** — *What:* the valuation multiple applied at the end of the horizon.
  *Anchor on:* the sector's typical multiple and the company's own current multiple, usually assuming
  it *compresses* as growth slows. *Where:* FMP / sector comparisons. *Sanity:* use **P/S** when the
  company isn't reliably profitable yet, **P/E** once margins are stable. This is the single biggest
  swing factor — small changes here move the "X" a lot.
- **Share dilution / yr** — *Anchor on:* the historical share-count CAGR, `(shares_now/shares_then)^(1/years) − 1`
  (FMP history; Compass computes this as the default). *Where else:* Macrotrends "Shares Outstanding",
  10-K statement of stockholders' equity. *Sanity:* buyback companies < 0; stable ~0–1%; growth cos
  2–5% (mostly **stock-based compensation**); frequent capital-raisers 5–10%+. Ignoring it overstates
  per-share returns.

## Stack terms

- **ASP.NET Core Identity** — The .NET framework's built-in identity system. Compass's own
  identity provider (ADR 0007): it stores users in the app Postgres and hashes passwords (PBKDF2).
  Replaces the earlier Supabase-as-IdP design. _Avoid_: "auth service", "Supabase".
- **Session cookie** — How an authenticated session is carried: an `HttpOnly; Secure; SameSite=Lax`
  cookie the browser attaches automatically (ADR 0007). Not readable from JavaScript; there is no
  bearer token. _Avoid_: JWT, access token, Bearer.
- **Antiforgery token** — The CSRF defense required because the session rides in an auto-attached
  cookie: a cookie-to-header token checked on state-changing requests (ADR 0007). _Avoid_: CSRF
  token (use "antiforgery" to match the ASP.NET Core API).
- **ADR** — Architecture Decision Record. A short doc capturing one decision, its context, and consequences. See `docs/adr/`.

> Historical: earlier ADRs (0002/0003, superseded) used **Supabase** as the auth provider and a
> **JWT** bearer token. Auth is now .NET-native (ADR 0007) and Supabase is removed from the project.
