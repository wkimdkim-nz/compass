# Roadmap — Compass

Ordered build plan. The guiding principle (grilling round 4): **prove the seams end-to-end before
adding features.** Four technologies are stacked here (React, .NET, Postgres/AWS, market data) — the risk is
building each in isolation and discovering the joints (auth, same-site delivery, deploy) break late. Auth is
.NET-native (ASP.NET Core Identity, ADR 0007); Supabase has been dropped.

## Phase 0 — Walking skeleton (de-risk the seams)

The thinnest possible slice that touches every layer. **No calculator, no market data.**

- [x] React app: register + log in against ASP.NET Core Identity; receive an `HttpOnly` session
      cookie (ADR 0007). No client-side token store. **No email path at all** — no `IEmailSender` is
      registered; confirmation not required (ADR 0007, amended).
- [x] Frontend calls one authenticated endpoint on the .NET API; the browser attaches the cookie
      automatically.
- [x] `.NET`: Identity cookie auth resolves the caller; `GET /api/auth/me` returns the caller's user
      id. There is no authenticated `/ping` — `/api/auth/me` is the session check the `HttpOnly`
      cookie makes necessary (ADR 0007).
- [x] One authenticated write + read of a single row in the app Postgres, `user_id` FK'd to
      `AspNetUsers` (ADR 0008), in the *same* database as Identity's tables.
- [x] Deploy on AWS (`ap-southeast-2`): React → S3 + CloudFront; .NET → **Amazon ECS on Fargate**
      behind an internet-facing ALB (ADR 0009 — App Runner was reversed); DB → RDS Postgres, kept
      **private**. **Register a custom domain (Route 53); CloudFront serves the SPA at root and
      path-routes `/api/*` to the ALB so the cookie is same-site** (ADR 0007) — an `api.` subdomain
      would be cross-*origin* and is ruled out, see ADR 0009. Provisioned by **Terraform** as an
      **ephemeral environment** (`terraform destroy` between sessions is the cost lever); migrations
      run as a one-off ECS task in the deploy pipeline. RDS owns the master password in **Secrets
      Manager**; TLS via ACM — **two certs**, `us-east-1` for CloudFront and `ap-southeast-2` for
      the ALB.

**Done when:** a logged-in user, in the deployed app, round-trips data through .NET to RDS. Every
joint (cookie auth, same-site cookie delivery — Vite proxy in dev, CloudFront path-routing in prod,
TLS, deploy, secrets) is proven.

> ✅ **Met on 2026-09-08** ([#46](https://github.com/wkimdkim-nz/compass/issues/46)). A human ran
> the manual pass — register, log in, write a row, read it back, log out — in a real browser at
> <https://financial-compass.dev> and reported it working. It ran against an **app stack applied
> from empty** that same day, because the failure this box exists to catch (the migration task
> losing its race with the service, which presents as broken *login* rather than as a missing
> table) only exists against a fresh database, so a green run against a stack that had been up for
> hours would have proved less. `scripts/smoke-deployed.sh` passed all nine assertions against that
> same stack first.
>
> **Phase 0 is therefore complete, and the app stack it was proven on no longer exists** — it was
> destroyed the same day, which is the cost lever and not a regression. What persists is the
> foundation stack and the SPA it serves; see
> [A work session, start to finish](../infra/README.md#a-work-session-start-to-finish) to stand the
> rest back up.

## Phase 1 — The calculator (first real feature)

Decisions from the grilling of 2026-09-17: market data is **SEC EDGAR + Finnhub, US domestic
filers only** (ADR 0010, superseding 0005); one projection per user per ticker, in-memory
cache, diluted weighted-average shares, exact seeding rules (ADR 0004, amended). The earlier draft
tickets #54–#63 are closed; spec issue #65 replaces them and carries this order, broken into
build tickets #66–#77 (sub-issues of #65 with native blocked-by edges).

Ordered. Each item is one build ticket; **the design task blocks the token set, the UI and the
deploy, so the tokens are built from the chosen look.**

1. [x] **Test CI.** GitHub Actions runs `dotnet test` and Vitest on every PR. No AWS, no
       secrets. Deploy CI via OIDC is a Later item.
2. [ ] **Shared projection vectors.** `spec/projection-vectors.json`: hand-computed cases from the
       glossary formula, P/E and P/S paths, edge cases (0% growth, buybacks, 1-year horizon, zero
       margin on P/S), tolerance in the header. The glossary points at it.
3. [ ] **Two calculators.** Pure functions, double on both sides: .NET (authoritative on save)
       and TypeScript (live preview), each passing every vector. Invalid inputs rejected
       explicitly, never NaN.
4. [ ] **Tailwind + design tokens.** Tailwind via the Vite plugin, our own token set (spacing,
       colour, numeric type), light and dark, built from the design's token values. Groundwork
       only; no page changes. **Blocked by 5.**
5. [ ] **Design.** Decide the look of the landing page and the calculator page before any UI is
       built: mobile-first, phone / tablet / desktop; the calculator is a **table** (rows =
       inputs, columns = Bear / Base / Bull, base facts above, outputs as the last rows, **X** as
       the headline with CAGR and future price beneath, the disclaimer under the output row); the
       landing page is a ticker box and one line on what Compass does. Candidates are generated
       with agent skills the user adds for the purpose. **Blocks 4, 10 and 11.**
6. [ ] **Configuration.** Finnhub key: `compass/finnhub-api-key` in the **foundation** stack,
       value set by hand, injected by name in the app-stack task definition; locally
       `dotnet user-secrets`. EDGAR User-Agent contact: an environment variable everywhere. With
       either absent the matching stub is registered **in Development and test only**; elsewhere
       startup fails naming the missing setting. Record the config key names.
7. [ ] **EDGAR normalisation.** Ticker → CIK via the SEC ticker file; `companyfacts` →
       TTM revenue (ordered tag list per period, Q4 = FY − 9M), TTM net income, current margin,
       diluted weighted-average shares, 3–5 years of annual history, figures-to date. 422 reasons
       for filers it cannot normalise. Unit-tested on recorded `companyfacts` fixtures. The
       largest single item.
8. [ ] **Ticker lookup.** `GET /api/tickers/{symbol}` behind two ports (fundamentals, quote) and
       one lookup service; Finnhub adapter; stubs; in-memory cache (quote 15 min, fundamentals
       and ticker map 24 h); 404 / 422 / 503 / partial-200 exactly as ADR 0010 states; seeded
       Bear/Base/Bull per ADR 0004's amended rules, returned with the lookup.
9. [x] **Projection persistence.** `projections` (unique on user + ticker; CIK, company name) →
       `scenarios` (≤3); no base facts stored. `POST` creates (409 if exists), `PUT /{ticker}`
       replaces (404 if none), both recompute in .NET and return outputs; `GET /{ticker}`
       returns saved assumptions plus live base facts, or empty base facts and the lookup's
       reason whenever the lookup cannot seed (unknown ticker, unsupported filer, provider
       down); this route's own 404 means only "no saved projection". Owner-only.
       **Retire `Note`** end to end in this ticket, smoke assertions rewritten here.
10. [x] **Landing + calculator UI.** `/` landing with the ticker box; `/projections/{ticker}` the
        calculator: pre-fill from the lookup, saved assumptions loaded over fresh facts when a
        projection exists (visibly marked, reset-to-defaults action), live X/CAGR on every
        keystroke, Save for new / Update with a confirmation modal for existing, figures-to
        date beside the base facts, whole-number percents and millions in the UI (decimals and
        raw values on the wire), P/E vs P/S per scenario, the disclaimer beside the output and
        in the footer (ADR 0006), 404 / 422 / 503 messages that leave the form usable.
11. [ ] **Deploy + smoke.** Foundation secret applied, task definition injects it, EDGAR contact
        env var set; `scripts/smoke-deployed.sh` gains lookup → save → load against the **real**
        providers on AAPL; full work session (push, apply, migrate, smoke, browser pass, destroy).
12. [ ] **Close out.** Roadmap ticked, glossary and ADRs reconciled with what was built, the
        spec issue closed with pointers to the PRs.

**Done when:** a logged-in user, in the deployed app, types a US ticker, gets a pre-filled
Bear/Base/Bull form, edits it, sees X and CAGR update live, saves, reloads and sees the same
assumptions with fresh base facts — disclaimer beside the output and in the footer — and the
smoke script proves lookup → save → load against the real providers.

## Phase 2 — Make it usable for beginners

- [ ] Education / help page: each input, what it means, where to find it (see glossary inputs table).
- [ ] Projection list; edit; delete.

## Later

- [ ] Compare Bear/Base/Bull (and multiple projections) side by side.
- [ ] Wire real email: **introduce** `IEmailSender` (Phase 0 registers none) with an AWS SES
      implementation, verify the domain (Route 53 DKIM), require confirmed email + enable password
      reset (ADR 0007).
- [ ] Abuse controls on open registration: rate limiting / CAPTCHA (ADR 0007).
- [ ] Swap Identity → AWS Cognito as a deliberate AWS-auth exercise. Note: with cookie sessions +
      the user FK this is a *moderate migration*, not config-only — flip cookie→bearer on both ends,
      add a Cognito migration Lambda, and re-map `user_id` to the Cognito `sub` (ADR 0007/0008).
- [ ] Migrate app DB RDS → Neon when the free tier lapses (ADR 0008).
- [ ] NZX / ASX ticker lookup — no free source found (research notes, 2026-09-13/14); needs a
      research spike and probably a paid provider before it is a ticket.
- [ ] Deploy CI: build and push the image from GitHub Actions via OIDC role assumption.
- [ ] Postgres-backed market-data cache, if the in-memory one ever proves insufficient.
- [ ] Choose a component library, if the Phase 2 pages outgrow Tailwind + tokens.

## Reference — calculator inputs & sources

See [`glossary.md`](glossary.md) for the formula and the input→source table that also drives the
Phase 2 education page.
