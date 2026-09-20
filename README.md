# Compass

A beginner-facing stock-projection calculator. This repository holds the **Phase 0 walking
skeleton**: register, sign in with an `HttpOnly` session cookie, and round-trip one row of your
own through the API to Postgres. There is no calculator yet — see [`docs/roadmap.md`](docs/roadmap.md).

Read [`docs/glossary.md`](docs/glossary.md) for the project's vocabulary and [`docs/adr/`](docs/adr/)
for the decisions the code below implements.

## The deployed app

<https://financial-compass.dev>

**Expect to find it half-up.** The site is deliberately split so that the free half runs
permanently and the half that costs money does not: CloudFront and S3 serve the SPA all the time,
while the API, its load balancer and the database exist only while someone is working on the
project. That is what **ephemeral** means here — the expensive stack is created at the start of a
work session and `terraform destroy`ed at the end, which at roughly ten hours of use a week is what
turns ~$58/month into ~$3.50 (ADR 0009), and it is a premise of the auth design rather than only a cost trick: the session key
ring is meant to die with the user table (ADR 0007).

So between sessions the page loads, and then registering or signing in fails — CloudFront answers
`/api/*` with an origin error because there is nothing behind that name. **That is the destroyed
state working as designed, not an outage**, and nothing is wrong with the deployment. Bringing it
back is a handful of commands in [`infra/README.md`](infra/README.md#a-work-session-start-to-finish).

There is no public sign-up worth having yet: Phase 0 is a walking skeleton, so the only thing to do
once signed in is write one line of text and read it back.

## Layout

| Path | What it is |
|---|---|
| `api/` | .NET 10 Web API (`Compass.Api`, minimal APIs) plus its xUnit integration tests. SDK pinned in `global.json`, `dotnet-ef` pinned in `.config/dotnet-tools.json`. |
| `api/Dockerfile` | The runtime image ECS runs in production, carrying the app and a migration bundle. |
| `web/` | Vite + React + TypeScript SPA with Redux Toolkit / RTK Query, styled with Tailwind from one token file (see [`web/README.md`](web/README.md)). Node version pinned in `.nvmrc`. |
| `docker-compose.yml` | Local Postgres 18. |
| `infra/` | Terraform. Two stacks split along the billing line — see [`infra/README.md`](infra/README.md). |
| `scripts/` | Bootstrapping, publishing and verifying the deployed stacks. |

## Running it locally

You need Docker, the .NET 10 SDK and Node 26 (`nvm use` inside `web/` picks the pinned version).
Three commands in three terminals, plus two one-off setup commands:

**1. Postgres**

```bash
docker compose up -d --wait
```

**2. Apply migrations, then run the API** (from `api/Compass.Api`)

`dotnet ef` is a local tool pinned in `api/.config/dotnet-tools.json`, not a global install, so a
fresh clone has to restore it once. Skip this and `dotnet ef` is simply not a command:

```bash
dotnet tool restore
```

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

### The market-data providers (optional locally)

The ticker lookup reaches two providers, and each takes one setting (ADR 0010; the key names are
recorded in [`infra/README.md`](infra/README.md#the-two-market-data-settings)). **Neither is in
source, and locally neither is required**: in Development an absent setting puts a stub behind that
port, and the API logs a warning at startup saying which stubs are in use. Anywhere else it refuses
to start and names the missing setting.

The **Finnhub API key** comes from `dotnet user-secrets`, which keeps it outside the repository and
is read in Development only. From `api/Compass.Api`, with a free key from <https://finnhub.io>:

```bash
dotnet user-secrets set "Finnhub:ApiKey" "<your key>"
```

The **EDGAR contact address** — SEC EDGAR refuses requests whose User-Agent carries none — is an
environment variable in every environment. Export it in the shell that runs `dotnet run`:

```bash
export Edgar__ContactEmail=<you@example.com>
```

`dotnet user-secrets remove "Finnhub:ApiKey"` and `unset Edgar__ContactEmail` go back to the stubs.
The test suite pins both absent and never calls a provider, whatever the machine holds.

To throw the database away: `docker compose down -v`.

## The container image

Production runs the API from a container image on ECS ([#28](https://github.com/wkimdkim-nz/compass/issues/28)),
not from `dotnet run`. Nothing above changes: the image is a second way to run the same code, and
the local loop is still the SDK.

```bash
docker build -t compass-api api
```

Three things about the image are load-bearing in the deployed stack and invisible from the AWS end:

- **It listens on 8080**, the port the .NET runtime image already defaults to, and nothing
  overrides it. The load balancer's target group depends silently on that number.
- **It registers no HTTPS redirection.** The load balancer reaches the task over plain HTTP, so a
  redirect would bounce every health check into a 307 loop.
- **The app is the image's `CMD`, not its `ENTRYPOINT`**, so a `command` override replaces it
  outright. That is how the migration task runs (below).

`GET /api/health` answers 200 without opening a database connection — shallow on purpose, because a
deep check turns a database fault into an endless kill-and-replace loop and makes the migration
race unrecoverable. Note the prefix: ADR 0007 and [#28](https://github.com/wkimdkim-nz/compass/issues/28)
both write this path as `/health`, but CloudFront routes only `/api/*` to the origin, so the target
group and the deployed smoke test must both name `/api/health`.

### How it takes its database credentials

RDS owns the master password, so it exists only as a two-key Secrets Manager secret and never
enters Terraform state. The task therefore receives its credentials in three pieces, and the app
joins them at startup through **one unconditional code path** —
[`DatabaseConnectionString`](api/Compass.Api/Data/DatabaseConnectionString.cs), which overlays the
two onto the connection string when they are present and ignores them when they are not:

| Variable | In production | Locally |
|---|---|---|
| `ConnectionStrings__Default` | The non-secret half: host, port, database, `Trust Server Certificate=true`, `Maximum Pool Size=20` | The whole string, credentials included, from `appsettings.Development.json` |
| `DB_USERNAME` | Injected from the RDS-managed secret's `username` key | Absent |
| `DB_PASSWORD` | Injected from its `password` key | Absent |

The two market-data settings arrive the same way — `Finnhub__ApiKey` as a third injected secret and
`Edgar__ContactEmail` as a plain variable — but are **not** tested for presence alone: outside
Development their absence stops startup (see *The market-data providers* above).

Production is not a branch: the overlay tests for *presence*, never for environment, so the code
that runs in production is the code the tests exercise. Why those two connection-string settings
are non-negotiable is recorded in that file.

### Running the image against the local Postgres

Point it at the Compose Postgres from step 1 and hand it the credentials the way ECS does. Run it
as `Development` so the session cookie is not `Secure`-only, since this is plain HTTP — and so that
it starts on the stub market-data ports, since user secrets do not reach into a container. Docker
Desktop resolves `host.docker.internal` on its own, but Docker Engine on Linux does not, so
`--add-host` maps it explicitly and the same command then works on both:

```bash
docker run --rm -p 8080:8080 --add-host host.docker.internal:host-gateway -e ASPNETCORE_ENVIRONMENT=Development -e "ConnectionStrings__Default=Host=host.docker.internal;Port=5432;Database=compass" -e DB_USERNAME=compass -e DB_PASSWORD=compass compass-api
```

The API is then on <http://localhost:8080> — register, log in, and round-trip a note exactly as
against `dotnet run`.

Compose carries the same thing as an opt-in `api` service, and it is where the two provider
settings are passed through from the calling shell — `FINNHUB_API_KEY` and `EDGAR_CONTACT_EMAIL`,
either or both, a stub standing in for whatever is unset:

```bash
EDGAR_CONTACT_EMAIL=<you@example.com> FINNHUB_API_KEY=<your key> docker compose --profile api up --build
```

### Running migrations from the image

`dotnet ef database update` needs the SDK and the sources, and the runtime image has neither. The
image therefore also carries **`efbundle`**, the committed migrations compiled into a standalone
executable, which production runs as a one-off ECS task with a `command` override — before the
service is allowed to serve traffic, because the Data Protection key ring lives in a
migration-created table and a service that wins that race breaks *login*
([`infra/README.md`](infra/README.md) has the mechanism). It reads the same configuration the app
does, so it takes its credentials the same split way. It boots the same host, too: with no
environment named, as here, EF's tooling runs it as Development and the market-data stubs satisfy
it, but as `Production` — which the ECS task sets — it wants both market-data settings first:

```bash
docker run --rm --add-host host.docker.internal:host-gateway -e "ConnectionStrings__Default=Host=host.docker.internal;Port=5432;Database=compass" -e DB_USERNAME=compass -e DB_PASSWORD=compass compass-api ./efbundle
```

Migrations are still never applied on application startup.

## Deploying it

The deployed shape is CloudFront serving the SPA at the root and path-routing `/api/*` to an ECS
service behind an ALB, at `financial-compass.dev`. It is described in Terraform and split into a
**foundation stack that persists** and an **app stack that is destroyed between work sessions** —
the cost lever that makes the topology affordable (ADR 0009).

Taking that ephemeral half up and down again is the routine act, and it is written down on its own
as [A work session, start to finish](infra/README.md#a-work-session-start-to-finish) — seven steps
from cold, including where the image tag comes from and how to tell that a teardown left nothing
paid behind. What the rest of this section describes is the **one-time** path underneath it:
standing the foundation up from nothing. That is five commands in
[`infra/README.md`](infra/README.md): create the state bucket, `init`, `apply`, publish the SPA,
push the image. The image is the last of those and a step of its own: the
deployed API runs from it, it is tagged with the commit it was built from, and the ECR repository
it lands in has immutable tags. Pushing it deploys nothing — there is no auto-deploy-on-push for a
standard ECS service:

```bash
scripts/push-image.sh
```

Then check the result from outside — delegation with `dig` against a public resolver, both
certificates in the region that consumes each, the apex on an alias rather than an illegal CNAME,
the SPA over HTTPS, the security-group chain admitting exactly one upstream at each link, and an
image in ECR built for the architecture the task will run:

```bash
scripts/verify-foundation.sh
```

Until the app stack exists, `/api/*` returns an origin error. That is the DNS decoupling working,
not a fault: CloudFront points at the *name* `api-origin.financial-compass.dev`, so the stack
behind it can come and go without CloudFront's configuration ever changing.

The app stack is one `terraform apply` and it holds the database. That apply creates RDS, runs the
migration task against it and waits for it to finish, and only then brings the service up — an
ordering the dependency graph enforces rather than a sequence anyone is asked to remember. The
master password is RDS's own and never enters Terraform state; the container receives exactly three
injected secrets — `DB_USERNAME`, `DB_PASSWORD` and the Finnhub API key as `Finnhub__ApiKey` —
fetched by the ECS agent because the application makes no AWS calls at all. The Finnhub key is set
by hand in the foundation stack, once, and the EDGAR contact address is given to the apply;
[`infra/README.md`](infra/README.md) says where.

Then ask the deployed app whether it works. It is an HTTP client rather than a browser because
nothing newly at risk out there is client-side — it is cookie attributes, the origin lock, cache
behaviour and whether two secrets resolved, all of which curl asserts on more precisely than a
rendered page can, and the SPA's own seam already owns the rest:

```bash
scripts/smoke-deployed.sh
```

Nine assertions, and [`infra/README.md`](infra/README.md) describes them — including the two
results that read backwards, where the direct-origin assertion passes by *failing* and a cache hit
on any `/api/*` response fails the whole run.

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
standing in for `/api/*`. Nothing in the app is mocked. The one test-only shim, in
`web/src/test/setup.ts`, resolves the app's relative `/api` URLs against jsdom's location the
way a browser would, because Node's `Request` refuses relative URLs.

```bash
cd web && npm test
```

`npm run typecheck` runs the TypeScript compiler on its own.

Both suites also run on every pull request to `main`, as the two jobs of the `Test` workflow in
`.github/workflows/test.yml`. It reads the SDK version from `api/global.json` and the Node version
from `web/.nvmrc`, uses no secrets and no AWS access, and both jobs must pass for the check to be
green.

## What the API exposes

All under `/api`, JSON bodies only, ProblemDetails on every error.

| Endpoint | Success | Notes |
|---|---|---|
| `GET /api/health` | 200, empty | The load balancer's check. Anonymous, and deliberately shallow — it never touches the database. |
| `POST /api/auth/register` | 201, `{ id, email }` | Signs the new user in. Password: at least 8 characters, no composition rules. Field errors under `errors` (keyed `email`, `password`, or `general`). |
| `POST /api/auth/login` | 200, `{ id, email }` | Any failure is the same generic 401. Repeated failures lock the account. |
| `POST /api/auth/logout` | 204 | Ends the session on the server. 401 when there is no session to end. |
| `GET /api/auth/me` | 200, `{ id, email }` | The session check. 401 (no redirect) when signed out. |
| `GET /api/tickers/{symbol}` | 200, `{ ticker, companyName, cik, baseFacts, history, scenarios, missing }` | The ticker lookup (ADR 0010). Raw values and decimal rates. `scenarios` is Bear, Base, Bull seeded from `history` (ADR 0004, amended), or empty when there is nothing sound to seed from: fewer than two years of it, no current net margin (TTM revenue not positive), or revenue or shares that start or end at nothing or below, which no growth rate can be taken over. Bear is never below −1, the lowest rate the calculators take. 404 unknown ticker; 422 a filer that cannot be normalised, with the plain reason in `detail`; 503 when the fundamentals source fails. When only the quote fails it is still a 200: `baseFacts.currentPrice` is null, `missing` is `["currentPrice"]`, and each scenario's `exitMultiple` keeps its `kind` with a null `value`. On the stub ports the tickers are `AAPL` (profitable), `LCID` (unprofitable), `MRP` (one year of history, no scenarios) and `TSM` (422), all with made-up figures at a price of 100. |
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

The PR gate ([#66](https://github.com/wkimdkim-nz/compass/issues/66)) surfaced one more:

- **Timestamps are truncated to microseconds before they are saved.** Postgres `timestamptz`
  keeps microseconds; .NET's clock counts 100ns ticks. Linux clocks do produce sub-microsecond
  ticks, macOS ones happen not to, so a POST that echoed the untruncated value and a GET that
  read the stored one agreed on a developer machine and disagreed on the CI runner. Any code
  that stamps a row and returns the stamp must truncate first (see `PostgresNow` in
  `NoteEndpoints.cs`, which the persistence work should keep or generalise when `Note` goes).
