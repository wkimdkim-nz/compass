# Compass

A beginner-facing stock-projection calculator. This repository holds the **Phase 0 walking
skeleton**: register, sign in with an `HttpOnly` session cookie, and round-trip one row of your
own through the API to Postgres. There is no calculator yet — see [`docs/roadmap.md`](docs/roadmap.md).

Read [`docs/glossary.md`](docs/glossary.md) for the project's vocabulary and [`docs/adr/`](docs/adr/)
for the decisions the code below implements.

## Layout

| Path | What it is |
|---|---|
| `api/` | .NET 10 Web API (`Compass.Api`, minimal APIs) plus its xUnit integration tests. SDK pinned in `global.json`, `dotnet-ef` pinned in `.config/dotnet-tools.json`. |
| `web/` | Vite + React + TypeScript SPA with Redux Toolkit / RTK Query. Node version pinned in `.nvmrc`. |
| `docker-compose.yml` | Local Postgres 18. |

## Running it locally

You need Docker, the .NET 10 SDK and Node 26 (`nvm use` inside `web/` picks the pinned version).
Three commands in three terminals, plus a one-off migration:

**1. Postgres**

```bash
docker compose up -d --wait
```

**2. Apply migrations, then run the API** (from `api/Compass.Api`)

```bash
dotnet ef database update
```

```bash
dotnet run
```

Migrations are applied by that explicit command and never on application startup, so the schema
only changes when you say so. The API listens on <http://localhost:5111>.

**3. The SPA** (from `web/`)

```bash
npm install && npm run dev
```

Open <http://localhost:5173>. Vite forwards `/api` to Kestrel unrewritten, so the SPA and the API
are one origin and the browser attaches the session cookie by itself: the SPA sends no token, no
antiforgery header and no `credentials: "include"`. Vite is configured with `strictPort`, so if
5173 is taken it fails instead of silently moving and breaking that arrangement.

The local connection string is committed in `api/Compass.Api/appsettings.Development.json`.
Production supplies its own through the normal configuration chain.

To throw the database away: `docker compose down -v`.

## Tests

Two seams, one command each. Neither shares state between tests, and neither uses an in-memory
database substitute.

**API — the HTTP seam.** xUnit tests drive the real app in-process through
`WebApplicationFactory` against a real Postgres started by Testcontainers, so Docker must be
running. Each test class gets its own container and a schema built by the committed migrations.

```bash
cd api && dotnet test
```

**SPA — the rendered-app seam.** Vitest renders the real routes and the real store, with MSW
standing in for `/api/*`. Nothing in the app is mocked.

```bash
cd web && npm test
```

`npm run typecheck` runs the TypeScript compiler on its own.

## What the API exposes

All under `/api`, JSON bodies only, ProblemDetails on every error.

| Endpoint | Success | Notes |
|---|---|---|
| `POST /api/auth/register` | 201, `{ id, email }` | Signs the new user in. Password: at least 8 characters, no composition rules. Field errors under `errors`. |
| `POST /api/auth/login` | 200, `{ id, email }` | Any failure is the same generic 401. Repeated failures lock the account. |
| `POST /api/auth/logout` | 204 | Ends the session on the server. |
| `GET /api/auth/me` | 200, `{ id, email }` | The session check. 401 (no redirect) when signed out. |
| `GET /api/notes` | 200, `[note]` | Only the caller's rows, newest first. |
| `POST /api/notes` | 201, `note` | `{ text }`, attributed to the caller. |

`Note` is a **throwaway** placeholder that exists only to prove the user-scoped round-trip. It is
deleted when Phase 1's first real entity lands. The `data_protection_keys` table is **not**
throwaway: it holds the key ring that encrypts the session cookie and must outlive `Note`.

## Decisions recorded at build time

The Phase 0 spec ([#27](https://github.com/wkimdkim-nz/compass/issues/27)) left two calls open.
Both are closed here:

- **Identity's table names stay mixed-case** (`"AspNetUsers"` and friends). `EFCore.NamingConventions`
  snake-cases every column, index and constraint, and every table the app itself declares
  (`notes`, `data_protection_keys`), but `IdentityDbContext` pins its own table names. We accept
  that rather than re-map seven tables: every ADR already says `AspNetUsers`. The `notes.user_id`
  foreign key targets `"AspNetUsers".id`.
- **Guid v7 everywhere.** EF Core already generates v7 keys; hand-created ids use
  `Guid.CreateVersion7()` so nothing in the database is v4.
