# ADR 0009 — The API runs on Amazon ECS (Fargate) behind an ALB, not App Runner

**Status:** Accepted
**Date:** 2026-09-02
**Amends the deployment note in:** ADR 0007

## Context

Every earlier deployment note in this repo (ADR 0007's *"Deployment consequence"*, `roadmap.md`'s
Phase 0 deploy bullet) named **AWS App Runner** as the .NET compute target. It was chosen as the
lowest-ceremony way to get a container behind HTTPS: no load balancer, no target groups, no
listeners, ~$2.56/mo at its floor.

Two investigations changed the picture during
[ticket #10](https://github.com/wkimdkim-nz/compass/issues/10):

- [Research: App Runner ↔ RDS connectivity](https://github.com/wkimdkim-nz/compass/issues/8) found
  that **App Runner is closed to new customers** — no new features, and migrators are pointed at
  ECS. It also found a load-bearing quirk: App Runner pulls images, ships logs and injects secrets
  *on your behalf*, **exempt from your VPC**, so a private, NAT-less service still deploys.
- [Confirm the AWS account can still create App Runner resources](https://github.com/wkimdkim-nz/compass/issues/17)
  then proved **by execution** that this account *can* still create App Runner services despite the
  closure — a service reached `RUNNING` and served `HTTP 200`.

So this is **not a forced migration.** App Runner worked. The question became whether to build on a
frozen service that happens to still work, or on the live one.

## Decision

**The API runs as a standard Amazon ECS service on Fargate, behind an internet-facing Application
Load Balancer, in `ap-southeast-2`.** App Runner is out.

The choice is made on **learning** grounds: App Runner receives no new features, and the
transferable AWS skill — the one this project exists to acquire — is ECS / Fargate / ALB. The
ALB, target-group and listener plumbing App Runner hides *is* the thing worth learning.

Shape, as resolved in [#10](https://github.com/wkimdkim-nz/compass/issues/10):

- **Standard Fargate service, not ECS Express Mode.** Express Mode would be less work and tears
  down cleanly, but it hides exactly the plumbing this decision was made to learn, it means two
  controllers over one stack (a Terraform resource that manages other resources), and its
  **deployment strategy is a fixed canary that cannot be changed** — ~6 minutes of old and new
  tasks live, doubling Postgres connections on a `db.t4g.micro`.
- **256 CPU / 512 MiB, passed explicitly** — AWS's own docs state the default two different ways
  (256/512 vs 1024/2048), a 4× cost swing decided by which page you read.

  > **Amended 2026-09-06 (per [ticket #42](https://github.com/wkimdkim-nz/compass/issues/42)).**
  > **The image is built for `linux/arm64`, so the task definition must declare
  > `runtime_platform { cpu_architecture = "ARM64" }`.** #10 settled the size and left the
  > architecture open, but pushing an image forces the choice, so #42 made it: Fargate is ~20%
  > cheaper on ARM64, the .NET base images are multi-arch, `api/Dockerfile` already builds against
  > `TARGETARCH`, the development machine is Apple silicon so the build is native rather than
  > emulated, and the database is already Graviton (`db.t4g.micro`). ⚠️ Left at the `X86_64`
  > default, the task fails to pull with a manifest mismatch **before any application log exists**.
  > `scripts/push-image.sh` sets the build platform and `scripts/verify-foundation.sh` asserts the
  > pushed image's architecture, so the two ends cannot drift silently.
- **Task in a public subnet with a public IP; RDS private.** ECS has **no control-plane exemption**
  (unlike App Runner), so a private NAT-less task cannot even start — it fails pulling its own
  image. Of the three surviving shapes, public+IP is cheapest now *and* later: the task reaches the
  IGW, so **Phase 1's FMP call costs nothing**, where private shapes owe ~$43/mo of NAT. Security
  is structural, via a **security-group chain** — the task SG admits only the ALB's SG, the RDS SG
  only the task's. **RDS is private in every shape**, which is a genuine improvement on App Runner,
  where the absence of a published source CIDR would have forced `0.0.0.0/0` on 5432.
- **ECR container image.** A standard Fargate service admits nothing else; "source-based deploy"
  was an App Runner concept.
- **Two ACM certs, and HTTPS to the origin.** CloudFront → ALB crosses the public internet;
  `http-only` to the origin would put the session cookie on the wire in plaintext. So a
  **`us-east-1`** cert for CloudFront (CloudFront reads certs only from N. Virginia) **plus** an
  **`ap-southeast-2`** cert on the ALB. The ALB is locked to CloudFront by AWS's managed
  `com.amazonaws.global.cloudfront.origin-facing` prefix list plus a secret header the listener
  requires.
- **Terraform, ephemeral environment, split in two stacks.** *Foundation (persistent):* hosted
  zone, both certs, ECR, S3 + CloudFront, VPC/subnets/SGs, state bucket. *App (ephemeral):* ALB,
  ECS service and tasks, RDS. The two are decoupled **by a DNS name, not a resource reference** —
  CloudFront always points `/api/*` at the fixed hostname `api-origin.financial-compass.dev`, and
  the ephemeral stack owns the Route 53 record aiming it at whatever ALB currently exists. State
  lives in **S3, never in git**.

  > ⚠️ **That record must be a Terraform resource inside the ephemeral stack**, destroyed in the
  > same apply as the ALB it aliases. A record left pointing at a released ELB hostname is a
  > dangling alias on a **same-site** subdomain, which breaks **precondition 1** of ADR 0007's CSRF
  > amendment — whoever re-claims the hostname can forge authenticated requests. Same shape as the
  > `/api/*` coupling below, and just as invisible from this end: keeping the record around to save
  > re-pointing CloudFront looks like tidiness and is a security regression.
- **Migrations run as a one-off ECS task** in the deploy pipeline — preserving ADR 0003's/#3's
  explicit-migration intent from inside the VPC. Startup `Migrate()` is rejected: it would reverse
  a settled decision to paper over a networking gap, and it races itself because deploys run
  concurrent tasks.
- **`manage_master_user_password = true`** on RDS, so the master password is generated and owned by
  RDS in Secrets Manager and **never enters Terraform state**.

**CloudFront VPC origins were considered and rejected.** They are free, supported in
`ap-southeast-2`, and structurally tighter — but they reference the ALB *as a resource*, so the
persistent CloudFront would hard-reference an ALB the ephemeral stack destroys every cycle. Revisit
only if the environment ever becomes persistent.

## Consequences

- **It costs 16×–34× more than App Runner.** The cheapest workable ECS shape is ~$40/mo idle plus
  ~$18.25 of RDS, against App Runner's $2.56 floor — ~3 months of the ~$200 credit budget rather
  than ~9. **The ephemeral-environment decision is what makes this affordable** and was taken
  before the number was known: every dominant line (ALB, Fargate, RDS, public IPs) is hourly and
  dies with `terraform destroy`. At ~10 hours of use a week the real figure is **≈$3.50/mo**.
  `terraform destroy` also **drops the database**, which makes the migration decision mandatory
  rather than optional.

  > **Amended 2026-09-03 (per [ticket #12](https://github.com/wkimdkim-nz/compass/issues/12)).**
  > This bullet originally waved the teardown off as "fine in Phase 0, whose only table is a
  > throwaway." Two tables now die with the stack, and neither is a throwaway: `AspNetUsers` and
  > the **Data Protection key ring**, which ADR 0007 puts in this same database *precisely so that
  > it dies here*. Their lifetimes are pinned to each other — a key ring surviving a teardown would
  > decrypt old cookies into deleted users (**ghost sessions**). So "the environment is ephemeral"
  > is not only the cost lever; it is a **premise of the auth design**, and making the environment
  > persistent reopens ADR 0007's key-ring amendment rather than merely raising the bill.
- **Several hundred more lines of Terraform** than Express Mode or App Runner would need. This is
  the accepted cost, and the most plausible way Phase 0 stalls.
- **ADR 0007's `/api/*` path-routing rule is unchanged — and is now known to be load-bearing.**
  The ALB makes an `api.financial-compass.dev` subdomain newly tempting, and it *would* work for
  the cookie (`SameSite` cares about *site*, not *origin*). **It must not be done:** a subdomain is
  cross-**origin**, so JSON `POST`s trigger a CORS preflight satisfiable only with
  `AllowCredentials` — **precondition 2** of ADR 0007's CSRF amendment — which forces antiforgery
  back before that change ships. See the amendment in ADR 0007.
- **ADR 0007's "Deployment consequence" section and `roadmap.md` were reconciled** with this
  decision ([ticket #21](https://github.com/wkimdkim-nz/compass/issues/21)). App Runner remains in
  the *history* — ADR 0002 and the research notes below record a route actually walked.
- Build-time landmines this hands the implementation: **CloudFront strips cookies by default**, so
  `/api/*` needs caching disabled and an origin-request policy forwarding `Cookie` (get it wrong
  and auth fails silently and totally); `skip_final_snapshot = true` + `deletion_protection =
  false` or `destroy` hangs and leaves paid snapshots; the DB subnet group needs **two subnets in
  two AZs** even single-AZ; deploys run old and new tasks concurrently, **doubling DB
  connections** — size the pool; **verify Postgres 18 is offered on RDS in `ap-southeast-2`**
  (chosen for parity with the local `postgres:18-alpine`, not verified).

  > **Amended 2026-09-07 (per [ticket #44](https://github.com/wkimdkim-nz/compass/issues/44)).**
  > **Postgres 18 is offered in `ap-southeast-2` — 18.1 through 18.6 — and `db.t4g.micro` on `gp3`
  > is orderable against it.** Checked with `aws rds describe-db-engine-versions` and
  > `describe-orderable-db-instance-options` before the instance was written, because a version a
  > region does not offer is refused in the middle of the apply rather than at plan time. The
  > parity itself is unchanged: `infra/app/rds.tf` pins the major version only and lets
  > `auto_minor_version_upgrade` carry the minor.
  >
  > The connection-pool half of the same line is settled too, and by measurement rather than
  > arithmetic. ⚠️ **The live ceiling on `db.t4g.micro` is 79, not the ~100 assumed** — the default
  > parameter group sets `max_connections = LEAST({DBInstanceClassMemory/9531392},5000)`, and
  > 1 GiB ÷ 9531392 ≈ 112 counts memory RDS keeps for itself. `Maximum Pool Size=20` against two
  > concurrent tasks is 40, so it fits with 36 spare — but the headroom is a third of what the
  > round number suggested, and a pool above 38 would make a *deployment* exhaust the database.
  > Observed at the first boot of the instance on 2026-09-07 and recorded in `infra/README.md`.
- **Left open on purpose:** whether this account qualifies for **750 free ALB hours/month** for new
  AWS customers — if it does, the $18.40/mo ALB line vanishes. That changes the *runway*, not the
  design. **Scale-to-zero** is likewise not pursued: teardown is the cost lever.

## Sources

- [`../research/app-runner-rds-connectivity.md`](../research/app-runner-rds-connectivity.md) —
  App Runner's closure, VPC-connector requirement, and control-plane exemption
  ([#8](https://github.com/wkimdkim-nz/compass/issues/8)).
- [`../research/ecs-mode-for-phase-0.md`](../research/ecs-mode-for-phase-0.md) — ECS mode
  comparison and `ap-southeast-2` pricing ([#18](https://github.com/wkimdkim-nz/compass/issues/18)).
