# Infrastructure

Compass's AWS infrastructure is described in Terraform and split into **two stacks along the
billing line** ([ADR 0009](../docs/adr/0009-compute-ecs-fargate.md),
[#28](https://github.com/wkimdkim-nz/compass/issues/28)):

| Stack | Holds | Lifecycle |
|---|---|---|
| `foundation/` | Hosted zone (adopted), both ACM certificates, the SPA bucket and CloudFront, and later the VPC, security groups, ECR and the origin secret | **Never destroyed.** Everything in it is free or near-free at rest. |
| `app/` (not built yet) | ALB, ECS service and tasks, RDS | Created at the start of a work session, destroyed at the end. |

The split is the cost lever, not tidiness: every dominant line item is hourly and dies with
`terraform destroy`, which is what turns ~$58/month into ~$3.50.

## What joins the two stacks

A **DNS name, not a resource reference.** CloudFront path-routes `/api/*` to
`api-origin.financial-compass.dev`, and the app stack owns the record behind that name. So the app
stack can be destroyed and recreated without CloudFront's configuration ever changing, and a torn
down environment still serves the SPA. Until the app stack exists, `/api/*` returns an origin
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

```bash
scripts/verify-foundation.sh
```

`verify-foundation.sh` checks the result from outside: delegation via `dig` against a public
resolver rather than off the console, both certificates issued in the region that consumes each,
an alias (never a CNAME) at the apex, the SPA and its client-side routes over HTTPS, and `/api/*`
reaching the origin hostname with caching disabled and cookies forwarded.

## Three things that are easy to get wrong

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
