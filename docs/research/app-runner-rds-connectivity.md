# Research — How AWS App Runner reaches RDS Postgres

Answers GitHub issue #8 (wayfinder research ticket). Research date: **2026-07-27**. All prices read on that date; all AWS doc pages fetched live on that date. This note reports facts only — the architectural decision belongs to a separate ticket.

## Bottom line

- **App Runner is closed to new customers.** The App Runner Developer Guide now carries a banner on every page: "AWS App Runner is no longer open to new customers. Existing customers can continue to use the service as normal." AWS is not adding features and points migrators at Amazon ECS Express Mode ([availability change](https://docs.aws.amazon.com/apprunner/latest/dg/apprunner-availability-change.html)). If the Compass AWS account has never created an App Runner resource, the rest of this note may be academic — verify account eligibility before designing around it.
- **Yes, a VPC connector is effectively required** to reach RDS, unless the RDS instance is set `Publicly accessible = Yes` and its security group allows inbound from the open internet — because default App Runner egress leaves from AWS-managed public IPs that you cannot enumerate or pin ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html), [RDS VPC guide](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_VPC.WorkingWithRDSInstanceinaVPC.html)).
- **Enabling a VPC connector is all-or-nothing for application egress.** "When you connect your service to a VPC, the outbound traffic doesn't have access to the public internet… your services can't access the public internet and AWS APIs" ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html)). For Compass this directly threatens the app's outbound call to the FMP market-data API. Restoring it costs a **NAT gateway at $0.045/hr + $0.045/GB (us-east-2, read 2026-07-27)** ([VPC pricing](https://aws.amazon.com/vpc/pricing/)) — roughly **$33/month before any traffic**, which likely exceeds the App Runner and RDS compute bill combined for a project this size.
- **App Runner's own control-plane traffic is exempt.** Source/image pulls, log pushes, and secret retrieval for referenced Secrets Manager / SSM env vars are done *by App Runner*, not by your container, and "isn't routed through your VPC" ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html)). So ECR and secret *injection* keep working without NAT. Your app calling the AWS SDK at runtime does not.
- **VPC connectors are immutable.** There is no `UpdateVpcConnector` in the App Runner API ([Actions list](https://docs.aws.amazon.com/apprunner/latest/api/API_Operations.html)), and you can't delete one that a service still uses ([DeleteVpcConnector](https://docs.aws.amazon.com/apprunner/latest/api/API_DeleteVpcConnector.html)). Changing subnets or security groups means create-new → `UpdateService` → delete-old.
- **Subnets must be private.** Public subnets are not merely discouraged — "your service fails to create or rolls back automatically during an update" ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html)).

---

## 1. Is a VPC connector required?

### Default (no connector)

App Runner's default egress is `EgressType: DEFAULT`. "By default, your AWS App Runner application can send messages to public endpoints… If you don't configure a VPC when you launch your environment, App Runner uses the default VPC, which is public." ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html)). That "default VPC" is an *App Runner-owned* VPC, not yours.

Consequence: with no connector, your container reaches RDS **only** if the DB instance is publicly reachable:

1. RDS `Publicly accessible` = **Yes** (console: **Connectivity → Additional connectivity configuration → Public access**). "If the DB instance is designated as publicly accessible, its DNS endpoint resolves to the private IP address from within the VPC. It resolves to the public IP address from outside of the VPC." ([RDS VPC guide](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_VPC.WorkingWithRDSInstanceinaVPC.html))
2. All subnets in the **DB subnet group** must be public, i.e. routed to an internet gateway. "For a DB instance to be publicly accessible, all of the subnets in its DB subnet group must be public." and "the subnets in the DB subnet group must have an internet gateway." (same page)
3. VPC attributes **DNS hostnames** and **DNS resolution** must be on. (same page)
4. The DB security group must have an inbound rule on TCP 5432 permitting the source. **This is the sticking point**: App Runner does not publish or let you pin the source IP range of default-egress traffic. There is no documented CIDR list and no `aws-apprunner` managed prefix list. The only rule that reliably works is `0.0.0.0/0` on 5432.

**So: the honest answer is that App Runner reaches RDS without a VPC connector only if the Postgres port is open to the entire internet.** That is a real configuration people ship, and Postgres auth + TLS is the only thing standing between the DB and the world. AWS documents the mechanism but does not document an App Runner source range that would let you narrow it.

### With a connector

`EgressConfiguration = { EgressType: "VPC", VpcConnectorArn: ... }` on `CreateService`/`UpdateService` ([EgressConfiguration](https://docs.aws.amazon.com/apprunner/latest/api/API_EgressConfiguration.html), [network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html)). Your container's outbound traffic now originates from Hyperplane ENIs inside your subnets, so the RDS security group can reference the connector's security group by ID — the normal, tight pattern.

---

## 2. Public vs private RDS — what each actually costs and requires

### Public RDS + no VPC connector

Requires:
- Public subnets + internet gateway for the whole DB subnet group.
- `Publicly accessible = Yes`, VPC DNS attributes on.
- Inbound 5432 from `0.0.0.0/0` (see above).

Costs:
- **Public IPv4 address charge: $0.005/hr for each in-use public IPv4 address.** The VPC pricing page explicitly names "Amazon RDS database that has one in-use public IPv4 address" as a charged example ([VPC pricing](https://aws.amazon.com/vpc/pricing/), read 2026-07-27). ≈ **$3.65/month**, permanently, purely for being public.
- No NAT gateway needed.
- App→DB traffic crosses the public internet path; data-transfer-out rates apply to the egress leg.

Breaks / risks:
- DB port exposed to internet-wide scanning. Compromise reduces to Postgres credentials.
- RDS uses **Elastic IPs from EC2's public pool**, `service_managed = "rds"`, and "IPs allocated by Amazon RDS do not count against your account's EIP limits" — but they are RDS-managed and **change** ([RDS IP addressing](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_VPC.WorkingWithRDSInstanceinaVPC.html)). Never IP-allowlist the DB endpoint; always use the DNS name.
- You cannot later attach a VPC connector and keep the DB public-only without rethinking egress (see §4).

### Private RDS + VPC connector

Requires:
- Private subnets in ≥2 AZs for the DB subnet group ("Each DB subnet group should have subnets in at least two Availability Zones"), and private subnets for the VPC connector.
- A VPC connector (private subnets only).
- SG rules in both directions (see §3).
- **A NAT gateway or interface VPC endpoints if the app needs anything on the public internet** — this is the real cost driver.

Costs:
- NAT gateway: **$0.045/hr + $0.045/GB processed** (us-east-2, read 2026-07-27) ([VPC pricing](https://aws.amazon.com/vpc/pricing/)). One NAT gateway ≈ **$32.85/month** before data. Data leaving to the internet is *additionally* charged at standard rates (the page's example uses $0.09/GB). A per-AZ NAT for HA multiplies the hourly charge.
- Interface VPC endpoints (PrivateLink), if you use them instead of NAT: **$0.01/hr per endpoint ENI per AZ**, plus **$0.01/GB** for the first 1 PB ([PrivateLink pricing](https://aws.amazon.com/privatelink/pricing/), read 2026-07-27). Two AZs × one endpoint ≈ $14.60/month per service you need. Cheaper than NAT *only* if you need a small, fixed set of AWS services and **no non-AWS internet** — endpoints cannot reach FMP.
- No public IPv4 charge for the DB.

Breaks / risks:
- All application outbound internet dies the moment you flip to VPC egress unless NAT/endpoints are already in place (§4, Gotcha 1).
- Debugging from your laptop requires a bastion, SSM Session Manager port forwarding, or a VPN — you can no longer `psql` the endpoint directly.

### IAM database authentication (either shape)

RDS Postgres supports IAM DB auth; the token is generated locally via SigV4 by the SDK (`RdsUtilities` / `generate-db-auth-token`) so **no extra network call is needed** — it works behind a VPC connector with no internet. Tokens live 15 minutes; traffic is forced over SSL/TLS; the IAM action is `rds-db:connect`. Caveats worth knowing: for PostgreSQL, granting `rds_iam` to a role makes IAM auth take **precedence over password auth** for that user (including the master user); you can't use a custom Route 53 record to generate the token; and it needs **300–1000 MiB extra memory on the DB instance**, which is significant on a `db.t4g.micro`. ([IAM DB auth](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/UsingWithRDS.IAMDBAuth.html))

---

## 3. Security-group and subnet implications

### Which SG attaches to what

| Resource | Field | What it controls |
|---|---|---|
| VPC connector | `SecurityGroups` on `CreateVpcConnector` (console: **Add new VPC connector → Security group**) | The SGs applied to the Hyperplane ENIs. **Outbound** rules gate where your app can talk. Optional — "If not specified, App Runner uses the default security group of the Amazon VPC. The default security group allows all outbound traffic." |
| RDS DB instance | `--vpc-security-group-ids` | **Inbound** rules gate who may connect on 5432. |

Rules needed:
- **Connector SG egress**: allow TCP 5432 to the RDS SG (or to the DB subnet CIDRs). If you also need NAT-bound internet, allow 443 to `0.0.0.0/0` too.
- **RDS SG ingress**: allow TCP 5432 with **source = the connector's security group ID**. The docs are explicit that this is your responsibility: "You must also ensure that any security groups that are associated with the destination resource have the appropriate inbound rules. Otherwise, these resources can't accept traffic that comes from the VPC Connector security groups." ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html))
- Security groups are stateful — no explicit return rule is needed. **Network ACLs are not**, so if you've customised NACLs you need the ephemeral-port return range.

### Subnets

- **Private only.** "When selecting a subnet for your VPC, ensure that you choose a private subnet, not a public subnet… If you choose a public subnet, errors will occur when running your App Runner service." And, more bluntly, in the console procedure: "If you assign public subnets to VPC connector, your service fails to create or rolls back automatically during an update." ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html))
- Why: "the App Runner service creates a Hyperplane ENI in each of the subnets. Each Hyperplane ENI is assigned a private IP address only" and is tagged with an **`AWSAppRunnerManaged`** key. ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html))
- **AZ spread**: AWS recommends subnets across at least three AZs, or all supported AZs if the region has fewer.
- **All selected subnets must have the same IP address type** — either IPv4 or dual-stack. ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html), [CreateVpcConnector](https://docs.aws.amazon.com/apprunner/latest/api/API_CreateVpcConnector.html))
- **The connector subnets and the DB subnets do not have to be the same subnets** — they just need routing and SG rules between them. They must be in the same VPC as each other for the simple case (`CreateVpcConnector` "Specify IDs of subnets of a single Amazon VPC").
- RDS side: DB subnet group needs ≥2 subnets in ≥2 AZs; "reserve at least one address in each subnet in the DB subnet group for use by Amazon RDS for recovery actions." ([RDS VPC guide](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_VPC.WorkingWithRDSInstanceinaVPC.html))

### IP consumption

Better than you'd fear. "one Hyperplane ENI per subnet in the customer VPC", shared across Fargate tasks in that AZ and across services that reuse the same connector configuration, so "typically only a handful of ENIs are required per service, even as we scale up the number of Fargate tasks" ([Deep Dive on AWS App Runner VPC Networking](https://aws.amazon.com/blogs/containers/deep-dive-on-aws-app-runner-vpc-networking/), AWS Containers Blog). A connector's identity is the **security-group + subnet combination**, which is what determines ENI reuse ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html)).

---

## 4. Gotchas that bite late

**1. Enabling a VPC connector silently kills your app's internet access.** — *Severity: high. Bites: the moment you flip egress to VPC, or later when you add an outbound API call.*
"When you connect your service to a VPC, the outbound traffic doesn't have access to the public internet. All outbound traffic from your application is directed through the VPC that your service is connected to… This means that your services can't access the public internet and AWS APIs." ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html)). Compass calls FMP for base facts — that call dies unless a NAT gateway exists. Fixes documented by AWS: NAT gateway, or interface VPC endpoints (AWS services only; no help for FMP).

**2. But App Runner's *own* traffic is exempt, which makes the failure confusing.** — *Severity: medium (diagnostic trap). Bites: during triage.*
"App Runner manages several actions on your behalf, such as pulling source code and images, pushing logs, and retrieving secrets. The traffic that these actions generate isn't routed through your VPC." ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html)). So deploys succeed, ECR pulls succeed, CloudWatch logs flow, and Secrets Manager env-var *injection* works — while your app's own SDK/HTTP calls hang and time out. The healthy control plane makes it look like networking is fine.

**3. NAT gateway is the single biggest recurring cost in this architecture.** — *Severity: high (financial). Bites: first full month's bill.*
$0.045/hr + $0.045/GB in us-east-2 (read 2026-07-27) ≈ $33/month idle ([VPC pricing](https://aws.amazon.com/vpc/pricing/)). Per-AZ NAT for HA multiplies it. There is no free tier for NAT gateways. This is plausibly more than the App Runner instance and the `db.t4g.micro` combined.

**4. VPC connectors are immutable — no `UpdateVpcConnector` exists.** — *Severity: medium. Bites: the first time you need to add a security group or an AZ.*
The API Actions list has `CreateVpcConnector`, `DeleteVpcConnector`, `DescribeVpcConnector`, `ListVpcConnectors` — and no update ([Actions](https://docs.aws.amazon.com/apprunner/latest/api/API_Operations.html)). The `VpcConnector` object carries a `VpcConnectorRevision`, but "At this time, App Runner supports only one revision per name" ([VpcConnector](https://docs.aws.amazon.com/apprunner/latest/api/API_VpcConnector.html)). And "You can't delete a connector that's used by one or more App Runner services" ([DeleteVpcConnector](https://docs.aws.amazon.com/apprunner/latest/api/API_DeleteVpcConnector.html)). So the change sequence is: create a *differently named* connector → `UpdateService` to point at it → delete the old one. In Terraform/CDK this shows up as forced replacement with an ordering constraint; plan for it.

**5. A public subnet in the connector makes the service fail to create — or roll back on update.** — *Severity: high. Bites: at create time, or worse, on a later update of a working service.*
"If you assign public subnets to VPC connector, your service fails to create or rolls back automatically during an update." ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html)). The rollback case is the nasty one: a connector built from a subnet that someone later added an IGW route to.

**6. Some AZs in some regions simply don't support App Runner connector subnets.** — *Severity: medium. Bites: at create/update, possibly long after you picked the subnets.*
"Some Availability Zones in some AWS Regions don't support the subnets that can be used with App Runner services. If you choose subnets in these Availability Zones, your service fails to be created or updated." AWS returns an error naming the unsupported subnets; the fix is to drop them. ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html))

**7. Mixed IP address types across connector subnets are rejected.** — *Severity: low. Bites: when someone adds an IPv6 CIDR to one subnet.*
"The subnets you select must all have the same IP address type, either IPv4 or dual-stack." ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html))

**8. Public RDS costs $0.005/hr for its public IPv4 address, forever.** — *Severity: low (financial), but invisible. Bites: every month.*
The VPC pricing page names RDS explicitly as a charged case ([VPC pricing](https://aws.amazon.com/vpc/pricing/), read 2026-07-27). ≈ $3.65/month.

**9. Secrets referenced as env vars are pulled only at deploy time.** — *Severity: medium. Bites: on the first credential rotation.*
"When the secrets and parameter values are rotated or changed they are not automatically updated in your App Runner service. Redeploy your App Runner service as App Runner only pulls secrets and parameters during deployment." ([Referencing environment variables](https://docs.aws.amazon.com/apprunner/latest/dg/env-variable.html)). If instead you avoid this by calling Secrets Manager from the app via the SDK — which the same page offers as an option — you re-enter Gotcha 1 and need NAT or a Secrets Manager interface endpoint.

**10. First deploy with a connector adds 2–5 minutes of startup latency, once per SG+subnet combination.** — *Severity: low. Bites: first deploy; and again after any connector change (Gotcha 4), because it's a new combination.*
"it may experience a one-time startup latency of two to five minutes. The startup process waits until the VPC Connector is ready… For a given VPC connector configuration, the latency only happens once, during the initial creation of the VPC Connector Hyperplane ENIs." ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html))

**11. Health check defaults are aggressive and default to TCP, which hides DB failures — then a switch to HTTP exposes them.** — *Severity: medium. Bites: when you add a `/health` endpoint that touches the DB.*
Defaults: **Protocol `TCP`, Path `/`, Interval 5s, Timeout 2s, Healthy threshold 1, Unhealthy threshold 5** ([Configuring health checks](https://docs.aws.amazon.com/apprunner/latest/dg/manage-configure-healthcheck.html)). A TCP check passes while the DB is unreachable. Switch to HTTP against a readiness endpoint that opens a Postgres connection, and a 2-second timeout is easily blown by connection establishment plus TLS on a cold pool — the deploy then fails. Raise `Timeout` if the health path touches the DB.

**12. Deployments temporarily double your instance count.** — *Severity: low (financial + capacity). Bites: on every deploy, and on AWS-initiated maintenance.*
"App Runner temporarily doubles the number of provisioned instances during deployments, to maintain the same capacity for both old and new code." ([CreateAutoScalingConfiguration](https://docs.aws.amazon.com/apprunner/latest/api/API_CreateAutoScalingConfiguration.html)). Separately, App Runner does blue/green migrations on hardware issues: "You might occasionally see a slight temporary increase in charges." ([architecture](https://docs.aws.amazon.com/apprunner/latest/dg/architecture.html)). Double the instances means double the DB connections — relevant on a `db.t4g.micro` with a small `max_connections`, especially with `MaxSize` defaulting to 25.

**13. Maintenance windows block your API calls.** — *Severity: low. Bites: in CI when a deploy randomly fails.*
"When maintenance is in progress, service status temporarily changes to `OPERATION_IN_PROGRESS`… Actions on your service (for example, deployment, configuration update, pause/resume, or deletion) are blocked during this time." ([architecture](https://docs.aws.amazon.com/apprunner/latest/dg/architecture.html)). CI needs retry logic.

**14. VPC connector quota is 10 per region (adjustable); VPC ingress connections is 1 and *not* adjustable.** — *Severity: low. Bites: with connector churn from Gotcha 4.*
([App Runner quotas](https://docs.aws.amazon.com/general/latest/gr/apprunner.html))

**15. No `MinSize: 0`.** — *Severity: medium (financial). Bites: immediately, and forever on a low-traffic app.*
`MinSize` has a valid range of 1–25, default 1 ([CreateAutoScalingConfiguration](https://docs.aws.amazon.com/apprunner/latest/api/API_CreateAutoScalingConfiguration.html)). App Runner cannot scale to zero. See §5.

---

## 5. Is App Runner still the right primitive?

Facts a reader should have before committing. **Not a recommendation.**

### Closed to new customers

Every App Runner Developer Guide page now shows: *"AWS App Runner is no longer open to new customers. Existing customers can continue to use the service as normal."* The availability page states: "After careful consideration, we decided to close AWS App Runner to new customers… AWS continues to invest in security and availability for AWS App Runner, but we do not plan to introduce new features." AWS recommends **Amazon ECS Express Mode** as the migration target — "With a single API call, you provide a container image and two IAM roles, and Amazon ECS provisions a complete application stack in your AWS account, including an ECS service on Fargate, an Application Load Balancer, auto scaling, and networking. There is no additional charge for using Amazon ECS Express Mode." ([availability change](https://docs.aws.amazon.com/apprunner/latest/dg/apprunner-availability-change.html))

Note a **date discrepancy in AWS's own docs** as read on 2026-07-27: the API Reference banner says "starting **March 31, 2026**" ([CreateVpcConnector](https://docs.aws.amazon.com/apprunner/latest/api/API_CreateVpcConnector.html)), the Release Notes banner says "starting **April 30, 2026**" ([relnotes](https://docs.aws.amazon.com/apprunner/latest/relnotes/welcome.html)), and the Developer Guide banner uses the past tense. Either way the window has closed. **Action item for whoever owns the decision ticket: try `aws apprunner list-services` / attempt a create in the target account to establish whether this account is grandfathered in.** AWS has not published a definition of "existing customer" that I could find.

### Pricing model (read 2026-07-27, [App Runner pricing](https://aws.amazon.com/apprunner/pricing/))

- **Provisioned (idle) container instances: $0.007/GB-hour** — memory only. us-east-1, us-east-2, us-west-2, eu-west-1.
- **Active container instances: $0.064/vCPU-hour + $0.007/GB-hour**, billed per second, rounded up, with a **one-minute minimum for vCPU** when processing starts.
- Tokyo (ap-northeast-1) differs: $0.009/GB-hour, $0.081/vCPU-hour.
- Automatic deployments carry a **per-application monthly fee**; source-code deployments carry a **build fee** based on build time. (Container-image deploys avoid the build fee.)
- Supported configs run 0.25 vCPU / 0.5 GB up to 4 vCPU / 12 GB ([architecture](https://docs.aws.amazon.com/apprunner/latest/dg/architecture.html)).

The API reference states the model precisely: "You pay for memory usage of all the provisioned instances. You pay for CPU usage of only the active subset." ([CreateAutoScalingConfiguration](https://docs.aws.amazon.com/apprunner/latest/api/API_CreateAutoScalingConfiguration.html))

**Practical floor for Compass:** smallest config (0.25 vCPU / 0.5 GB), `MinSize: 1` → 0.5 GB × $0.007 × ~730 h ≈ **$2.56/month** even at zero traffic, plus active-instance charges whenever requests arrive. Cheap. The NAT gateway (Gotcha 3) dwarfs it by ~13×.

### Cold starts / scale-to-zero

App Runner **cannot scale to zero** (`MinSize` min 1). AWS frames the provisioned tier as the anti-cold-start mechanism: idle instances are "a cost-effective compute capacity reserve and are ready to be quickly activated." That's a positive for latency and a negative for a hobby-scale bill. Note that .NET app-level cold start (JIT, first EF Core query, connection pool warm-up) still applies on scale-out and on every deploy.

### Regional availability

11 regions only: us-east-1, us-east-2, us-west-2, ap-south-1, ap-southeast-1, ap-southeast-2, ap-northeast-1, eu-central-1, eu-west-1, eu-west-2, eu-west-3. Notably **no ap-southeast-2-adjacent NZ region, no ca-*, no sa-*, no us-west-1**. ([App Runner endpoints and quotas](https://docs.aws.amazon.com/general/latest/gr/apprunner.html))

### Other structural notes relevant to the Compass plan

- The CloudFront `/api/*` → App Runner path-routing plan works because incoming traffic is unaffected by VPC egress config: "Incoming messages that your application receives are unaffected by an associated VPC. The messages are routed through the public domain name that's associated with your service and don't interact with the VPC." ([network-vpc](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html))
- Relevant to the auth-cookie design: **`*.awsapprunner.com` is on the Public Suffix List**, and AWS recommends `__Host-` prefixed cookies on the default domain ([architecture](https://docs.aws.amazon.com/apprunner/latest/dg/architecture.html)). With a custom domain behind CloudFront this matters less, but it constrains anything that relies on the default App Runner hostname.
- **App Runner does not support making your service itself reachable via a VPC endpoint you create for it** — "App Runner doesn't support creating a VPC endpoint for your application"; private ingress is a separate `VpcIngressConnection` resource, quota 1 per service, not adjustable ([security-vpce](https://docs.aws.amazon.com/apprunner/latest/dg/security-vpce.html), [quotas](https://docs.aws.amazon.com/general/latest/gr/apprunner.html)).
- If connection count becomes a problem (Gotcha 12), **RDS Proxy** sits in the VPC and would be reached through the same connector. It's "priced per vCPU per hour" of the underlying instance with "a 10-minute minimum charge" ([RDS Proxy pricing](https://aws.amazon.com/rds/proxy/pricing/)) — I could not extract the us-east-1 per-vCPU rate from the rendered page.

### RDS free-tier interaction

AWS has moved off the classic 12-month free tier. Per the [RDS free tier page](https://aws.amazon.com/rds/free/) (read 2026-07-27): accounts activated **before July 15, 2025** keep "select RDS instances for free up to 12 months after your sign-up date"; newer accounts instead get a credit-based Free plan (~$100 at sign-up plus up to $100 earned). Free-tier-eligible classes are `db.t3.micro` and `db.t4g.micro` for MySQL, PostgreSQL, MariaDB, and SQL Server Express. **Free tier does not depend on public accessibility** — a private DB is equally eligible, and going private *saves* the public IPv4 charge. Note that credits, unlike the old free tier, are consumed by the NAT gateway too.

---

## Sources

Primary AWS documentation, all fetched 2026-07-27:

- [Enabling VPC access for outgoing traffic — App Runner Developer Guide](https://docs.aws.amazon.com/apprunner/latest/dg/network-vpc.html) — the load-bearing page for §1–§4
- [Networking with App Runner](https://docs.aws.amazon.com/apprunner/latest/dg/network.html)
- [When the service fails to connect to Amazon RDS or downstream service](https://docs.aws.amazon.com/apprunner/latest/dg/troubleshooting.vpc-outbound-connect-failure.html)
- [AWS App Runner availability change](https://docs.aws.amazon.com/apprunner/latest/dg/apprunner-availability-change.html)
- [App Runner architecture and concepts](https://docs.aws.amazon.com/apprunner/latest/dg/architecture.html)
- [Configuring health checks for your service](https://docs.aws.amazon.com/apprunner/latest/dg/manage-configure-healthcheck.html)
- [Referencing environment variables](https://docs.aws.amazon.com/apprunner/latest/dg/env-variable.html)
- [Using App Runner with VPC endpoints](https://docs.aws.amazon.com/apprunner/latest/dg/security-vpce.html)
- [About AWS App Runner release notes](https://docs.aws.amazon.com/apprunner/latest/relnotes/welcome.html)
- [App Runner API — Actions](https://docs.aws.amazon.com/apprunner/latest/api/API_Operations.html)
- [CreateVpcConnector](https://docs.aws.amazon.com/apprunner/latest/api/API_CreateVpcConnector.html) / [DeleteVpcConnector](https://docs.aws.amazon.com/apprunner/latest/api/API_DeleteVpcConnector.html) / [VpcConnector type](https://docs.aws.amazon.com/apprunner/latest/api/API_VpcConnector.html) / [EgressConfiguration](https://docs.aws.amazon.com/apprunner/latest/api/API_EgressConfiguration.html)
- [CreateAutoScalingConfiguration](https://docs.aws.amazon.com/apprunner/latest/api/API_CreateAutoScalingConfiguration.html)
- [AWS App Runner endpoints and quotas — AWS General Reference](https://docs.aws.amazon.com/general/latest/gr/apprunner.html)
- [Working with a DB instance in a VPC — Amazon RDS User Guide](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_VPC.WorkingWithRDSInstanceinaVPC.html)
- [IAM database authentication — Amazon RDS User Guide](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/UsingWithRDS.IAMDBAuth.html)
- [Deep Dive on AWS App Runner VPC Networking — AWS Containers Blog](https://aws.amazon.com/blogs/containers/deep-dive-on-aws-app-runner-vpc-networking/) (official AWS blog)

Pricing pages, all read 2026-07-27:

- [AWS App Runner pricing](https://aws.amazon.com/apprunner/pricing/)
- [Amazon VPC pricing](https://aws.amazon.com/vpc/pricing/) — NAT gateway, public IPv4
- [AWS PrivateLink pricing](https://aws.amazon.com/privatelink/pricing/)
- [Amazon RDS free tier](https://aws.amazon.com/rds/free/)
- [Amazon RDS Proxy pricing](https://aws.amazon.com/rds/proxy/pricing/)

---

## Uncertain / not first-party

Things I could not nail down in AWS docs. Treat as open questions, not facts.

1. **What exactly counts as an "existing customer" for App Runner.** AWS says existing customers may continue "including creating new resources and services" but publishes no definition (account that ever created a service? account with an active service on the cutoff date? organization-level?). **Verify empirically in the target account.**
2. **The cutoff date.** AWS's own banners disagree — March 31, 2026 (API Reference) vs April 30, 2026 (Release Notes) vs past tense (Developer Guide), all as rendered on 2026-07-27. Moot for today's purposes but worth noting if anyone cites a date.
3. **Source IP ranges for App Runner default (non-VPC) egress.** I found no AWS-published CIDR list or managed prefix list for App Runner outbound. This is why the public-RDS-without-connector path effectively requires `0.0.0.0/0` on 5432. Absence of documentation, not documented absence — if narrowing matters, ask AWS Support.
4. **Whether AWS Free Tier includes any hours of public IPv4 address usage** that would offset the $0.005/hr RDS public-IP charge. The VPC pricing page's free-tier text did not come through in the fetch. Verify on the pricing page directly before treating the $3.65/month as unavoidable.
5. **Exact us-east-1 RDS Postgres on-demand rates** for `db.t4g.micro` / `db.t3.micro` and gp3 storage. The RDS PostgreSQL pricing page renders its tables via JavaScript and the numbers were not extractable; use the AWS Pricing Calculator.
6. **Exact RDS Proxy per-vCPU-hour rate** in us-east-1 — same rendering problem.
7. **Whether the FMP integration is actually invoked server-side from the API.** I asserted this based on `docs/glossary.md` ("Base Facts… Fetched live from FMP (ADR 0005)"), but did not read the code. If FMP is called from the browser rather than the API, **Gotcha 1 and the entire NAT gateway cost line may not apply**, which materially changes the economics. Worth confirming before the decision ticket.
8. **Whether ECS Express Mode has an equivalent all-or-nothing egress constraint.** Not investigated — out of scope for this ticket, but it's the obvious follow-up question given §5.
