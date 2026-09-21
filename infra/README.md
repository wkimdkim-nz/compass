# Infrastructure

Compass's AWS infrastructure is described in Terraform and split into **two stacks along the
billing line** ([ADR 0009](../docs/adr/0009-compute-ecs-fargate.md),
[#28](https://github.com/wkimdkim-nz/compass/issues/28)):

| Stack | Holds | Lifecycle |
|---|---|---|
| `foundation/` | Hosted zone (adopted), both ACM certificates, the SPA bucket and CloudFront, the VPC and its subnets, the security-group chain, the ECR repository, the CloudFront-to-origin secret, and the Finnhub API key's secret | **Never destroyed.** Everything in it is free or near-free at rest. |
| `app/` | The load balancer and the listener rule that locks it to CloudFront, the origin hostname's record, the ECS cluster, its three task definitions and the service, and the RDS instance | **Created at the start of a work session, destroyed at the end.** Every dominant line item is in here. |

The split is the cost lever, not tidiness: every dominant line item is hourly and dies with
`terraform destroy`, which is what turns ~$58/month into ~$3.50.

## What joins the two stacks

A **DNS name, not a resource reference.** CloudFront path-routes `/api/*` to
`api-origin.financial-compass.dev`, and the app stack owns the record behind that name. So the app
stack can be destroyed and recreated without CloudFront's configuration ever changing, and the site
still serves the SPA while it is torn down. Until the app stack exists, `/api/*` returns an origin
error — that is the correct result, not a fault.

> ⚠️ The origin record must live in the **app** stack and be destroyed with the ALB it aliases. A
> record left pointing at a released load balancer hostname is a dangling alias on a same-site
> subdomain, and whoever re-claims that hostname can forge authenticated requests (ADR 0009).

## A work session, start to finish

Everything below has a section of its own further down; this is the order, in one place, for
someone starting from cold. **Cold means the ephemeral stack is destroyed** — its normal resting
state — with the foundation stack up and the site serving the SPA. If even the foundation is
missing, start at *Bringing the foundation stack up*; that is a one-time path, not part of a
session.

**0. Get a session.** The AWS CLI here is a console login rather than SSO, and it expires fast
enough to go stale between two commands, so this is checked immediately before the apply rather
than trusted from earlier:

```bash
aws login
```

```bash
aws sts get-caller-identity
```

**1. Find the image tag.** This is the only input the stack cannot work out for itself, and the one
step where a wrong answer is easy: **it is not `HEAD`**, for the reason *Bringing the ephemeral
stack up* gives below. Ask the registry what is actually in it:

```bash
aws ecr describe-images --region ap-southeast-2 --repository-name compass-api --query 'sort_by(imageDetails,&imagePushedAt)[-1].imageTags[0]' --output text
```

Then confirm that tag still describes the code you are deploying — **empty output means it does**:

```bash
git diff --stat <tag>..HEAD -- api
```

Not empty means `api/` has moved since that image was built: push a new one (*Putting an image in
the registry*) and use the tag it prints.

The same question has two quieter siblings, because an image is not the only thing a session can
find stale. **If `web/` has moved since the SPA was last published**, publish it —
`scripts/deploy-spa.sh` — or the browser pass exercises the old pages against the new API. **If
`infra/foundation/` has moved**, plan it and apply whatever the plan shows; *Bringing the
foundation stack up* says why an unapplied `outputs.tf` stops the app stack's plan. Then
`scripts/verify-foundation.sh`, which also says whether the Finnhub key is still the placeholder.

**2. Bring the ephemeral stack up.** One apply creates the database, runs the migration task
against it, waits for that to finish and only then brings the service up — an ordering the
dependency graph enforces. `init` is needed once per clone, and is a no-op afterwards:

```bash
terraform -chdir=infra/app init
```

The apply also needs the **EDGAR contact address** — `edgar_contact_email`, which has no default
because it is not in source (ADR 0010). Export it once and both this apply and step 5's destroy
pick it up. Replace the whole placeholder, brackets included: pasted as it stands, zsh reads
`<you@example.com>` as a redirect from a file of that name and the export fails:

```bash
export TF_VAR_edgar_contact_email=<you@example.com>
```

```bash
terraform -chdir=infra/app apply -var "image_tag=<tag>"
```

Expect ten minutes or so, most of it RDS. It blocks until the service is *stable*, not merely
created.

**3. Ask the app whether it works.** Eleven assertions, cheapest first, and the only automated seam
against the deployed environment:

```bash
scripts/smoke-deployed.sh
```

**4. Work.** <https://financial-compass.dev> is live. `scripts/run-one-off-task.sh psql -c '...'`
is the way into the database; there is no other.

**5. Tear it down.** This is the cost lever, and the step that must not be skipped — every dominant
line item is hourly. The tag satisfies the parser and means nothing here; the contact address is
still required, and is still exported from step 2 unless this is a new shell:

```bash
terraform -chdir=infra/app destroy -var "image_tag=none"
```

It is done when it prints `Destroy complete!` and not before: it stops at a prompt that takes only
`yes`, and a new shell without the export stops it earlier still, at a prompt for the address. A
destroy that was started is not one that ran — in #76's session the first attempt left all 24
resources up and billing, and only step 6 said so.

It takes the origin record with it, so the site goes on serving the SPA and `/api/*` returns an
origin error again. That is the destroyed state, not a fault.

**6. Check that the teardown actually finished.** A destroy that *reports* success is not the same
as one that left nothing paid behind, and the residue bills quietly. Two commands, because a
deleted instance leaves its two kinds of backup in two different places — a final snapshot lands
among the **manual** snapshots, and retained automated backups outlive the instance under an API of
their own that `describe-db-snapshots` does not answer for. Both should print nothing:

```bash
aws rds describe-db-snapshots --region ap-southeast-2 --snapshot-type manual --query 'DBSnapshots[?DBInstanceIdentifier==`compass`].DBSnapshotIdentifier' --output text
```

```bash
aws rds describe-db-instance-automated-backups --region ap-southeast-2 --query 'DBInstanceAutomatedBackups[?DBInstanceIdentifier==`compass`].DBInstanceArn' --output text
```

`skip_final_snapshot` and `backup_retention_period` are set so that neither kind of residue is
created in the first place (`deletion_protection` is the third of that trio, but it gates the
destroy itself rather than any backup) — so this is a check on those settings still being what they
were, not a routine clean-up.

The other quiet failure is a Route 53 record that outlives the load balancer it aliased: a dangling
alias on a same-site subdomain, which whoever next claims that released hostname can forge
authenticated requests through — ADR 0009's DNS-decoupling bullet and ADR 0007's precondition 1,
which record it in both places on purpose. The destroy takes the record with it, so the check is
that the name no longer resolves:

```bash
dig +short api-origin.financial-compass.dev @1.1.1.1
```

And the foundation should be exactly as it was, which is the whole point of joining the two stacks
by a name:

```bash
scripts/verify-foundation.sh
```

## Bringing the foundation stack up

State lives in S3 and never in git. The bucket cannot be created by the configuration whose state
it holds, so it is created once by a script — the only piece of the foundation that is not
Terraform, and the only reason it isn't:

```bash
scripts/bootstrap-state-bucket.sh
```

```bash
terraform -chdir=infra/foundation init
```

```bash
terraform -chdir=infra/foundation apply
```

The apply blocks while ACM validates both certificates through Route 53 and while CloudFront
deploys, which together take a few minutes. Then publish the SPA — Terraform owns the bucket, this
owns what is in it:

```bash
scripts/deploy-spa.sh
```

**Then set the Finnhub API key, once, by hand.** Terraform creates the secret
`compass/finnhub-api-key`, writes a placeholder into it once as the secret is created, and never
writes to it again, so the key is in no variable and no state file, and it survives every
teardown of the app stack (ADR 0010). Sign up at <https://finnhub.io>, copy the key from the
dashboard, and hand it over on stdin — `file:///dev/stdin` keeps it out of the shell history and
out of the process list. Run it, paste the key, press Enter, then Ctrl-D:

```bash
aws secretsmanager put-secret-value --region ap-southeast-2 --secret-id compass/finnhub-api-key --secret-string file:///dev/stdin
```

The same command replaces the key later. A running task does not notice — injection happens once,
at task start — so follow a replacement with a rebuild of the app stack, or with
`aws ecs update-service --force-new-deployment`.

Then push an image — the next section — and only then verify. `verify-foundation.sh` asserts that
the registry holds an image, so on a fresh stack it fails until one is there; it also fails while
the Finnhub secret still holds the placeholder.

> ⚠️ **Re-apply the foundation after pulling a change to its `outputs.tf`** — the change that added
> the Finnhub secret is one, and until it is applied the app stack cannot plan. Outputs are the app
> stack's only view of the network, and they are written by an apply, not by the file. The app
> stack's plan otherwise fails with `This object does not have an attribute named ...` — which
> reads like a bug in the app stack and is not. The apply itself is a no-op on every resource.

## Putting an image in the registry

The app stack can only name an image that already exists, so pushing one is a step of its own —
and, like the SPA, it is not a side effect of `terraform apply`:

```bash
scripts/push-image.sh
```

That builds `api/Dockerfile` and pushes it tagged with the commit it was built from. Tags in the
repository are **immutable**, and the script refuses to build a dirty `api/` tree, so the tag in a
task definition is evidence of what is running rather than a pointer someone may have moved.

The image is built for **`linux/arm64`** — Fargate is about 20% cheaper on ARM64, the .NET base
images are multi-arch, and the development machine is Apple silicon, so it builds natively. ⚠️ The
app stack's task definition must therefore declare `runtime_platform { cpu_architecture = "ARM64" }`;
left at the X86_64 default, the task fails to pull with a manifest mismatch before any application
log exists.

> ⚠️ **Pushing an image deploys nothing.** There is no auto-deploy-on-push for a standard ECS
> service — that was App Runner's behaviour, and App Runner is out (ADR 0009). A push adds a tag;
> a deployment names it and rolls the service, in the app stack.

## Bringing the ephemeral stack up

The commands are steps 1, 2 and 5 of *A work session, start to finish* above, and they live there
rather than here so that there is one copy of each to keep true. This is what they do, and why each
is shaped the way it is.

This is the half that costs money, and the half that is thrown away. It needs the foundation stack
applied and an image in the registry; it reads the network, the registry and the certificate out of
the foundation stack's state, and the origin header out of Secrets Manager **by name**.

The image tag is the only thing the stack cannot work out for itself — a deployment names an image,
so `image_tag` has no default. It is whatever `scripts/push-image.sh` last printed.

⚠️ **It is not `HEAD` unless `api/` changed and you pushed.** `push-image.sh` tags with the commit
that was checked out when it ran, so a branch full of Terraform and prose leaves `HEAD` moving
while the registry stands still — and `-var "image_tag=$(git rev-parse --short=12 HEAD)"` then
fails the task definitions' precondition, which is the precondition working. That is why step 1
asks the registry rather than `git`.

⚠️ But asking the registry is only half of it, because **the newest tag can be stale too**.
Rebuilding to make `HEAD` true is waste **when `api/` has not moved** — it pushes a second tag over
identical bytes and expires an older one out of the lifecycle policy for nothing. When `api/`
*has* moved, the newest tag no longer describes the code being deployed, and the tag stops being
evidence of what is running, which is the only reason it is a commit hash at all. Hence the second
half of step 1: a non-empty `git diff --stat <tag>..HEAD -- api` is the case where you do push.
⚠️ It can be non-empty for a change that cannot alter a single byte of IL — a comment, a doc
string — and the answer is still to push, because "the tag describes `api/`" is a cheap rule that
survives being applied without judgement and an expensive one to apply with it.

One apply does three things in an order it cannot rearrange: it creates the database, runs the
migration task against it and waits for that to finish, and only then creates or updates the
service. The next section is how that ordering is enforced and why it has to be.

Expect ten minutes or so. Most of it is RDS — a fresh instance takes five to eight minutes before
it accepts a connection — and the load balancer being provisioned alongside it.

The apply blocks until the service is **stable**, not merely created: the task has to start, pass
the target group's health check and stay up. That is deliberate — without it an apply reports
success while the task is crash-looping, and the failure surfaces minutes later as an unexplained
502 from the public site.

A task that cannot start does not make that wait open-ended. The service's deployment circuit
breaker gives up after a run of failed starts, so the apply fails rather than hanging until
Terraform's own timeout — and the reason is in the stopped task's status
(`aws ecs describe-tasks`), not in the log group, because the container never ran.

Then tear it down at the end of the session (step 5). This is the cost lever, and it takes the
origin record with it, so the site goes on serving the SPA and `/api/*` returns an origin error
again. A destroy still has to be given every required variable even though it builds nothing, which
is why the tag is passed to something that will not use it.

## How migrations are ordered, and why that is the load-bearing part

`api/Dockerfile` builds **`efbundle`** beside the app — the committed migrations compiled into one
executable — because `dotnet ef database update` needs the SDK and the sources and the runtime image
has neither. Production runs it as a **one-off ECS task**: the same image, the same task definition
shape, with `command = ["./efbundle"]` replacing the app. The app is a `CMD` rather than an
`ENTRYPOINT` precisely so that override replaces it outright.

Migrations are **never** applied at application startup. A deployment runs the old task and the new
one at once, so a startup `Migrate()` races itself.

> ⚠️ **The loser of that race cannot log in.** The Data Protection key ring lives in a
> migration-created table (ADR 0007), so a service task that starts before the schema exists does
> not present as a missing feature — it presents as authentication being broken for everyone, with
> nothing in the log that names a migration.

[#28](https://github.com/wkimdkim-nz/compass/issues/28) left open how that ordering is enforced on
the **first manual deploy**, before any CI exists to sequence it. This is the answer, and it is a
mechanism rather than a habit:

1. `terraform_data.migrate` runs `scripts/run-one-off-task.sh migrate` as a `local-exec`
   provisioner. The script starts the task, waits for it to stop, prints what it logged, and exits
   with **the container's own exit code**.
2. `aws_ecs_service.api` declares `depends_on = [terraform_data.migrate, ...]`. Nothing else in the
   service references the migration, so without that edge Terraform is free to bring the service up
   first.
3. A failed migration therefore fails the apply, and the service is never created or updated. A
   stack whose migration did not run is a stack with **no service in front of it** — a plain origin
   error — rather than a service answering 500s against a schema that is not there.
4. It re-runs whenever the migration task definition gets a new revision: a new image tag, a new
   database endpoint, any change to the shared environment. An unchanged re-apply runs nothing.

⚠️ The exit code is the whole of step 1. `aws ecs run-task` reports success when a task is
*accepted*, not when it works, so a migration that failed against the database and a migration that
never started both look like success to it.

The service task and the migration task share **one** declaration of environment and secrets —
`local.task_environment` and `local.task_secrets` in `infra/app/ecs.tf`. That is not tidiness: a
migration applied to one database while the service talks to another is a failure with no error
message anywhere. Anything either task needs to reach Postgres belongs in those two lists.

## What the container is given, and what it is not

Exactly three values are injected as secrets. Two are `DB_USERNAME` and `DB_PASSWORD`, pulled key
by key out of the RDS-managed secret with Secrets Manager's `<arn>:<json-key>::` selector. The rest
of the connection string is a plain environment variable, and `DatabaseConnectionString.Resolve`
joins them at startup through the same code path the local tests walk. The third is the Finnhub API
key.

### The two market-data settings

One setting per market-data port (ADR 0010). **This table is the one place their names are
recorded**; the API's options classes, `FinnhubOptions` and `EdgarOptions` in
`api/Compass.Api/MarketData/`, bind to exactly these keys.

| Configuration key | Environment variable | What it is | Deployed | Locally |
|---|---|---|---|---|
| `Finnhub:ApiKey` | `Finnhub__ApiKey` | The quote source's API key | Injected by the ECS agent from the foundation stack's `compass/finnhub-api-key` secret, which the app stack finds **by name**. The operator sets its value by hand — *Bringing the foundation stack up* | `dotnet user-secrets` |
| `Edgar:ContactEmail` | `Edgar__ContactEmail` | The contact address in the User-Agent SEC EDGAR requires | A plain environment variable on the task, from the app stack's `edgar_contact_email` variable. The operator supplies it with the apply — step 2 of *A work session* | An exported environment variable |

Neither value is in source or in a `.tfvars`. **Only the Finnhub key is also out of Terraform
state.** The contact address is not: it is a plain environment variable, so it is written into both
task definitions and therefore into the **app** stack's state, and it is readable on the task
definition by anyone who can call `ecs describe-task-definition`. That is acceptable because it is
an address SEC EDGAR is sent in the clear on every request, not a credential, and because that
state is destroyed with the stack — but it is not a secret and nothing here treats it as one. The
repository root's `README.md` has the local commands.

**A setting that is absent stubs its port only in Development.** There the API starts, logs a
warning naming the stub port and the setting that would make it real, and calls nobody. In every
other environment it refuses to start and the exception names the missing setting — so a deployed
task either talks to the real providers or is not healthy. Blank counts as absent. Both settings
are in the lists the service and the migration task share, and the migration task needs them too:
`efbundle` boots the same host to find its `DbContext`, so it refuses to run on the same terms.

**The ECS agent fetches; the application does not.** There is no task role on any of these task
definitions, so there is nothing for the application to assume — the absence of application-side AWS
access is structural rather than a matter of discipline. The execution role carries
`AmazonECSTaskExecutionRolePolicy` plus two inline policies, each `secretsmanager:GetSecretValue` on
one secret ARN: the RDS-managed secret and the Finnhub key.

> ⚠️ **The seven-day tripwire.** RDS rotates that managed secret every seven days by default,
> injection happens **once** at task start, and AWS has no channel to push a new value into a
> running container. This is safe **solely** because the app stack is ephemeral — a work session is
> hours. [#19](https://github.com/wkimdkim-nz/compass/issues/19) names the one trigger to move to
> an application-side password provider: *"the environment becomes permanent"*, meaning this stack
> stops being destroyed between sessions. Nothing else is that trigger.

## Looking inside the database

The instance is private, in subnets with no route off the VPC, behind a security group that admits
one other security group. That is the design working — and it also means there is no way to run a
query from a laptop. There is no bastion, no public endpoint, and `aws ecs execute-command` would
need the task role this stack deliberately does not have.

So the way in is a third task definition, `compass-psql`, in the same network position as the API
with a Postgres client in it. It costs nothing while it is not running:

```bash
scripts/run-one-off-task.sh psql -c 'SELECT count(*) FROM "AspNetUsers"'
```

Everything after `psql` is arguments to `psql` itself; the script supplies the binary. ⚠️ It has
to, and this is not ergonomics: the postgres image's entrypoint reads a leading `-` as arguments to
the *server* and tries to initialise a new database cluster, failing with a message about
`POSTGRES_PASSWORD` that never mentions the query you asked for.

With no arguments at all it runs `SHOW max_connections`, which is the one observation below.

### The connection ceiling

`Maximum Pool Size=20` is in the connection string because Npgsql defaults to **100**. A deployment
runs two tasks at once, so the arithmetic that has to hold is **2 × 20 = 40 connections** against
the instance's ceiling.

That ceiling is not something AWS publishes. The default parameter group sets
`max_connections = LEAST({DBInstanceClassMemory/9531392},5000)`, and `DBInstanceClassMemory` is the
memory left after RDS's own overhead — a number only the running instance knows. So it is observed
once, at the first boot of a rebuilt database, and recorded here
([#28](https://github.com/wkimdkim-nz/compass/issues/28) excludes it from the smoke test on purpose:
it is a one-time observation, not a per-rebuild assertion):

```bash
scripts/run-one-off-task.sh psql
```

| Instance | Engine | Observed `max_connections` | Reserved | Usable | Against 2 × 20 |
|---|---|---|---|---|---|
| `db.t4g.micro` | Postgres 18.6 in `ap-southeast-2` | **79** (observed 2026-09-07) | 3 | 76 | fits, 36 spare |

⚠️ **79, not the ~100 the design assumed.** The arithmetic that produced "near 100" —
1 GiB ÷ 9531392 ≈ 112 — counts memory RDS keeps for itself, which is exactly why
[#28](https://github.com/wkimdkim-nz/compass/issues/28) said to verify rather than compute. The
pool ceiling still holds and is unchanged, but the headroom is a third of what the round number
suggested: raising `Maximum Pool Size` past 38 would make a *deployment* the thing that exhausts
the database.

If a future instance class lands below 40, the pool ceiling is what moves — lower
`Maximum Pool Size` in `local.database_connection_string` rather than assuming a deployment will
not overlap.

## Checking the result

```bash
scripts/verify-foundation.sh
```

`verify-foundation.sh` checks the result from outside: delegation via `dig` against a public
resolver rather than off the console, both certificates issued in the region that consumes each,
an alias (never a CNAME) at the apex, the SPA and its client-side routes over HTTPS, `/api/*`
reaching the origin hostname with caching disabled and cookies forwarded, the network's two tiers
routed the way their names claim, each security group admitting exactly one upstream, an image in
ECR built for the architecture the task will run, CloudFront sending the origin header the
secret holds, and the Finnhub secret holding something other than Terraform's placeholder — compared,
never printed.

The ephemeral stack has its own script, and it is the **only** automated seam against the deployed
environment. Run it after the apply:

```bash
scripts/smoke-deployed.sh
```

It reads the base URL and the cluster and service out of the two stacks' outputs, or takes them as
arguments — `scripts/smoke-deployed.sh <base-url> <cluster> <service>` — so the same script runs
from CI later without being rewritten. It registers two throwaway users on a fresh unique email
each run and **cleans up nothing**: `terraform destroy` is the cleanup.

Eleven assertions, cheapest first, each named for the silent failure it catches: the SPA loads; the
origin refuses direct access; `/api/health` returns 200; register returns 201 with `HttpOnly`,
`Secure` and `SameSite=Lax` read off the raw header; the session check returns 200; a second user
in a second cookie jar sees themselves on a cache miss; saving a projection returns 201 with the
outputs the API recomputed; an unauthenticated call returns 401 with no `Location`; the ticker
lookup returns AAPL's base facts from the real providers; the projection reads back as the saved
assumptions over a live price, and as a 404 to the second user; and the session survives a forced
redeployment. The lookup assertion is the only place SEC EDGAR and Finnhub are asked for real, and
it fails against a local API by design: with neither setting the ports are stubs, and it checks
for the SEC's CIK. The script itself cannot be pointed there — it refuses a plaintext base URL, and
its first two assertions are about CloudFront — so seeing that failure means replaying the lookup
with curl against `http://localhost:5111`, where the stub answers CIK 1, "Stub Profitable Inc.". It
is the only one of the three that can tell: save and load never reach a provider, and pass on the
stubs (#76). It stops at the first failure, because it walks one session forwards and everything
after a break would be noise.

Two of those are worth knowing about before you read a red line.

**The direct-origin assertion passes by failing.** The origin must be unreachable except through
CloudFront, and the security group rejects at TCP rather than answering, so the only passing
results are a TCP connection that never completes within the budget or one that is refused — which
is why the wait is bounded rather than generous. The probe is a raw TCP connection (`telnet://`)
rather than an HTTPS request, because curl runs TCP and TLS on one clock and cannot tell a dropped
packet from an accepted socket that stalls in the handshake: both exit 28 with no connect time
recorded. **A completed TCP handshake is a failure, whatever happens after it**, and only then does
the script make the HTTPS request, to say how far it got. A `403` is the listener's default action,
so it proves the request reached the load balancer: the security-group half of the lock is open
even though the header half held. A `200` means both halves are.

> ⚠️ **[#28](https://github.com/wkimdkim-nz/compass/issues/28) says the opposite**, in its
> assertion 2: *"timeout-or-403 is the pass"*. The build is right and the spec's wording is wrong —
> `alb.tf`'s listener answers a fixed `403` by default precisely so that reaching it has an
> unambiguous signature, which makes a 403 the clearest possible evidence that the security group
> admitted an address it should not have. Flagged on
> [#46](https://github.com/wkimdkim-nz/compass/issues/46) rather than corrected in #28's body,
> because a closed spec is a record of what was asked for. Recorded here because this is the page
> someone reads while looking at a red line.

**A cache hit on any `/api/*` response fails the run outright**, wherever it appears, because a
cached authenticated response is one user's data served to another and it does not matter which
path leaks it. `X-Cache` is the only cheap witness for that, and the `/api/*` behaviour uses
CachingDisabled precisely so it always reads `Miss`.

The health assertion wants exactly `200`, and redirects are not followed, so a `307` shows as a
`307` rather than being quietly resolved. That is the answer the path most needs to reject: the
target group matches `200` alone, so a redirect here means `UseHttpsRedirection()` has crept back
into the API and every health check from the load balancer is in a loop — with nothing in the
application's own logs to say so.

The last assertion is the expensive one and the only one that changes anything: it forces a new
deployment and waits for stability, which takes a few minutes. A forced deployment starts a second
task before stopping the first, so a session issued by the old task has to be decrypted by a task
that never saw it — which only works if the Data Protection key ring is the shared one in Postgres
([ADR 0007](../docs/adr/0007-auth-dotnet-identity-cookies.md)). Left in memory, every deployment
silently signs everyone out and nothing anywhere reports it. The task ARN is compared before and
after, so a deployment that rolled nothing cannot pass by re-checking a session against the very
task that issued it.

## The network, in one paragraph

The task runs in a **public subnet with a public IP**, and the database is private. That looks
backwards and is not: ECS has no control-plane exemption, so a Fargate task pulls its image,
fetches its secret and ships its logs over the same network path as application traffic. A private,
NAT-less, endpoint-less task does not run slowly — it **cannot start at all**. Fixing that with NAT
costs about $43/month in this region, more than everything else running put together, and interface
endpoints across two availability zones cost *more* than NAT rather than less. The public subnet is
also what makes Phase 1's outbound market-data call free.

What keeps it safe is a chain, not a perimeter — each group admits exactly one upstream:

```
CloudFront ──443──▶ load balancer ──8080──▶ task ──5432──▶ database
```

The load balancer admits only AWS's managed `com.amazonaws.global.cloudfront.origin-facing` prefix
list. Those ranges are public and shared by every CloudFront customer, so the prefix list proves
"a CloudFront" and a **secret header** proves "this distribution". The foundation stack generates
that header into Secrets Manager as `compass/cloudfront-origin-header`, holding
`{header_name, header_value}`, and the app stack reads it **by name** — never through a
remote-state reference, for the same reason the two stacks are joined by a DNS name.

## Things that are easy to get wrong

- **CloudFront's certificate must be requested in `us-east-1`**, whatever region the stack runs
  in. Requested in `ap-southeast-2` it issues perfectly and CloudFront simply cannot see it. The
  ALB's certificate belongs in `ap-southeast-2`. `providers.tf` carries an alias for exactly this.
- **CloudFront strips cookies from origin requests by default.** `/api/*` therefore needs both
  caching disabled *and* an origin request policy that forwards `Cookie`. Get it wrong and nothing
  looks broken: the SPA renders, and every authenticated call 401s.
- **SPA deep links are handled by a CloudFront Function, not by custom error responses.** The
  usual trick — map 403/404 to `/index.html` with a 200 — is configured per *distribution*, not
  per behaviour, so it would also rewrite the API's own 404s into an HTML page with status 200.
  The function is attached to the default behaviour only, so it never sees `/api/*`.
- **The database subnet group needs two subnets in two availability zones**, even though the
  instance is single-AZ. RDS rejects a one-AZ group at creation, which means the failure arrives
  in the middle of the app stack's apply rather than here.
- **The origin header's value is sensitive, so Terraform hides the whole `/api/*` origin block
  from every plan** — including `origin_protocol_policy`. A clean plan is therefore not evidence
  that the origin is still HTTPS-only; `verify-foundation.sh` asserts it against the live
  distribution for exactly that reason.
- **The health path is `/api/health`, not `/health`.** CloudFront routes only `/api/*` to the
  origin, so the shallow health path lives under that prefix and nothing answers on the shorter
  one. The target group and the smoke test must both name the longer path.
- **What attaches the target group to the load balancer is the listener *rule*, not the listener.**
  The listener's own default action is a fixed `403` — that is the origin lock's default position —
  so the ECS service has to depend on the rule. Without that dependency the first apply fails
  saying the target group has no load balancer, and the second one succeeds.
- **`Trust Server Certificate=true` is load-bearing in the connection string.** Npgsql 8 changed
  `SSL Mode=Require` to validate the server's certificate, and RDS's certificate authority is not in
  the runtime image's trust store. Without it the connection does not fall back to something weaker
  — it fails outright, and the task is unhealthy with a TLS error in the application log.
- **The database's port is a foundation output, not a literal.** The security group admits 5432 and
  the instance has to be created on the same number. Nothing refuses a mismatch: the task simply
  never connects, and it reads as a database that is down.
- **`skip_final_snapshot`, `deletion_protection` and `backup_retention_period` are all three set,
  and each one alone breaks the cost lever.** Deletion protection makes `destroy` fail outright; a
  final snapshot makes it hang and then bill for what it left behind, every session, forever;
  retained automated backups do the same more quietly.
- **The execution role's *policies* are not referenced by any task definition, only the role is.**
  So the one-off task's `depends_on` names them explicitly. Without it Terraform may start a task
  before the agent can pull an image or read the secret, and neither failure writes an application
  log — the container never runs, and the reason is only in the stopped task's status.
- **The health check stays shallow, and that is what makes a database fault recoverable.** A deep
  check turns any database problem into an endless kill-and-replace loop, and restarting a task
  never repairs a database.
