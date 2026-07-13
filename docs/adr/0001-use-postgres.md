# ADR 0001 — Use PostgreSQL from day one (skip MySQL)

**Status:** Accepted
**Date:** 2026-07-01

## Context

The project needs a relational database. Options considered: start on MySQL and migrate to Postgres later, or go straight to Postgres. Supabase (chosen for auth) is itself a managed Postgres.

## Decision

Use **PostgreSQL** from the start. Do not begin on MySQL.

## Rationale

- "Start on MySQL, migrate later" teaches you *migration pain*, not database features. For a learning project the migration is pure waste.
- Supabase **is** Postgres. Using Postgres means one SQL dialect and one mental model across auth and data — and leaves open the option of putting your app tables in the same Supabase Postgres instead of a second database.
- Postgres has features you'll actually want here: `JSONB` (natural fit for storing an assumption-set snapshot), `numeric` for money math without float error, richer constraints, generated columns.
- On AWS the managed equivalent is RDS for PostgreSQL / Aurora Serverless v2 — both first-class.
- From .NET, use **Npgsql** with EF Core; the tooling is mature.

## Consequences

- Money/valuation fields use `numeric`/`decimal`, never `float`/`double`.
- Open question deferred to a later ADR: **one database or two?** (Supabase Postgres for both auth + data, vs. Supabase for auth only + a separate RDS Postgres owned by .NET.)
