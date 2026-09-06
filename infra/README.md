# Infrastructure

Compass's AWS infrastructure is described in Terraform and split into **two stacks along the
billing line** ([ADR 0009](../docs/adr/0009-compute-ecs-fargate.md),
[#28](https://github.com/wkimdkim-nz/compass/issues/28)):

| Stack | Holds | Lifecycle |
|---|---|---|
| `foundation/` | Hosted zone (adopted), both ACM certificates, the SPA bucket and CloudFront, the VPC and its subnets, the security-group chain, the ECR repository, and the CloudFront-to-origin secret | **Never destroyed.** Everything in it is free or near-free at rest. |
| `app/` (not built yet) | ALB, ECS service and tasks, RDS | Created at the start of a work session, destroyed at the end. |

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
