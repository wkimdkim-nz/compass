# Architecture Decision Records

Short records of the significant decisions for Compass, each with its context and consequences.
Newest supersede older; check a record's **Status** before relying on it.

| # | Decision | Status |
|---|---|---|
| [0001](0001-use-postgres.md) | Use PostgreSQL from day one (skip MySQL) | Accepted |
| [0002](0002-auth-supabase-jwt.md) | Supabase issues identity; .NET validates the JWT | Superseded by 0007 |
| [0003](0003-two-databases-ownership.md) | .NET owns its own Postgres (RDS → Neon after free tier) | Superseded by 0008 |
| [0004](0004-scenario-model.md) | Bear/Base/Bull scenarios; live-fetch persistence | Accepted |
| [0005](0005-market-data-fmp.md) | Financial Modeling Prep as market-data provider | Superseded by 0010 |
| [0006](0006-not-financial-advice.md) | "Not financial advice" disclaimer | Accepted |
| [0007](0007-auth-dotnet-identity-cookies.md) | .NET owns identity via ASP.NET Core Identity (cookie sessions) | Accepted |
| [0008](0008-single-database-identity-fk.md) | One database: identity + domain co-located, real user FK | Accepted |
| [0009](0009-compute-ecs-fargate.md) | The API runs on Amazon ECS (Fargate) behind an ALB, not App Runner | Accepted |
| [0010](0010-market-data-edgar-finnhub.md) | SEC EDGAR for fundamentals, Finnhub for the quote; US domestic filers only | Accepted |
| [0011](0011-ui-figure-precision.md) | Percent fields keep one decimal; the current net margin is printed, not typed | Accepted |

See also: [`../glossary.md`](../glossary.md) (shared domain language) and
[`../roadmap.md`](../roadmap.md) (ordered build plan).

## Format

Each ADR: **Title · Status · Date · Context · Decision · Consequences.** One decision per file.
To change a past decision, add a new ADR that supersedes it rather than rewriting history.
