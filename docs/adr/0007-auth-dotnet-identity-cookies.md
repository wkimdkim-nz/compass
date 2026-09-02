# ADR 0007 — .NET owns identity via ASP.NET Core Identity (cookie sessions)

**Status:** Accepted
**Date:** 2026-07-14
**Supersedes:** ADR 0002

## Context

ADR 0002 made Supabase the identity provider (it issued a JWT; .NET only validated it) and was
written to be *provider-swappable, config-only* — with AWS Cognito named as the future swap. We are
reversing that. The driving goal is to **learn .NET deeply**: owning authentication — password
hashing, token/session issuance, the auth middleware pipeline — is the natural extension of ADR
0003's "learn .NET as a real backend that owns its data." If .NET only validates someone else's
token, that learning never happens.

Managed IdPs (Cognito, Auth0, Entra) are the more common *production* default ("don't roll your own
auth"), and Cognito is the AWS-native one. We are deliberately choosing the deeper-learning path
over the industry-default path, with eyes open (see the swap note in Consequences).

## Decision

**.NET is the identity provider**, using **ASP.NET Core Identity**:

- **User store** via EF Core (Npgsql) in the app Postgres. Password hashing is Identity's default
  (PBKDF2). We do not hand-roll crypto.
- **Session model: HttpOnly cookie**, not bearer JWT. The cookie is `HttpOnly; Secure;
  SameSite=Lax` — no token is reachable from JavaScript, closing the XSS token-theft class.
- **CSRF defense: `SameSite=Lax` alone.** No antiforgery token is implemented in Phase 0.

  > **Amended 2026-07-27 (reversal, per [ticket #6](https://github.com/wkimdkim-nz/compass/issues/6)).**
  > This bullet originally required an ASP.NET Core **antiforgery token** (cookie-to-header) on
  > state-changing requests, layered on top of `SameSite=Lax`. That layer is dropped.
  >
  > `SameSite=Lax` blocks the cookie on every cross-site POST/PUT/DELETE, and also refuses to
  > *store* a `Set-Cookie` returned to one — so classic CSRF and login CSRF are both closed at the
  > browser. Compass additionally accepts only JSON request bodies and registers **no CORS policy
  > at all** (ADR 0007's dev-CORS note is superseded by
  > [ticket #4](https://github.com/wkimdkim-nz/compass/issues/4) — dev is single-origin via the
  > Vite proxy), so a cross-origin `fetch` is stopped by preflight and a preflight-free content
  > type is answered with 415. No exploit was constructible against this configuration.
  >
  > **This holds only while all four conditions below hold. If any one changes, antiforgery must be
  > implemented before that change ships:**
  >
  > 1. **No subdomain serves third-party or user-controlled content.** `SameSite` treats every
  >    `*.financial-compass.dev` subdomain as same-site, so a subdomain — including one CNAME'd to
  >    a SaaS whose account later lapses ([subdomain takeover](https://owasp.org/www-project-web-security-testing-guide/latest/4-Web_Application_Security_Testing/02-Configuration_and_Deployment_Management_Testing/10-Test_for_Subdomain_Takeover)) —
  >    can forge fully authenticated requests. A subdomain serving *our own* content (e.g. `www`
  >    pointing at the same CloudFront distribution) is fine. This stopped being hypothetical when
  >    the domain was registered ([ticket #9](https://github.com/wkimdkim-nz/compass/issues/9)):
  >    the zone is live, so the surface is real rather than illustrative.
  > 2. **No CORS policy with `AllowCredentials`** for any origin.
  > 3. **No endpoint mutates on GET** — `Lax` permits the cookie on top-level GET navigation.
  > 4. **Request bodies are JSON only** — a `multipart/form-data` or form-encoded endpoint is
  >    preflight-free and would be reachable from a same-site origin.
  >
  > Note that conditions 2–4 are ordinary engineering choices, not security controls: nothing fails
  > loudly if one is changed. That is the accepted risk of this reversal.
- **User key type: `Guid`** (`IdentityUser<Guid>`), so `AspNetUsers.Id` is a native Postgres
  `uuid` — consistent with "identity is a uuid" and with the domain `user_id` columns.
- **Registration is open** (public self-signup); abuse controls (rate limit / CAPTCHA) are a later
  hardening item, not day one.
- **Email is deferred:** Phase 0 has **no email path at all** — no `IEmailSender` is registered, and
  `RequireConfirmedEmail = false`. Real **AWS SES** + required confirmation is a later phase (SES
  verifies the sender via the custom domain from ADR 0008's deployment note).

  > **Amended 2026-07-26 (correction, per [ticket #5](https://github.com/wkimdkim-nz/compass/issues/5)).**
  > This bullet originally called for "a no-op `IEmailSender` that logs the confirmation/reset link to
  > the console." That requirement was an artefact of assuming `MapIdentityApi`, whose `/register`
  > sends a confirmation email and throws without a sender registered. Phase 0's auth endpoints are
  > hand-rolled over `UserManager`/`SignInManager` and invoke no email flow, so a registered sender
  > would have zero callers. The intent — defer real email to a later phase — is unchanged; only the
  > named mechanism was wrong.

**Supabase is removed from the project entirely.** It was only ever auth; app data was already
independent (ADR 0003). Dropping Supabase auth drops Supabase.

## Deployment consequence: same-site required

Cookie auth only works if the browser treats the SPA and API as same-site. CloudFront's and App
Runner's default domains are *different* registrable domains → cross-site → the cookie would not
attach on `SameSite=Lax`. Therefore:

- Register a **custom domain** (~$12/yr via Route 53 — also teaches Route 53 / ACM / CloudFront).
- **CloudFront path routing:** SPA served at the root; `/api/*` forwarded to App Runner as a
  second origin. Everything is same-origin → clean cookies, TLS via ACM.
- Local dev runs on `localhost` as a **single origin** via a Vite dev proxy that forwards `/api` to
  Kestrel. No CORS policy is registered in .NET at all.

  > **Amended 2026-07-27 (per [ticket #4](https://github.com/wkimdkim-nz/compass/issues/4)).**
  > This bullet originally allowed **CORS-with-credentials for the dev port**, treating dev as
  > cross-origin-but-same-site. The Vite proxy makes dev single-origin instead, mirroring the
  > CloudFront path-routing rule above — one rule, both environments. Precondition 2 of the CSRF
  > amendment above (no CORS policy with `AllowCredentials`) depends on this.

## Consequences

- Roadmap Phase 0 auth bullets change: register/log in via Identity → receive an HttpOnly cookie →
  authenticated call carries the cookie automatically (no `Bearer` header, no client token store).
- The user's identity inside our domain is `AspNetUsers.Id` (a `uuid`). See ADR 0008 for how domain
  rows reference it.
- **The Cognito swap is no longer config-only.** Choosing cookies (and the ADR 0008 user FK) trades
  cheap swappability for security + depth. A future Identity→Cognito migration is a *moderate*
  exercise — flip cookie→bearer middleware on both ends, build a Cognito migration Lambda to move
  password-backed users, and re-map `user_id` from `AspNetUsers.Id` to the new Cognito `sub`
  (reverting to loose uuid keying). This is kept in the roadmap "Later" section as a deliberate
  AWS-native learning exercise, sequenced *after* Identity so each builds on the last.
