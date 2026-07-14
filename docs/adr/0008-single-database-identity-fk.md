# ADR 0008 — One database: identity and domain data co-located, joined by a real user FK

**Status:** Accepted
**Date:** 2026-07-14
**Supersedes:** ADR 0003

## Context

ADR 0003 split data across **two databases by ownership**: Supabase owned identity (`auth.users`),
.NET owned domain data, and every domain row was keyed by the Supabase `sub` with **no
cross-database foreign key** — the "no FK" rule existed *precisely because* identity lived in a
database .NET did not control.

That premise is gone. ADR 0007 makes .NET the identity provider via ASP.NET Core Identity, which
stores its users (`AspNetUsers`, …) via EF Core in **our** Postgres — the same database as
`projections` and `scenarios`. "Two databases" collapses to one, and the reason for banning a
foreign key disappears.

## Decision

- **One database.** Identity tables and domain tables live together in the app Postgres, managed
  with EF Core migrations (Npgsql). There is no separate identity database.
- **Real foreign key:** `projections.user_id → AspNetUsers.Id`, **`ON DELETE CASCADE`**. Deleting a
  user automatically removes their projections — which resolves ADR 0003's "we handle user-deletion
  cleanup ourselves" burden. Integrity is DB-enforced, not convention.
- **`user_id` stays a `uuid`**, matching `AspNetUsers.Id` (`Guid` key, ADR 0007).

## Hosting (unchanged from ADR 0003)

"One database" is a decision about **ownership/co-location, not hosting.** Host on **AWS RDS for
PostgreSQL** during the learning phase; **migrate to Neon** when the RDS free tier lapses. Adding
the Identity tables does not change this — they migrate with everything else (`pg_dump | pg_restore`
+ `dotnet ef database update`).

## Consequences

- Schema: `AspNetUsers` (+ Identity's other tables) alongside `projections` (1) → `scenarios` (≤3);
  `projections.user_id` FKs to `AspNetUsers.Id` with cascade delete.
- Money/valuation columns remain `numeric`/`decimal`, never floating point (unchanged).
- The FK **couples the domain schema to Identity's schema.** That is an accepted cost of owning
  identity (ADR 0007). It is also what makes a future Cognito swap a data migration rather than a
  config change: the FK target (`AspNetUsers`) would be dropped and rows re-keyed to the Cognito
  `sub`, reverting to ADR 0003's loose-uuid model. See ADR 0007's swap note.
