# Glossary — Compass (stock projection calculator)

The shared language for this project. If a term here is fuzzy, the feature built on it will be fuzzy too. Update as decisions land.

## Domain terms

- **Projection** — A user's saved thesis on one **Company**: the calculation that turns a set of *assumptions* about its future into an estimated future share price and return. The core entity of the app. What is saved is the ticker, the company name, the CIK and one to three **Scenarios**, with no base facts and no outputs: the **Base Facts** are fetched live on every load, and the outputs (future price, X, CAGR) are computed, in the browser as the user types and again by the .NET calculator on every save, against the base facts on the user's form, which the save carries and then lets go (ADR 0004, amended). A save answers with the outputs; a load does not.
- **Assumption Set** — The user-supplied inputs that drive a projection: growth rate, future margin, exit multiple, dilution, horizon. These are *guesses*, not facts.
- **Base Facts** — The company's actual current figures: current price, TTM revenue, shares outstanding (diluted weighted average), current net margin, and the **figures-to date** they run to. Fetched **live** each time a projection loads — from the **Fundamentals source** and the **Quote source** (ADR 0010), within the cache TTL (quote 15 minutes, fundamentals 24 hours; ADR 0004, amended) — and never written to the database. The price is null when the Quote source fails and the current net margin null when TTM revenue is not positive; when the **Ticker lookup** cannot answer at all, a saved Projection still loads, with no base facts and the lookup's plain reason in their place. Editable, so the user can override any value or type in a company the lookup does not cover; the current net margin alone is printed, not typed, since no formula takes it (ADR 0011). _Avoid_: "snapshot" for the live values — a Projection holds no frozen copy.
- **Horizon** — The number of years into the future being projected (e.g. 5 or 10).
- **Exit Multiple** — The valuation multiple applied at the end of the horizon. Either **P/E** (price/earnings, applied to net income) or **P/S** (price/sales, applied to revenue).
- **Dilution Rate** — Annual % increase in share count from new share issuance. Growth companies dilute; ignoring it overstates per-share returns.
- **CAGR** — Compound Annual Growth Rate. The annualized return implied by the projection: `(future_price / current_price)^(1/years) − 1`.
- **The "X"** — Total return multiple: `future_price / current_price`. The headline number (a "10X" = 10× your money) that names Lefebvre's "1000X" style of calculator. Precise-looking but built on guesses — see Disclaimer.
- **Disclaimer** — The "not financial advice" framing shown in the footer and beside the projection output (ADR 0006). Reminds users the X/CAGR figures are hypothetical, assumption-driven, and educational only.
- **Scenario** — A named variant of an assumption set (Bear / Base / Bull; `bear`, `base`, `bull` on the wire) for the same company. A Projection has one to three, each name at most once. Defaults are seeded from the company's filed history (ADR 0004, amended; ADR 0010): Base growth is the trailing revenue CAGR, Bear and Bull half of its size either side with Bear held at −100%, and none at all where there is nothing sound to seed from (fewer than two years of history, no current net margin, or revenue or shares that start or end at nothing or below).
- **Company** — What a Projection targets: one US-listed issuer, identified to the user by its **ticker** (upper-cased, a share class written the way the SEC writes it: BRK.B is BRK-B) and to the Fundamentals source by its SEC **CIK** (null on a Projection whose company was typed in by hand). A user has **at most one Projection per Company** (ADR 0004, amended) — competing theses are Scenarios inside it, not a second Projection. _Avoid_: "stock", "symbol" as the entity; use ticker for the handle and Company for the thing.
- **Ticker lookup** — The authenticated `GET /api/tickers/{symbol}` that turns a ticker into Base Facts plus up to five fiscal years of history plus three seeded Scenarios (or none; see **Scenario**). Covers **US domestic SEC filers only**; an unknown ticker is a 404, a known-but-unsupported filer (e.g. a 20-F issuer) is a 422 with a plain reason, a Fundamentals source that cannot be reached is a 503, and a quote failure alone is still a 200 with a null price, a `missing` list naming it, and scenarios whose exit multiple keeps its kind and has no value, since a multiple is the price over something (ADR 0010).
- **Fundamentals source** — The port the Ticker lookup gets revenue, net income, shares and history from. Today **SEC EDGAR** (`companyfacts`), normalised by Compass: ordered revenue tags, TTM summed with Q4 = FY − 9M, diluted weighted-average shares **split-adjusted** from the restatements, transition reports (10-KT, 10-QT) read as the 10-K or 10-Q they stand in for (ADR 0010, amended). Has a stub for tests and keyless local runs. _Avoid_: "FMP" (historical, ADR 0005 superseded), "the provider" without saying which.
- **Split-adjusted** — Said of a filed share count put on the basis of the latest filing. `companyfacts` records no share split and a 10-K restates only two prior years, so the Fundamentals source infers each split from the restated counts and multiplies the older years of the history by it; otherwise the seeded **Dilution Rate** reads a split as issuance (ADR 0010, amended). The inferred splits are checked against every restated count; where they do not reconcile (a serial reverse-splitter, a figure filed in the wrong scale), or one was seen in a single restatement only, nothing is adjusted and the history is cut short at the latest split instead. A split since the latest filing is not seen.
- **Quote source** — The port the Ticker lookup gets the current price from. Today **Finnhub** free, keyed by a secret in the foundation stack (ADR 0010). Has a stub. Failure here is partial, never a 503, and an answer holding no price (Finnhub's 200 of zeroes for a ticker it does not know) counts as one.
- **Figures-to date** — The period end the TTM Base Facts run to. Shown beside the base facts so a stale filing set is visible rather than silent (ADR 0010).

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

The executable form of this formula is `spec/projection-vectors.json` at the repo root: hand-computed cases that both the .NET and the TypeScript calculator assert against, with the tolerance and the wire units (decimals, raw values, exit multiple as a `pe`/`ps` kind plus value) stated in its header (ADR 0004, amended). If this prose and that file disagree, the file is wrong until a case is re-derived by hand.

## Input guide

What each figure on the calculator means, how to choose it and where to look it up is the public
Guide at `/guide`, rendered from one typed content module in the web app,
`web/src/features/guide/guideEntries.ts`. Edit the guide there, not here.

## Stack terms

- **ASP.NET Core Identity** — The .NET framework's built-in identity system. Compass's own
  identity provider (ADR 0007): it stores users in the app Postgres and hashes passwords (PBKDF2).
  Replaces the earlier Supabase-as-IdP design. _Avoid_: "auth service", "Supabase".
- **Session cookie** — How an authenticated session is carried: an `HttpOnly; SameSite=Lax` cookie
  the browser attaches automatically (ADR 0007). `Secure` is **environment-conditional**: `Always` in
  production, `SameAsRequest` locally, because local development runs over plain HTTP (ADR 0007,
  amended 2026-09-04). `HttpOnly` and `SameSite=Lax` hold in both. Not readable from JavaScript;
  there is no bearer token. _Avoid_: JWT, access token, Bearer.
- **Data Protection key ring** — The keys ASP.NET Core Data Protection uses to encrypt and sign the
  **Session cookie** itself. Kept in the app Postgres via `PersistKeysToDbContext<AppDbContext>`,
  unencrypted, under an explicit `SetApplicationName("Compass")` — the same store in every
  environment, local dev included (ADR 0007). The table (`data_protection_keys`) is **permanent**,
  not part of the throwaway skeleton. **Deliberately non-durable**: it lives in the *ephemeral*
  stack's RDS beside `AspNetUsers`, so `terraform destroy` drops the keys and the users together.
  That is the design, not a gap — a ring outliving the user table would decrypt old cookies into
  users who no longer exist. _Avoid_: "session secret", "signing key"; and avoid calling its loss a
  bug — see **Ephemeral environment** and the amendment in ADR 0007.
- **Same-site cookie delivery** — The single-origin arrangement that lets the session cookie attach:
  a Vite dev proxy in development and CloudFront path-routing `/api/*` to the ALB in production, so
  the SPA and API share one origin (ADR 0007). With no antiforgery token, this is also what carries
  the CSRF defense — `SameSite=Lax` only holds because there is no cross-origin path in. **This is
  load-bearing, not a convenience**: an `api.` subdomain would be cross-origin and would force
  antiforgery back (ADR 0009). _Avoid_: "CORS" — Compass registers no CORS policy at all; _avoid_
  "api subdomain".
- **Antiforgery token** — A cookie-to-header token checked on state-changing requests, ASP.NET Core's
  CSRF defense. **Not implemented in Compass**: `SameSite=Lax` alone carries CSRF, on four
  preconditions recorded in ADR 0007 — break any one and the token ships. Defined here because
  ADR 0007 turns on the term. _Avoid_: CSRF token (use "antiforgery" to match the ASP.NET Core API).
- **ECS service** — The API's production compute: a standard Amazon ECS service running Fargate
  tasks in `ap-southeast-2`, behind an internet-facing ALB (ADR 0009). Replaces the earlier App
  Runner design. _Avoid_: "App Runner" (historical only), "Express Mode" (considered, rejected).
- **Origin hostname** — `api-origin.financial-compass.dev`, the fixed DNS name CloudFront sends
  `/api/*` to. It exists so the persistent stack points at a *name* rather than at the ALB *as a
  resource*, letting the ephemeral stack destroy and recreate the ALB without touching CloudFront
  (ADR 0009). Not a user-facing hostname — the SPA never calls it directly. _Avoid_: "the API
  domain" (there isn't one; see **Same-site cookie delivery**).
- **Security-group chain** — The deployed network's access model: each security group admits
  exactly one upstream — the load balancer admits CloudFront's managed origin-facing prefix list,
  the task admits the load balancer, the database admits the task — so no *ingress* rule anywhere
  names a CIDR (ADR 0009). The only CIDR in the chain is the task's egress to `0.0.0.0/0`, which
  has to be open: ECS requires a route out, and a task without one cannot start at all. The chain
  is what makes that *public-subnet* task safe. _Avoid_: "the perimeter", "the firewall" — there is no single
  boundary, which is the point.
- **Origin header** — The secret header CloudFront adds to every `/api/*` origin request, checked
  by the ALB's listener rule. The **security-group chain** admits CloudFront's *published, shared*
  edge ranges, so the prefix list proves "a CloudFront" and this proves "this distribution". The
  foundation stack generates it into Secrets Manager as `compass/cloudfront-origin-header` and the
  app stack reads it **by name**, mirroring the **origin hostname**'s decoupling. The container is
  never configured with it and nothing in the API reads it; the header does still reach the task on
  the wire, since an ALB cannot strip a request header. _Avoid_: calling it a credential — it is an
  origin lock, and its value is readable by anyone who can read the distribution's configuration.
- **Ephemeral environment** — The deployed stack is torn down between work sessions with
  `terraform destroy` and rebuilt on demand; this is the **cost lever** that makes ADR 0009's
  topology affordable, not tidiness. Splits the infrastructure in two: a *foundation* stack that
  persists (zone, certs, ECR, S3+CloudFront, VPC, state bucket) and an *app* stack that does not
  (ALB, ECS service, RDS). Teardown drops the database — and with it `AspNetUsers` and the
  **Data Protection key ring**, deliberately: their lifetimes are pinned to each other (ADR 0007).
  _Avoid_: "staging", "the environment" — say which stack.
- **ADR** — Architecture Decision Record. A short doc capturing one decision, its context, and consequences. See `docs/adr/`.

> Historical: earlier ADRs (0002/0003, superseded) used **Supabase** as the auth provider and a
> **JWT** bearer token. Auth is now .NET-native (ADR 0007) and Supabase is removed from the project.
