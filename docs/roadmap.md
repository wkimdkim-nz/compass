# Roadmap — Compass

Ordered build plan. The guiding principle (grilling round 4): **prove the seams end-to-end before
adding features.** Five technologies are stacked here (React, .NET, Supabase, Postgres/AWS, FMP) —
the risk is building each in isolation and discovering the joints (auth, CORS, deploy) break late.

## Phase 0 — Walking skeleton (de-risk the seams)

The thinnest possible slice that touches every layer. **No calculator, no FMP.**

- [ ] React app: log in via the Supabase client SDK; obtain a JWT.
- [ ] Frontend calls one authenticated endpoint on the .NET API with the JWT as `Bearer`.
- [ ] `.NET`: validate the Supabase JWT (ADR 0002), read `sub`, return `GET /ping` = the caller's id.
- [ ] One authenticated write + read of a single row in the app Postgres, keyed by `sub` (ADR 0003).
- [ ] Deploy all of it on AWS: React → S3 + CloudFront; .NET → App Runner; DB → RDS Postgres.
      Connection string + secrets in AWS Secrets Manager.

**Done when:** a logged-in user, in the deployed app, round-trips data through .NET to RDS. Every
joint (auth, CORS, TLS, deploy, secrets) is proven.

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
- [ ] Swap Supabase → AWS Cognito as a deliberate AWS-auth exercise (ADR 0002 makes this config-only).
- [ ] Migrate app DB RDS → Neon when the free tier lapses (ADR 0003).
- [ ] Polish, error/rate-limit handling for FMP, caching.

## Reference — calculator inputs & sources

See [`glossary.md`](glossary.md) for the formula and the input→source table that also drives the
Phase 2 education page.
