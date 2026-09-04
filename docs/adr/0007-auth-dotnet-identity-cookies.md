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

  > **Amended 2026-09-03 (per [ticket #12](https://github.com/wkimdkim-nz/compass/issues/12)).**
  > This bullet named the cookie but never named what encrypts it. The cookie is protected by
  > **ASP.NET Core Data Protection**, whose **key ring** is stored in the app Postgres via
  > `PersistKeysToDbContext<AppDbContext>` — `AppDbContext` also implements
  > `IDataProtectionKeyContext` — **unencrypted**, with **`SetApplicationName("Compass")`** set
  > explicitly rather than derived from the content root (a base-image or working-directory change
  > would otherwise log everyone out). The same store is used in **every environment**, local dev
  > included; key lifetime is left at the 90-day default.
  >
  > **⚠️ The key ring is deliberately non-durable, and that is not a defect.** **Its lifetime is
  > pinned to the user table's.** RDS sits in the *ephemeral* app stack (**ADR 0009**), so
  > `terraform destroy` deletes the key ring and `AspNetUsers` in the same apply. A ring that
  > *outlived* the user table would decrypt an old cookie into a `ClaimsPrincipal` whose `Guid`
  > matches no row — **ghost sessions**: `/api/auth/me` succeeds on the cookie, then the first
  > domain query returns nothing or violates the FK. **Moving the ring to S3, Parameter Store, or
  > any store outside the ephemeral stack manufactures exactly that**, which is why those were
  > rejected on *correctness*, not on cost or IAM. This coupling between this ADR and ADR 0009 is
  > invisible from either end — like the `/api/*` rule below, it is recorded in both places on
  > purpose. Revisit only if the environment stops being ephemeral (or becomes multi-tenant, which
  > also reopens the unencrypted-keys acceptance below).
  >
  > **Why Phase 0 builds this rather than deferring it:** the "no longer" half comes free, but the
  > **"no shorter" half does not**. ADR 0009's deploys run old and new tasks concurrently and its
  > migration task makes a deploy non-atomic, so two *filesystem* key rings could not read each
  > other's cookies — users bounced to the login page **silently and intermittently**, inside the
  > one feature Phase 0 exists to prove. Pinning `desired_count = 1` would also work, but forfeits
  > zero-downtime deploys, which ADR 0009 rejected Express Mode to keep.
  >
  > **Consequences for the build:**
  >
  > - The **`data_protection_keys` table is permanent** — unlike the throwaway skeleton entity of
  >   [ticket #3](https://github.com/wkimdkim-nz/compass/issues/3), it is not deleted when Phase 1
  >   starts. `EFCore.NamingConventions` snake_cases it, unlike Identity's own tables, which pin
  >   their names with explicit `ToTable`.
  > - It is created by the **one-off migration ECS task**, so
  >   [ticket #19](https://github.com/wkimdkim-nz/compass/issues/19)'s
  >   migration-before-service ordering is load-bearing for **login**, not merely for app data: a
  >   fresh environment that loses that race has broken auth, not an empty database.
  > - **DB down now means auth down.** A filesystem key ring kept cookie decryption working while
  >   the database was unreachable; this one does not. It is a further reason `/health` must not
  >   touch the database (ticket #19).
  > - **Keys sit in the table as plain XML** — anyone who can read it can mint a cookie for any
  >   user. Accepted because RDS is private (ADR 0009) and the blast radius equals direct
  >   `AspNetUsers` access. There is no first-party AWS KMS protection provider to lean on, and the
  >   certificate alternative brings its own rotation problem.
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
  >
  >    ⚠️ **This precondition binds `api-origin.financial-compass.dev`.**
  >    [Ticket #10](https://github.com/wkimdkim-nz/compass/issues/10) decoupled the *persistent*
  >    CloudFront from the *ephemeral* ALB by a DNS name: a record in the persistent hosted zone,
  >    owned by the ephemeral stack, aliasing whatever ALB currently exists. That is safe **only
  >    while the record is a Terraform resource inside the ephemeral stack**, deleted by
  >    `terraform destroy` in the same apply as the ALB it targets. A record that outlives its ALB —
  >    a partial destroy, or one created by hand outside Terraform — is a **dangling alias on a
  >    same-site subdomain**, and whoever next claims that released ELB hostname can forge fully
  >    authenticated requests: the takeover above, on our own infrastructure. Convenience is the
  >    likely route in ("keep the record so CloudFront doesn't need re-pointing"). See ADR 0009's
  >    DNS-decoupling bullet.
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

Cookie auth only works if the browser treats the SPA and API as same-site. CloudFront's default
domain and the API's own AWS-assigned domain are *different* registrable domains → cross-site → the
cookie would not attach on `SameSite=Lax`. Therefore:

- Register a **custom domain** (~$12/yr via Route 53 — also teaches Route 53 / ACM / CloudFront).
- **CloudFront path routing:** SPA served at the root; `/api/*` forwarded to the API as a **second
  origin**. Everything is same-origin → clean cookies, TLS via ACM.

  > **Amended 2026-09-02 (per [ticket #10](https://github.com/wkimdkim-nz/compass/issues/10) and
  > [ticket #21](https://github.com/wkimdkim-nz/compass/issues/21)).**
  > This section originally named **AWS App Runner** as the compute target and as the second origin.
  > App Runner has been reversed in favour of a standard **Amazon ECS service on Fargate behind an
  > internet-facing ALB** (**ADR 0009**), so the second origin is now that ALB, reached by a stable
  > DNS name (`api-origin.financial-compass.dev`) rather than a resource reference. **The routing
  > rule itself is unchanged** — only the thing on the far end of it.
  >
  > **The `/api/*` path-routing rule is load-bearing and must not be "simplified".** Now that an ALB
  > exists, serving the API from an `api.` subdomain looks tempting and *would* work for the cookie —
  > `SameSite` cares about *site*, not *origin*. But a subdomain is cross-**origin**, so the SPA's
  > JSON `POST`s would trigger a CORS preflight satisfiable only with `AllowCredentials` — which
  > breaks **precondition 2** of the CSRF amendment above and **forces antiforgery back before that
  > change ships**. This coupling is invisible from either end: nothing in the CloudFront config
  > mentions CSRF, and nothing in the CSRF amendment mentions CloudFront. It is recorded in both
  > places on purpose.
- Local dev runs on `localhost` as a **single origin** via a Vite dev proxy that forwards `/api` to
  Kestrel. No CORS policy is registered in .NET at all.

  > **Amended 2026-07-27 (per [ticket #4](https://github.com/wkimdkim-nz/compass/issues/4)).**
  > This bullet originally allowed **CORS-with-credentials for the dev port**, treating dev as
  > cross-origin-but-same-site. The Vite proxy makes dev single-origin instead, mirroring the
  > CloudFront path-routing rule above — one rule, both environments. Precondition 2 of the CSRF
  > amendment above (no CORS policy with `AllowCredentials`) depends on this.
  >
  > **Amended 2026-09-04 (per [ticket #27](https://github.com/wkimdkim-nz/compass/issues/27),
  > implementing [ticket #4](https://github.com/wkimdkim-nz/compass/issues/4)).** Local
  > development is plain HTTP, so the `Secure` flag named in the session-cookie bullet above is
  > environment-conditional: `SecurePolicy = SameAsRequest` in Development and `Always` in
  > Production. `HttpOnly` and `SameSite=Lax` hold in both. The HTTP seam tests assert both
  > shapes from the raw `Set-Cookie` header.

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
