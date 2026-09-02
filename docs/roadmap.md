# Roadmap — Compass

Ordered build plan. The guiding principle (grilling round 4): **prove the seams end-to-end before
adding features.** Four technologies are stacked here (React, .NET, Postgres/AWS, FMP) — the risk is
building each in isolation and discovering the joints (auth, same-site delivery, deploy) break late. Auth is
.NET-native (ASP.NET Core Identity, ADR 0007); Supabase has been dropped.

## Phase 0 — Walking skeleton (de-risk the seams)

The thinnest possible slice that touches every layer. **No calculator, no FMP.**

- [ ] React app: register + log in against ASP.NET Core Identity; receive an `HttpOnly` session
      cookie (ADR 0007). No client-side token store. **No email path at all** — no `IEmailSender` is
      registered; confirmation not required (ADR 0007, amended).
- [ ] Frontend calls one authenticated endpoint on the .NET API; the browser attaches the cookie
      automatically.
- [ ] `.NET`: Identity cookie auth resolves the caller; `GET /ping` returns the caller's user id.
- [ ] One authenticated write + read of a single row in the app Postgres, `user_id` FK'd to
      `AspNetUsers` (ADR 0008), in the *same* database as Identity's tables.
- [ ] Deploy on AWS (`ap-southeast-2`): React → S3 + CloudFront; .NET → **Amazon ECS on Fargate**
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

## Phase 1 — The calculator (first real feature)

- [ ] Ticker lookup → .NET calls FMP (key in Secrets Manager), returns base facts + history (ADR 0005).
- [ ] Pre-fill Bear/Base/Bull from FMP history; all fields editable (ADR 0004).
- [ ] Live projection compute in TS as the user types; authoritative recompute in .NET on save.
- [ ] Save / load a projection (assumptions only; base facts re-fetched live on load).
- [ ] "Not financial advice" disclaimer by the output and in the footer (ADR 0006).

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
- [ ] Polish, error/rate-limit handling for FMP, caching.

## Reference — calculator inputs & sources

See [`glossary.md`](glossary.md) for the formula and the input→source table that also drives the
Phase 2 education page.
