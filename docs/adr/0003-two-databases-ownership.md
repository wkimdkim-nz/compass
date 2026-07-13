# ADR 0003 — .NET owns its own Postgres (two databases, keyed by Supabase user id)

**Status:** Accepted
**Date:** 2026-07-01

## Context

Supabase (chosen for auth, ADR 0002) is itself a managed Postgres, so app data *could* live there.
But a core goal is to learn .NET as a real backend that owns its data. If .NET merely CRUDs
Supabase's Postgres, the backend adds nothing over Supabase's auto-generated API.

## Decision

Use **two databases**, split by *ownership*:

- **Supabase Postgres** — identity only (`auth.users` etc.). Owned by Supabase.
- **App Postgres** — all domain data (projections, scenarios, snapshots). Owned by the .NET API,
  managed with **EF Core migrations** using **Npgsql**.

Every domain row is keyed by a `uuid user_id` column = the Supabase `sub` claim (ADR 0002). There
is **no** cross-database foreign key into `auth.users`.

"Two databases" is a decision about **ownership, not hosting.** The app Postgres may be hosted on
AWS RDS, Neon, or a second Supabase project — swappable without changing the architecture.

## Decision on hosting

Host the app Postgres on **AWS RDS for PostgreSQL** during the learning phase, because "get familiar
with AWS" is an explicit project goal (teaches VPC, security groups, Secrets Manager for the
connection string).

**Post-free-tier migration target: Neon.** When RDS's 12-month free tier lapses, migrate the app
Postgres to **Neon** (serverless Postgres, scales to zero → an idle side project effectively costs
$0; free tier ~0.5 GB is expected to be sufficient here). Runner-up: a second Supabase project (one
fewer vendor, but free projects pause after ~1 week idle). The migration is a hosting swap, not a
rewrite: repoint the connection string, run `dotnet ef database update`, and `pg_dump | pg_restore`
existing rows.

## Consequences

- Accept RDS cost after the 12-month free tier (mitigate by stop/start, or migrate the DB later —
  the ownership boundary makes that a hosting swap, not a rewrite).
- Money/valuation columns are `numeric`/`decimal`, never floating point.
- User deletion: we own the mapping from `user_id` to their projections; deleting a Supabase user
  does not cascade to us — we handle cleanup ourselves.
