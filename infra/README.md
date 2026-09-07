# Infrastructure

Compass's AWS infrastructure is described in Terraform and split into **two stacks along the
billing line** ([ADR 0009](../docs/adr/0009-compute-ecs-fargate.md),
[#28](https://github.com/wkimdkim-nz/compass/issues/28)):

| Stack | Holds | Lifecycle |
|---|---|---|
| `foundation/` | Hosted zone (adopted), both ACM certificates, the SPA bucket and CloudFront, the VPC and its subnets, the security-group chain, the ECR repository, and the CloudFront-to-origin secret | **Never destroyed.** Everything in it is free or near-free at rest. |
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

Then push an image — the next section — and only then verify. `verify-foundation.sh` asserts that
the registry holds an image, so on a fresh stack it fails until one is there.

> ⚠️ **Re-apply the foundation after pulling a change to its `outputs.tf`.** Outputs are the app
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

This is the half that costs money, and the half that is thrown away. It needs the foundation stack
applied and an image in the registry; it reads the network, the registry and the certificate out of
the foundation stack's state, and the origin header out of Secrets Manager **by name**.

```bash
terraform -chdir=infra/app init
```

The image tag is the only thing the stack cannot work out for itself — a deployment names an image,
so `image_tag` has no default. It is what `scripts/push-image.sh` printed, which is the commit
`api/` was built from:

```bash
terraform -chdir=infra/app apply -var "image_tag=$(git rev-parse --short=12 HEAD)"
```

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

Then tear it down at the end of the session. This is the cost lever, and it takes the origin
record with it, so the site goes on serving the SPA and `/api/*` returns an origin error again. A
destroy still has to be given every required variable even though it builds nothing, so the tag
here satisfies the parser and means nothing:

```bash
terraform -chdir=infra/app destroy -var "image_tag=none"
```

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

Exactly two values are injected as secrets — `DB_USERNAME` and `DB_PASSWORD`, pulled key by key out
of the RDS-managed secret with Secrets Manager's `<arn>:<json-key>::` selector. The rest of the
connection string is a plain environment variable, and `DatabaseConnectionString.Resolve` joins them
at startup through the same code path the local tests walk.

**The ECS agent fetches; the application does not.** There is no task role on any of these task
definitions, so there is nothing for the application to assume — the absence of application-side AWS
access is structural rather than a matter of discipline. The execution role carries
`AmazonECSTaskExecutionRolePolicy` plus one inline statement, `secretsmanager:GetSecretValue` on the
single secret ARN.

> ⚠️ **The seven-day tripwire.** RDS rotates that managed secret every seven days by default,
> injection happens **once** at task start, and AWS has no channel to push a new value into a
> running container. This is safe **solely** because the stack is ephemeral — a work session is
> hours. *"The environment becomes permanent"* is the named trigger to move to an application-side
> password provider, and nothing else is
> ([#19](https://github.com/wkimdkim-nz/compass/issues/19)).

## Looking inside the database

The instance is private, in subnets with no route off the VPC, behind a security group that admits
one other security group. That is the design working — and it also means there is no way to run a
query from a laptop. There is no bastion, no public endpoint, and `aws ecs execute-command` would
need the task role this stack deliberately does not have.

So the way in is a third task definition, `compass-psql`, in the same network position as the API
with a Postgres client in it. It costs nothing while it is not running:

```bash
scripts/run-one-off-task.sh psql -c 'SELECT count(*) FROM asp_net_users'
```

With no arguments it runs `SHOW max_connections`, which is the one observation below.

### The connection ceiling

`Maximum Pool Size=20` is in the connection string because Npgsql defaults to **100** and a
`db.t4g.micro` tops out near it. A deployment runs two tasks at once, so the arithmetic that has to
hold is **2 × 20 = 40 connections** against the instance's ceiling.

That ceiling is not something AWS publishes. The default parameter group sets
`max_connections = LEAST({DBInstanceClassMemory/9531392},5000)`, and `DBInstanceClassMemory` is the
memory left after RDS's own overhead — a number only the running instance knows. So it is observed
once, at the first boot of a rebuilt database, and recorded here
([#28](https://github.com/wkimdkim-nz/compass/issues/28) excludes it from the smoke test on purpose:
it is a one-time observation, not a per-rebuild assertion):

```bash
scripts/run-one-off-task.sh psql
```

| Instance | Engine | Observed `max_connections` | Against 2 × 20 |
|---|---|---|---|
| `db.t4g.micro` | Postgres 18 in `ap-southeast-2` | _not yet observed_ | — |

⚠️ If it lands below 40, the pool ceiling is wrong and not the arithmetic: lower
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
ECR built for the architecture the task will run, and CloudFront sending the origin header the
secret holds.

The ephemeral stack has no script of its own yet —
[#45](https://github.com/wkimdkim-nz/compass/issues/45) builds `scripts/smoke-deployed.sh` and both
checks below become two of its nine assertions. Until then they are two commands, and between them
they prove the path-routing arrangement end to end:

```bash
curl -sS -o /dev/null -w '%{http_code}\n' --max-time 30 https://financial-compass.dev/api/health
```

`200`. Not `307`: `curl` is not following redirects here, so a redirect would show as one — and a
redirect on this path means `UseHttpsRedirection()` has crept back into the API and every health
check from the load balancer is in a loop.

```bash
curl -sS -o /dev/null --max-time 10 https://api-origin.financial-compass.dev/api/health
```

⚠️ This one **passes by failing**. The origin must be unreachable except through CloudFront, and
the security group rejects at TCP rather than answering, so the only passing results are
`curl: (28) Operation timed out` — exit 28 — or a connection refused — exit 7. A bounded wait is
therefore part of the check rather than a convenience. **Any HTTP response is a failure.** A `403`
is the listener's default action, so it proves the request reached the load balancer: the
security-group half of the lock is open even though the header half held. A `200` means both
halves are.

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
