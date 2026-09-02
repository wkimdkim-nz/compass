# Research — Which Amazon ECS shape for Compass Phase 0

Answers GitHub issue [#18](https://github.com/wkimdkim-nz/compass/issues/18) (wayfinder research ticket). Research date: **2026-09-02**. Every AWS documentation page cited was fetched live on that date; every price was read on that date from either the named AWS pricing page or the **AWS Price List Bulk API** (`https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/<service>/current/ap-southeast-2/index.json`), which is first-party and region-specific. This note reports **facts only** — the mode choice and the networking call belong to [#10](https://github.com/wkimdkim-nz/compass/issues/10).

⚠️ **This note does not carry over [#8](https://github.com/wkimdkim-nz/compass/issues/8)'s conclusions.** #8 studied AWS App Runner. Where a claim of its was App Runner-specific, this note says so and gives the ECS answer. The single most important reversal is in §3.

---

## Bottom line

- **App Runner's control-plane exemption does not exist on ECS.** #8's load-bearing finding — "App Runner manages several actions on your behalf, such as pulling source code and images, pushing logs, and retrieving secrets… that traffic isn't routed through your VPC" — has **no ECS equivalent**. AWS states the opposite for Fargate: *"For a Fargate task in a private subnet to pull container images, you need a NAT gateway in the subnet to route requests to the internet"*, or ECR interface endpoints ([Fargate task networking](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/fargate-tasks-services.html)). **A private, NAT-less, endpoint-less ECS task cannot start.** The cheap-and-safe shape #8 handed #10 does not survive the move.
- **Express Mode is a real, distinct API** — `CreateExpressGatewayService` / `UpdateExpressGatewayService` / `DescribeExpressGatewayService` / `DeleteExpressGatewayService` ([ECS API Operations](https://docs.aws.amazon.com/AmazonECS/latest/APIReference/API_Operations.html)) — but everything it makes is an ordinary ECS/ELB/Application-Auto-Scaling resource in your account, editable directly. It is a provisioning shortcut with a managed lifecycle, not a separate runtime.
- **The ALB is unavoidable and is the dominant fixed cost.** In `ap-southeast-2` an Application Load Balancer is **$0.0252/hr ≈ $18.40/month** before a single request (Price List, `AWSELB`, read 2026-09-02). Express Mode always provisions one; a hand-rolled ECS service needs one anyway to give CloudFront a stable origin. An **internet-facing** ALB adds **≥ 2 in-use public IPv4 addresses** (one per enabled AZ, minimum two AZs) at $0.005/hr each ≈ **$7.30/month**.
- **Cheapest workable Phase 0 shape is ≈ $40/month; the private shapes are $58–$86/month.** Against App Runner's **$2.56/month** floor that is **16×–34×**. Full arithmetic in §2 and §3.
- **`ap-southeast-2` is supported** for Express Mode (docs: *"available in all AWS Regions where Amazon ECS and Fargate are supported"*), for Fargate (it has published Fargate rates), and for **CloudFront VPC origins** (named explicitly in the supported-Regions table).
- **Scale-to-zero is genuinely available on ECS** — Application Auto Scaling documents `MinCapacity: 0` as valid for ECS services — but scaling *back out* from zero on the CPU metric Express Mode defaults to is not something AWS documents as working. See §4; this is the largest single "cannot establish" in this note.

---

## 1. The modes

### What the choices actually are

| Shape | What it is | Provisioning surface |
|---|---|---|
| **ECS Express Mode service** ("Express Gateway service") | Four dedicated ECS API operations that provision and then *manage* a conventional stack: ECS service on Fargate, ALB + HTTPS listener + listener rules + target groups, security groups, Application Auto Scaling target + target-tracking policy, CloudWatch log group, deployment-failure metric alarm, and an **ACM certificate**. | `create-express-gateway-service` needs only an image, a task execution role and an infrastructure role. |
| **Standard ECS service on Fargate** | `CreateService` + `RegisterTaskDefinition` + you build the ALB, target group, listener, scaling policy, log group and security groups yourself. | Everything explicit. |
| **ECS Managed Instances** | ECS-managed **EC2** capacity: standard EC2 instance price **plus a per-instance management fee** ([ECS Managed Instances pricing](https://aws.amazon.com/ecs/managed-instances/pricing/)). | Named for completeness; for one always-on 0.25-vCPU task it adds a whole EC2 instance plus a fee. |
| **ECS on EC2 (self-managed capacity)** | You run the instances. | Named for completeness. |

### Is Express Mode a distinct API or a shortcut?

**Both, in a specific way.** It has its own API operations and its own resource identity — deleting an Express Mode service deletes resources a plain `DeleteService` would leave behind — but the artefacts are ordinary. The docs are explicit: *"All underlying AWS resources remain accessible for direct management when you need fine-grained control or advanced features"* and *"All resources are created in your AWS account with full visibility and accessibility through the AWS console and APIs"* ([Express Mode overview](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-overview.html)).

The [resources page](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-work.html) splits every default into two classes, and this distinction is the practical shape of the abstraction:

- **Configurable through Express Mode:** cluster, cpu, memory, container port, health-check path, environment variables and secrets, command, task role, subnets, security groups, scaling metric/target/min/max, log group and stream prefix, tags, and (alternatively) a whole custom task definition ARN.
- **Set by Express Mode but changeable only on the underlying resource:** `networkMode: awsvpc`, `cpuArchitecture: X86_64`, `operatingSystemFamily: LINUX`, `platformVersion: LATEST`, `availabilityZoneRebalancing: true`, `enableECSManagedTags`, `healthCheckGracePeriodSeconds: 300`, `versionConsistency: enabled`, `initProcessEnabled: true`, `stopTimeout: 30`, ALB `desync-mitigation-mode: Off`, ALB access logs off, listener `https`/`443`/`host-header` rules, target-group protocol `HTTP`/`HTTP1`, target type `ip`, health-check interval 30s / timeout 5s / healthy 5 / unhealthy 2.
- **Not changeable at all:** *"deployment strategy can not be updated on Express Mode services"* and *"load balancer configurations can not be updated on Express Mode services."*

### What it hides that matters here

- **Deployment strategy is canary and fixed.** *"5% of traffic is shifted to the new environment… Alarm Based Rollbacks will trigger if sum of 4xx and 5xx errors percentage is > 1 for 2 datapoints within 3 minutes… After 3 minutes bake time, 100% of traffic is shifted… After 3 minutes bake time for monitoring, old tasks are gradually stopped"* ([Updating an Express Mode service](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-update-full.html)). **This is the ECS analogue of #8's Gotcha 12**: old and new tasks run concurrently for ~6+ minutes per deploy, so Fargate cost and **Postgres connection count** both roughly double during a deploy. On a `db.t4g.micro` that is worth sizing the pool for.
- **A custom task definition is accepted** (`--task-definition-arn`) but then `primaryContainer`, `executionRoleArn`, `taskRoleArn`, `cpu` and `memory` may not be passed in the same call, and the task definition must contain a container literally named **`Main`** with a single TCP port mapping carrying both a container port and a **port name**, and `FARGATE` compatibility.
- **Renaming the primary container breaks Express Mode.** *"Changing the name of the default container can effect Express Mode's ability to make subsequent updates to your service."*

### Documentation discrepancies found (report, do not resolve)

1. **Default CPU/memory.** The API reference says *"The default value for an Express service is 256 (.25 vCPU)"* and *"512 MiB"* ([CreateExpressGatewayService](https://docs.aws.amazon.com/AmazonECS/latest/APIReference/API_CreateExpressGatewayService.html)). The Developer Guide says `cpu: 1024`, `memory: 2048` and the CLI tutorial says the minimal call yields *"1 vCPU and 2 GB memory"*. **That is a 4× cost difference** ($10.80/mo vs $43.22/mo — §2). Whichever is right, **pass `--cpu` and `--memory` explicitly.**
2. **Default health-check path.** API reference: *"If not specified, the default health check path is `/ping`."* Developer Guide target-group defaults: *"health-check-path: (default `/`)"*.
3. **Log-group retention on delete.** Best practices: *"CloudWatch Log Groups created by Express Mode are configured to never expire and are retained when the Express Mode service is deleted."* The delete page lists *"CloudWatch log group"* among resources that *"may be deleted depending on your selection."*

---

## 2. Cost at Compass's scale, in `ap-southeast-2`

### Published rates (Price List Bulk API, region `ap-southeast-2`, read 2026-09-02)

| Item | Rate | Source offer file |
|---|---|---|
| Fargate vCPU (Linux/x86) | **$0.048560 / vCPU-hour** | `AmazonECS` |
| Fargate memory (Linux/x86) | **$0.005320 / GB-hour** | `AmazonECS` |
| Fargate vCPU (Linux/**ARM**) | **$0.038850 / vCPU-hour** | `AmazonECS` |
| Fargate memory (Linux/**ARM**) | **$0.004260 / GB-hour** | `AmazonECS` |
| Fargate ephemeral storage | $0.000133 / GB-hour (above the included allowance) | `AmazonECS` |
| **Application Load Balancer** | **$0.0252 / hour** + **$0.008 / LCU-hour** | `AWSELB` |
| Network Load Balancer | $0.0252 / hour + $0.006 / LCU-hour | `AWSELB` |
| **NAT gateway** | **$0.059 / hour** + **$0.059 / GB processed** | `AmazonEC2` |
| **Interface VPC endpoint** | **$0.013 / VPC-endpoint-hour** (per ENI, i.e. per AZ) + $0.01 / GB | `AmazonVPC` |
| **In-use public IPv4 address** | **$0.005 / hour** | `AmazonVPC` |
| ECR private storage | $0.10 / GB-month | `AmazonECR` |
| CloudWatch Logs ingestion (Standard class) | $0.67 / GB | `AmazonCloudWatch` |
| RDS `db.t4g.micro` PostgreSQL, Single-AZ | $0.025 / hour (≈ $18.25/mo) | `AmazonRDS` |
| Gateway VPC endpoint (S3) | **$0** — *"There is no additional charge for using gateway endpoints"* | [Gateway endpoints](https://docs.aws.amazon.com/vpc/latest/privatelink/gateway-endpoints.html) |
| Express Mode itself | **$0** — *"There is no additional charge for using an Amazon ECS Express Mode service"* | [Express Mode overview](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-overview.html) |
| CloudFront VPC origins | **$0** — *"There is no additional cost for using VPC origins with CloudFront"* | [What's New, 2024-11-20](https://aws.amazon.com/about-aws/whats-new/2024/11/amazon-cloudfront-vpc-origins/) |

> Note the ap-southeast-2 NAT gateway rate is **$0.059/hr, not the $0.045/hr** #8 quoted from us-east-2. **One NAT gateway in Sydney is $43.07/month idle**, not $33.

### Fargate task cost at 730 hours/month

| Size | Arch | Monthly |
|---|---|---|
| 0.25 vCPU / 0.5 GB | x86 | **$10.80** |
| 0.25 vCPU / 0.5 GB | ARM | **$8.65** |
| 0.5 vCPU / 1 GB | x86 | $21.61 |
| **1 vCPU / 2 GB** (Developer Guide's stated Express default) | x86 | **$43.22** |

Express Mode sets `cpuArchitecture: X86_64` and lists it as changeable only on the task definition — so the ARM rate is reachable, but only via a custom task definition or by editing the generated one.

### ALB, honestly

- $0.0252 × 730 = **$18.40/month** for existing at all.
- **LCUs are extra and usage-based.** *"You are charged only on the dimension with the highest usage"*, where one LCU = 25 new connections/s, 3,000 active connections/min, 1 GB/hour processed, or 1,000 rule evaluations/s ([ELB pricing](https://aws.amazon.com/elasticloadbalancing/pricing/)). At Compass's traffic this is a small fraction of an LCU-hour, but **AWS does not publish a minimum LCU charge**, so treat it as "small, non-zero, unquantified".
- **Sharing does not help Compass.** *"Up to 25 Express Mode services in the same VPC can share an Application Load Balancer… This sharing reduces the effective cost per application as you deploy more Express Mode services."* Compass has one service, so it pays the whole $18.40.
- **Internet-facing adds public IPv4.** *"The Application Load Balancer has one IP address per enabled Availability Zone"* and *"You must select at least two Availability Zone subnets"* ([ALB overview](https://docs.aws.amazon.com/elasticloadbalancing/latest/application/application-load-balancers.html)). The [VPC pricing page](https://aws.amazon.com/vpc/pricing/) example bills *"One Elastic load balancer with two in-use public IPv4 address"*. **2 × $0.005 × 730 = $7.30/month.** An **internal** ALB has no public IPv4 and no such charge.

### Comparison against App Runner's floor

App Runner's Phase 0 floor, per #8: 0.25 vCPU / 0.5 GB, `MinSize: 1`, provisioned-instance memory only → **≈ $2.56/month**, with **no NAT needed in Phase 0** (nothing in Phase 0 egresses).

| | App Runner (from #8) | ECS, public-subnet shape | ECS, private + NAT | ECS, private + endpoints (2 AZ) |
|---|---|---|---|---|
| Compute | $2.56 | $10.80 | $10.80 | $10.80 |
| Load balancer | included | $18.40 | $18.40 | $18.40 |
| Public IPv4 | $0 | $10.95 (2 ALB + 1 task) | $0 | $0 |
| Egress plumbing | $0 | $0 | $43.07 | $56.94 (3 endpoints) |
| **Monthly, idle** | **$2.56** | **$40.15** | **$72.27** | **$86.14** |

Add, in every column alike: RDS `db.t4g.micro` ≈ $18.25, Route 53 hosted zone $0.50, ECR storage ≈ $0.05–0.20, CloudWatch Logs ingestion, CloudFront requests. **A ~$200 credit balance buys roughly 3 months of the cheapest ECS shape** versus roughly 9 months of the App Runner shape, on these numbers.

### Teardown story

**Deleting an Express Mode service deletes the expensive parts.** Per the [delete page](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-delete-task.html), `DeleteExpressGatewayService` removes: the ECS service, task definition and tasks; the service security group; the CloudWatch log group; the metric alarm; the **ACM certificate**; and *"The Application Load Balancer (if no other services are configured), target group, security group, listener, and listener rule"*; plus the auto-scaling policy and scalable target, and the cluster if nothing else runs in it. That is the $18.40 ALB **and** the Fargate charge gone.

- *"if the Express Mode service created the load balancer and is the only service using the load balancer it will delete the associated load balancer. If the load balancer is being shared, even with resources not managed by Express Mode, it will not be deleted."*
- Contrast plain `DeleteService`: *"Amazon ECS will delete the service if there are no running tasks. Amazon ECS does not delete target groups associated with the service or the default cluster"* — **the ALB you built by hand survives and keeps billing.** A hand-rolled stack needs its own teardown script.
- **Scaling to zero tasks is not teardown**: it stops the $10.80 but the ALB's $18.40 continues.
- **Persists across teardown regardless:** the ECR repository and images ($0.10/GB-month), RDS (instance + storage), the Route 53 hosted zone ($0.50/month), the CloudFront distribution, and the IAM roles and OIDC provider.
- **Not established:** whether recreating an Express Mode service with the same `serviceName` yields the *same* `<name>.ecs.<region>.on.aws` URL. The CLI tutorial's generated example (`app-23d97h88`) carries a random suffix; the docs say `serviceName` *"can only be configured on create"*. If the URL moves, a CloudFront origin pointing at it breaks on every teardown/rebuild cycle. See §9.

---

## 3. Task networking — the load-bearing question

### The exemption is gone

#8, on App Runner: *"App Runner manages several actions on your behalf, such as pulling source code and images, pushing logs, and retrieving secrets. The traffic that these actions generate isn't routed through your VPC."* That is why a private, NAT-less App Runner service still deployed.

**ECS on Fargate is the opposite, and AWS says so plainly** ([Fargate task networking](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/fargate-tasks-services.html)):

> *"Amazon ECS tasks for AWS Fargate require the `awsvpc` network mode, which provides each task with an elastic network interface… For a Fargate task in a public subnet to pull container images, a public IP address needs to be assigned to the task's elastic network interface, with a route to the internet or a NAT gateway that can route requests to the internet. **For a Fargate task in a private subnet to pull container images, you need a NAT gateway in the subnet to route requests to the internet.** When you host your container images in Amazon ECR, you can configure Amazon ECR to use an interface VPC endpoint. In this case, the task's private IPv4 address is used for the image pull."*

So the image pull leaves from **your** ENI, in **your** subnet, under **your** security group and route table. Same for the `awslogs` driver and for secret injection (§7). **A private subnet with no NAT and no VPC endpoints cannot start a task at all** — the failure is at task start (`CannotPullContainerError`-class), not at request time.

One nuance worth recording, from the [ECS VPC endpoints page](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/vpc-endpoints.html): *"When there is an VPC endpoint for `ecr.dkr` and `ecr.api` in the same VPC where a Fargate task is deployed into, it will use the VPC endpoint. If there is no VPC endpoint, it will use the Fargate interface."* This is **not** an App-Runner-style exemption — "the Fargate interface" is the task ENI, which still needs a route. The rest of that page is unambiguous that private-subnet Fargate needs endpoints for ECR, CloudWatch Logs and Secrets Manager.

### Shape (a) — public subnet, public IP on the task

- Configure: `assignPublicIp: ENABLED`, task in a public subnet routed to an IGW. Express Mode does this automatically: *"assignPublicIp: Based on subnet type - Enabled for public subnets… This is disabled if you provide a private subnet."*
- **Cost:** one in-use public IPv4 per task = **$3.65/month/task**. Plus the internet-facing ALB's ≥2 public IPv4 = **$7.30/month**. No NAT, no endpoints.
- **Security discipline required:** the task ENI is directly on the internet. AWS's own warning: *"not only can you establish an outbound connection to any other host on the internet, but other hosts on the internet might also attempt to connect to your host. Therefore, you should pay close attention to your security group and firewall rules"* ([Connect ECS applications to the internet](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/networking-outbound.html)). Express Mode's generated **Load Balancer SG** allows inbound HTTPS and outbound to the **Service SG** on the container port only; the generated **Service SG** *"allows outbound traffic to the public internet."* Inbound to the task should be restricted to the ALB SG. Note also: *"When you provide a security group you are providing an **additional** ingress path to your service"* — supplying your own SG adds to, rather than replaces, the managed one.
- **This is the only shape where nothing recurring is paid for egress plumbing.** It is the cheapest, and it is the one that puts the app's ENI on the public internet.

### Shape (b) — private subnet + NAT gateway

- **Cost in `ap-southeast-2`: $0.059/hr = $43.07/month per NAT gateway**, plus $0.059/GB processed, plus normal data-transfer-out on top. AWS recommends *"a NAT gateway in each Availability Zone"* for redundancy — which doubles it — while also noting *"if you have a small workload, it might be more cost effective to use private subnets and NAT gateways"* (i.e. cheaper than many endpoints; see (c)).
- **Buys everything at once:** ECR, CloudWatch Logs, Secrets Manager, STS, and any future third-party call (Phase 1's FMP) with no further per-service charge.
- Task has no public IP; nothing on the internet can initiate a connection to it.

### Shape (c) — private subnet + interface VPC endpoints

Endpoints actually needed for a Fargate task pulling from a private ECR repo and logging to CloudWatch:

| Endpoint | Needed? | Why |
|---|---|---|
| `com.amazonaws.<region>.ecr.api` | **Yes** | *"Amazon ECS tasks hosted on Fargate using platform version 1.4.0 or later require both Amazon ECR VPC endpoints and the Amazon S3 gateway endpoints"* ([ECR VPC endpoints](https://docs.aws.amazon.com/AmazonECR/latest/userguide/vpc-endpoints.html)) |
| `com.amazonaws.<region>.ecr.dkr` | **Yes** | same |
| **S3 gateway endpoint** | **Yes — and free** | image layers live in S3 |
| `com.amazonaws.<region>.logs` | **Yes** | *"If your VPC doesn't have an internet gateway and your tasks use the `awslogs` log driver… you must create an interface VPC endpoint for CloudWatch Logs"* |
| `com.amazonaws.<region>.secretsmanager` | **Yes, if using task-definition `secrets`** | *"To allow your tasks to pull sensitive data from Secrets Manager, you must create the interface VPC endpoints for Secrets Manager"* |
| `ecs`, `ecs-agent`, `ecs-telemetry` | **No** | *"Tasks using Fargate don't require the interface VPC endpoints for Amazon ECS"* |

**Honest ENI count and price.** Interface endpoints are billed **per ENI, i.e. per subnet/AZ you enable**, at **$0.013/hr = $9.49/month** each in `ap-southeast-2`, plus $0.01/GB.

| Endpoint set | 1 AZ | 2 AZ | 3 AZ |
|---|---|---|---|
| 3 (ecr.api, ecr.dkr, logs) | $28.47 | **$56.94** | $85.41 |
| 4 (+ secretsmanager) | $37.96 | **$75.92** | $113.88 |

**So #18's suspicion is confirmed with numbers: at two AZs, endpoints cost more than a NAT gateway** ($56.94 or $75.92 vs $43.07). Endpoints only undercut NAT if you confine them to a **single AZ** ($28.47 / $37.96) — which is feasible even though the ALB itself must span ≥2 AZs, because ALB **cross-zone load balancing is on by default and cannot be turned off at the load balancer level**, so a two-AZ ALB can serve a one-AZ target set. That single-AZ saving is bought with a single-AZ failure domain.

Also note the endpoint SG requirement, which is a common first-attempt failure: *"The security group attached to the VPC endpoint must allow incoming connections on TCP port 443 from the private subnet of the VPC."*

### Does Express Mode let you choose?

**Yes — but the choice of subnet type silently decides the load balancer's scheme too.** From [Resources created by Express Mode](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-work.html):

> *"If none are provided, Express Mode will use the default public subnets in the default VPC… If you provide custom public subnets, Express Mode will provision an internet-facing ALB and turn on `assignPublicIP` for your tasks. If you provide private subnets (subnets without an internet gateway in their route table), Express Mode will provision an internal ALB."*

and

> *"assignPublicIp: … This is disabled if you provide a private subnet, **and you are then responsible for configuring a NAT gateway if your tasks need internet access.**"*

Further constraints:

- *"The default VPC must have at least two public subnets, in at least two availability zones, with at least 8 free IPs available, per assigned CIDR block, per subnet."*
- *"the first Express Mode service for a VPC defines the subnets associated with either the internet-facing or internal load balancer for that VPC. Subsequent Express Mode services that launch in the same VPC must have subnets that match the availability zones supported by the load balancer. Because of this, we recommend creating Express Mode services with subnets from all availability zones."* — the **first** service in a VPC pins the scheme. This is the closest ECS analogue to #8's App Runner Gotcha 5 (public subnets rolling back a working service), but it is a *pinning* rule, not a failure mode.
- IPv6: *"if you have IPv6-enabled subnets, the first Express Mode service in a VPC defines the internal or internet-facing load balancer for that VPC. Because of this, we recommend creating your IPv6-only subnets first, or in a new VPC."*
- `networkConfiguration` is `{securityGroups[], subnets[]}` only ([ExpressGatewayServiceNetworkConfiguration](https://docs.aws.amazon.com/AmazonECS/latest/APIReference/API_ExpressGatewayServiceNetworkConfiguration.html)) — there is no `assignPublicIp` knob at the Express Mode API level.

### Can RDS stay private in every shape? Yes.

In all three shapes the task's ENI is inside your VPC with a private IP, so the RDS security group can allow TCP 5432 **from the task's security group ID** and the DB instance can stay `Publicly accessible = No`, in private subnets, with no public IPv4 charge. **This is a genuine improvement over App Runner's default-egress path**, where #8 established that the absence of any published App Runner source CIDR forced `0.0.0.0/0` on 5432. Under ECS there is no shape that requires opening Postgres to the internet — including shape (a), where the task has a public IP but still originates from a known SG inside the VPC.

Security groups are stateful; network ACLs are not — if NACLs are customised, the ephemeral return range must be allowed. (Carried over from #8 as generally true, not App Runner-specific.)

---

## 4. Scale-to-zero

**App Runner could not do it** (`MinSize` range 1–25, per #8). **ECS can, at the Application Auto Scaling layer, and AWS documents it directly.**

- [RegisterScalableTarget](https://docs.aws.amazon.com/autoscaling/application/APIReference/API_RegisterScalableTarget.html): *"For the following resources, the minimum value allowed is 0… **ECS services**…"*
- [ECS service auto scaling](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/service-auto-scaling.html): *"If you want your task count to scale to zero when there's no work to be done, set a minimum capacity of 0. With target tracking scaling policies, when actual capacity is 0 and the metric indicates that there is workload demand, Service Auto Scaling waits for one data point to be sent before scaling out. In this case, it scales out by the minimum possible amount as a starting point and then resumes scaling based on the actual running task count."*

At zero tasks the Fargate charge is $0. **The ALB keeps billing $18.40/month regardless**, so scale-to-zero saves at most the $10.80 compute line — it does not make the shape cheap.

**Cold start** when a task does start: Fargate task provisioning + ENI attach + image pull, then .NET's own JIT, first EF Core query and connection-pool warm-up. On top of that, Express Mode's ALB target group requires **5 consecutive successful health checks at 30-second intervals** before a target is healthy (`healthy-threshold-count: 5`, `health-check-interval-seconds: 30`) — so on the documented defaults a freshly started task is **at least ~2.5 minutes** from receiving traffic even after the container is up. Requests arriving before then get no healthy target.

**Also documented and reliable:** [scheduled scaling actions](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/service-auto-scaling.html) — scale to 0 overnight, back to 1 in the morning, on a clock rather than on demand.

**What I could not establish — flagged in §9.** Express Mode's default scaling metric is `ECSServiceAverageCPUUtilization`. With **zero** running tasks there are no tasks emitting CPU utilisation, and with zero registered targets there is no `RequestCountPerTarget`. AWS's "metric indicates that there is workload demand" sentence does not say which metrics can indicate demand at zero capacity, and I found no first-party statement that ECS target tracking scales out from zero on either of Express Mode's offered metrics. `ExpressGatewayScalingTarget` also documents **no valid range** for `minTaskCount`, so whether Express Mode even accepts `0` is unknown from the docs.

---

## 5. The CloudFront `/api/*` origin

#8 confirmed CloudFront path-routing was compatible with App Runner because App Runner ingress is a managed public HTTPS endpoint unaffected by VPC egress config. **With an ALB, three things change.**

### 5a. Certificates — two of them, in two Regions

- **CloudFront's viewer certificate must be in `us-east-1`.** *"To use an ACM certificate with CloudFront, you must request or import the certificate in the US East (N. Virginia) region"* ([ACM integrated services](https://docs.aws.amazon.com/acm/latest/userguide/acm-services.html)). This restates #9's constraint; unchanged by the move to ECS.
- **ACM certificates are regional.** *"Certificates in ACM are regional resources"* and *"To use a certificate with Elastic Load Balancing for the same fully qualified domain name (FQDN)… in more than one AWS region, you must request or import a certificate for each region"* ([ACM overview](https://docs.aws.amazon.com/acm/latest/userguide/acm-overview.html)). So **an HTTPS listener on an ALB in `ap-southeast-2` needs a certificate issued in `ap-southeast-2`** — a second, separate certificate from the CloudFront one.
- **Express Mode issues that regional certificate for you.** The resources page lists *"ACM certificate"* among what it creates, and the listener defaults are `protocol: https`, `port: 443`, `rule-type: host-header`. The service is reachable at `https://<service-name>.ecs.<region>.on.aws/` with *"Automatic SSL/TLS termination"*.
- **You may not need a second certificate at all.** CloudFront's origin protocol policy accepts `http-only` (valid values `http-only | match-viewer | https-only`, [VpcOriginEndpointConfig](https://docs.aws.amazon.com/cloudfront/latest/APIReference/API_VpcOriginEndpointConfig.html); the same three values apply to custom origins). CloudFront→origin over plain HTTP inside your VPC removes the regional-certificate requirement entirely. Whether that is acceptable is a decision, not a fact.

### 5b. Can the ALB be locked to CloudFront only? Yes — two documented mechanisms

[Restrict access to Application Load Balancers](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/restrict-access-to-load-balancer.html) gives, for an **internet-facing** ALB:

1. **A secret custom origin header** that CloudFront adds and an ALB listener rule requires. AWS is blunt about the weakness: *"This use case relies on keeping the custom header name and value secret… Treat header names and values as secure credentials, like usernames and passwords."*
2. **The AWS-managed CloudFront origin-facing prefix list** on the ALB security group (narrows the source to CloudFront's edge, but not to *your* distribution).

And for an **internal** ALB, the stronger mechanism:

3. **[CloudFront VPC origins](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/private-content-vpc-origins.html)** — *"placing your load balancers and EC2 instances in private subnets, making CloudFront the single point of entry."* CloudFront creates a service-managed ENI in your private subnet and a service-managed security group `CloudFront-VPCOrigins-Service-SG`; you allow inbound from that SG, which *"is further restrictive as it restricts the traffic only to your CloudFront distributions."* **Supported in `ap-southeast-2`** (named in the supported-Regions table) and **at no additional cost**.

VPC origins prerequisites and limits worth knowing:
- *"You need to add an internet gateway to the VPC that has your VPC origin resources. The internet gateway is required to denote that the VPC can receive traffic from the internet. The internet gateway is not used for routing traffic to origins inside the subnet, and you don't need to update the routing policies."* — an IGW must **exist on the VPC**, which does not make the origin subnet public.
- One free private IPv4 needed per subnet for the CloudFront ENI.
- Creating/updating a VPC origin *"can take up to 15 minutes"*.
- **No gRPC**, **no Lambda@Edge origin-request/origin-response triggers**, and *"Inbound NACL rules are not evaluated for traffic from CloudFront to your VPC origin. However, outbound NACL rules are evaluated on the return path and must allow traffic on ephemeral TCP ports (1024–65535)."*
- An NLB with a TLS listener cannot be a VPC origin, and an NLB used as one must have a security group.

### 5c. The wrinkle Express Mode introduces

Express Mode's ALB listener routes by **host header** (`listener-configurations.rule-type: host-header`), matched to the AWS-provided `<service>.ecs.<region>.on.aws` name, and *"load balancer configurations can not be updated on Express Mode services"* (though the ALB itself can be edited directly). Whether CloudFront's origin request carries a Host header that satisfies that rule — and how to make it do so behind a custom domain — I could not establish from first-party docs. See §9.

Separately: in the **public** Express Mode shape the `.on.aws` URL is a fully working, publicly reachable second front door to the API. Compass's registration and login endpoints would be reachable there as well as through CloudFront. Cookies are unaffected (`compass_auth` is scoped to `financial-compass.dev`, and `SameSite=Lax` is a browser-side rule), but ADR 0007's threat model assumed one entrance.

---

## 6. Deployment

**There is no ECS equivalent of App Runner's auto-deploy-on-ECR-push.** A push to ECR does nothing by itself; something must call an ECS API.

The mechanisms:

- **Standard ECS service:** register a new task definition revision and `UpdateService`, or `UpdateService --force-new-deployment` to redeploy the same definition ([Updating a service](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/update-service-console-v2.html)).
- **Express Mode:** `aws ecs update-express-gateway-service --primary-container '{"image": "..."}'`, or `--task-definition-arn` if you manage the task definition yourself. Express Mode then *"creates a new managed task definition with your changes while preserving your other task-level configuration."* Deployment is the fixed canary described in §1.

### GitHub Actions, per AWS's own walkthrough

AWS published [Automated deployments with GitHub Actions for Amazon ECS Express Mode](https://aws.amazon.com/blogs/containers/automated-deployments-with-github-actions-for-amazon-ecs-express-mode/) (AWS Containers Blog), with a companion AWS-owned action **`aws-actions/amazon-ecs-deploy-express-service@v1`**. The workflow is: build → push to ECR (tagged with the 7-character commit SHA) → deploy step, which *"will check if the specified cluster exists (or creates it if `default` is specified and doesn't exist), creates the service (or updates it, if the service already exists), and waits for the service and deployment to reach a stable state."*

**OIDC, not long-lived keys.** *"Because OIDC generates credentials that automatically expire after each workflow run, you don't need to store long-lived AWS credentials in your repository."* The setup is:

- `aws iam create-open-id-connect-provider --url https://token.actions.githubusercontent.com --client-id-list sts.amazonaws.com`
- A role trust policy on `sts:AssumeRoleWithWebIdentity` with `StringEquals` on `token.actions.githubusercontent.com:aud = sts.amazonaws.com` and `StringLike` on `token.actions.githubusercontent.com:sub = repo:<owner>/<repo>:*`.
- Workflow `permissions: { id-token: write, contents: read }` and `aws-actions/configure-aws-credentials@v5` with `role-to-assume`.

**IAM the deploy role needs**, as published:

- ECS: `CreateCluster`, `RegisterTaskDefinition`, `CreateExpressGatewayService`, `UpdateExpressGatewayService`, `DescribeExpressGatewayService`, `DescribeClusters`, `DescribeServices`, `ListServiceDeployments`, `UpdateService`, `DescribeServiceDeployments`.
- `iam:PassRole` for `ecsTaskExecutionRole` and `ecsInfrastructureRoleForExpressServices`, conditioned on `iam:PassedToService = ecs.amazonaws.com`.
- ECR: `GetAuthorizationToken` (on `*`) plus `BatchGetImage`, `BatchCheckLayerAvailability`, `CompleteLayerUpload`, `GetDownloadUrlForLayer`, `InitiateLayerUpload`, `PutImage`, `UploadLayerPart` on the repository.

AWS's own caveat: *"We recommend reviewing and tightening these policies… you might restrict the Resource field to specific Amazon ECR repositories or Amazon ECS clusters rather than using wildcards."*

### The two roles Express Mode itself needs

- **Task execution role** — `AmazonECSTaskExecutionRolePolicy`; *"required for Amazon ECS to pull container images from Amazon ECR, send container logs to Amazon CloudWatch Logs, and retrieve sensitive data from AWS Systems Manager Parameter Store or AWS Secrets Manager."* Trust: `ecs-tasks.amazonaws.com`.
- **Infrastructure role** — `AmazonECSInfrastructureRoleforExpressGatewayServices`; used *"only during Express service creation, updates, and deletion operations"* to manage the ALB, target groups, SGs and scaling policies. Trust: `ecs.amazonaws.com`.
- **Task role** — optional, for the app's own AWS SDK calls. Phase 0 makes none.
- Gotcha: *"IAM roles are eventually consistent… If your first `create-express-gateway-service` call fails with `Unable to assume the service linked role`… wait a short time (about a minute)… and then retry."*

---

## 7. Secrets

**This is where the App Runner exemption is most expensive to lose.**

The mechanism is the task definition's `secrets` list (`{name, valueFrom}`), with `valueFrom` an ARN of a Secrets Manager secret (optionally `:json-key:version-stage:version-id`) or an SSM parameter. Express Mode exposes it directly as `primaryContainer.secrets` in [CreateExpressGatewayService](https://docs.aws.amazon.com/AmazonECS/latest/APIReference/API_CreateExpressGatewayService.html), and the console offers it as *"Value type → Secret"*.

**Injection is done by the ECS/Fargate agent using the task execution role, and on Fargate it needs a network path.** The [ECS VPC endpoints page](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/vpc-endpoints.html), under "Considerations for Amazon ECS VPC endpoints for Fargate", states: *"To allow your tasks to pull sensitive data from Secrets Manager, you must create the interface VPC endpoints for Secrets Manager."* The ECS secrets page repeats it: *"Use interface VPC endpoints to enhance security controls and connect to Secrets Manager through a private subnet. You must create the interface VPC endpoints for Secrets Manager."*

**So, directly answering #18's question:** yes, injection is the agent's job, and **unlike App Runner it is not exempt from your VPC**. In shape (b) it rides the NAT; in shape (c) it costs a **fourth interface endpoint at $9.49/month per AZ**; in shape (a) it rides the IGW.

Other facts that price the map's "Production secrets & per-environment config" fog:

- **Rotation still needs a redeploy** — same as App Runner: *"Sensitive data is injected into your container when the container is initially started. If the secret is subsequently updated or rotated, the container will not receive the updated value automatically. You must either launch a new task or… update the service and use the Force new deployment option."*
- **Platform version floors:** full-secret injection needs Fargate PV **1.3.0+**; injecting a **specific JSON key or version** needs **1.4.0+** (Linux). `LATEST` is 1.4.0.
- **Environment variables are visible** to the app, its logs and debugging tools: *"Applications that run on the container and container logs and debugging tools have access to the environment variables."* AWS's stated alternatives are an encrypted S3 object read by the app, or a sidecar that writes the secret to a task-scoped volume (*"On AWS Fargate, volume storage is automatically encrypted using a service managed key"*).
- SSM Parameter Store is an equally valid `valueFrom` target and, unlike Secrets Manager, has a free standard tier — but it needs its own `ssm` interface endpoint in shape (c). (The per-service cost of Secrets Manager vs Parameter Store was not priced in this note.)
- The execution role needs explicit `secretsmanager:GetSecretValue` / `ssm:GetParameters` (and `kms:Decrypt` for a customer-managed key) beyond `AmazonECSTaskExecutionRolePolicy`.

**One oddity to flag.** The [best-practices page](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-best-practices.html) shows secrets being passed through `--primary-container '{"environment":[{"name":"DB_PASSWORD","value":"arn:aws:secretsmanager:..."}]}'` — i.e. an **ARN in the `environment` list**, not in the `secrets` list. The API model has a separate `secrets` array with `valueFrom`. The two are inconsistent; the `secrets`/`valueFrom` form is the one the API reference and the wider ECS documentation define. Treat the best-practices snippet as suspect.

---

## 8. Regional availability

- **Express Mode:** *"Amazon ECS Express Mode is available in all AWS Regions where Amazon ECS and Fargate are supported"* ([overview](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-overview.html), read 2026-09-02). Launched **2025-11-21**, announced as *"available now in all AWS Regions at no additional charge"* ([What's New](https://aws.amazon.com/about-aws/whats-new/2025/11/announcing-amazon-ecs-express-mode/)). It reached **GovCloud (US-East/US-West) on 2026-06-15** ([What's New](https://aws.amazon.com/about-aws/whats-new/2026/06/amazon-ecs-express-mode-govcloud/)) — GovCloud is a separate partition, so that is a partition expansion rather than evidence of an incomplete commercial rollout.
- **Fargate in `ap-southeast-2`:** confirmed by the existence of published `ap-southeast-2` Fargate rates in the Price List (§2).
- **CloudFront VPC origins:** `ap-southeast-2` is listed explicitly in the supported-Regions table, with no AZ exception noted (unlike `us-east-1`, `us-west-1`, `ap-northeast-1`, `ca-central-1`).
- **Contrast with App Runner**, which #8 found in **11 regions only** — including `ap-southeast-2`, so this is not a reason the choice had to change.
- **Not verified by API call.** No AWS credentials were available in this session, so — unlike [#17](https://github.com/wkimdkim-nz/compass/issues/17), which proved App Runner by execution — nothing here was confirmed against the live account. A one-line probe (`aws ecs create-express-gateway-service … --region ap-southeast-2`) would settle it.

---

## 9. Uncertain / not first-party

Things I could not establish from AWS documentation. Treat as open questions, not facts.

1. **Whether Express Mode accepts `minTaskCount: 0`.** [ExpressGatewayScalingTarget](https://docs.aws.amazon.com/AmazonECS/latest/APIReference/API_ExpressGatewayScalingTarget.html) documents no valid range for `minTaskCount` or `maxTaskCount`; the Developer Guide only gives the default of 1. Application Auto Scaling allows 0 for ECS services generally, but Express Mode may impose its own floor.
2. **Whether ECS target tracking can actually scale *out from zero* on Express Mode's metrics.** AWS says target tracking scales out from 0 *"when… the metric indicates that there is workload demand"*, but does not say which metrics can do so. With zero tasks there is no `ECSServiceAverageCPUUtilization` and no `RequestCountPerTarget` (no registered targets). **This is the difference between "scale-to-zero saves $10.80/month" and "scale-to-zero is a manual/scheduled operation only."** Test it, or use scheduled scaling, which is unambiguous.
3. **Whether a CloudFront origin request satisfies Express Mode's host-header listener rule.** Express Mode routes on `host-header` matched to the `.on.aws` name; CloudFront's Host-forwarding behaviour to a custom origin versus a VPC origin, and whether an added listener rule survives Express Mode's management, are both unestablished. This is the main open item for §5.
4. **Whether re-creating an Express Mode service yields a stable URL.** If `<service-name>.ecs.<region>.on.aws` gains a random suffix on each create, the teardown-between-sessions plan in §2 breaks the CloudFront origin every cycle.
5. **The default CPU/memory, and the default health-check path.** AWS's own docs disagree (§1). Pass both explicitly rather than resolving the discrepancy by reading.
6. **Whether ALB LCU charges have any minimum.** AWS publishes the four dimensions and the "highest dimension wins" rule but no floor, so the LCU line for an idle service is unquantified — small, but not provably zero.
7. **Whether the ELB free tier applies to this account.** AWS's ELB pricing page offers *"750 hours per month shared between Classic and Application load balancers"* and *"15 LCUs"* to *"New AWS customers"*. #8 established that AWS has moved newer accounts to a credit-based Free plan, and #9/#1 record that this account was **upgraded to the paid plan**. Whether the 750 ALB hours apply here — which would erase the $18.40 line for up to 12 months — is **the single highest-value unknown in this note** and should be checked in Billing → Free Tier before §2's arithmetic is trusted.
8. **Whether AWS Free Tier includes any public IPv4 hours.** Carried over unresolved from #8; the VPC pricing page's free-tier text did not come through in either fetch.
9. **Secrets Manager vs SSM Parameter Store per-secret cost** in `ap-southeast-2` — not priced here.
10. **CloudWatch Logs storage rate** in `ap-southeast-2` — only the ingestion rate ($0.67/GB Standard) was extracted.
11. **Whether an Express Mode service can be created without a load balancer at all.** Nothing in the docs suggests it can; the ALB appears mandatory. A hand-rolled ECS service certainly can run without one, but then CloudFront has no stable origin (task IPs change on every deploy), and CloudFront VPC origins accept only ALB/NLB/EC2 — not ECS tasks.
12. **Nothing in this note was verified against the live AWS account** (§8).

---

## Sources

Amazon ECS — Developer Guide (all fetched 2026-09-02):
- [Amazon ECS Express Mode](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-overview.html)
- [Resources created by Amazon ECS Express Mode services](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-work.html) — the load-bearing page for §1 and §3
- [Creating an Amazon ECS Express Mode service](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-create-full.html)
- [Create your first Express Mode service using the AWS CLI](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-getting-started.html)
- [Updating an Amazon ECS Express Mode service](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-update-full.html)
- [Delete Amazon ECS Express Mode services](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-delete-task.html)
- [Best practices for Amazon ECS Express Mode services](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/express-service-best-practices.html)
- [Amazon ECS tasks on AWS Fargate (task networking)](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/fargate-tasks-services.html)
- [Fargate platform versions](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/platform-linux-fargate.html)
- [Connect Amazon ECS applications to the internet](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/networking-outbound.html)
- [Amazon ECS interface VPC endpoints (AWS PrivateLink)](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/vpc-endpoints.html)
- [Pass sensitive data to an Amazon ECS container](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/specifying-sensitive-data.html)
- [Pass Secrets Manager secrets through Amazon ECS environment variables](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/secrets-envvar-secrets-manager.html)
- [Automatically scale your Amazon ECS service](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/service-auto-scaling.html)
- [Updating an Amazon ECS service](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/update-service-console-v2.html)

Amazon ECS — API Reference:
- [Actions](https://docs.aws.amazon.com/AmazonECS/latest/APIReference/API_Operations.html)
- [CreateExpressGatewayService](https://docs.aws.amazon.com/AmazonECS/latest/APIReference/API_CreateExpressGatewayService.html)
- [ExpressGatewayScalingTarget](https://docs.aws.amazon.com/AmazonECS/latest/APIReference/API_ExpressGatewayScalingTarget.html)
- [ExpressGatewayServiceNetworkConfiguration](https://docs.aws.amazon.com/AmazonECS/latest/APIReference/API_ExpressGatewayServiceNetworkConfiguration.html)

Other AWS documentation:
- [Amazon ECR interface VPC endpoints (AWS PrivateLink)](https://docs.aws.amazon.com/AmazonECR/latest/userguide/vpc-endpoints.html)
- [Gateway endpoints — Amazon VPC / AWS PrivateLink](https://docs.aws.amazon.com/vpc/latest/privatelink/gateway-endpoints.html)
- [Application Load Balancers — Elastic Load Balancing](https://docs.aws.amazon.com/elasticloadbalancing/latest/application/application-load-balancers.html)
- [SSL certificates for your Application Load Balancer](https://docs.aws.amazon.com/elasticloadbalancing/latest/application/https-listener-certificates.html)
- [Restrict access with VPC origins — Amazon CloudFront](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/private-content-vpc-origins.html)
- [Restrict access to Application Load Balancers — Amazon CloudFront](https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/restrict-access-to-load-balancer.html)
- [VpcOriginEndpointConfig — CloudFront API Reference](https://docs.aws.amazon.com/cloudfront/latest/APIReference/API_VpcOriginEndpointConfig.html)
- [Managed automation with integrated services — AWS Certificate Manager](https://docs.aws.amazon.com/acm/latest/userguide/acm-services.html)
- [What is AWS Certificate Manager?](https://docs.aws.amazon.com/acm/latest/userguide/acm-overview.html)
- [RegisterScalableTarget — Application Auto Scaling API Reference](https://docs.aws.amazon.com/autoscaling/application/APIReference/API_RegisterScalableTarget.html)

AWS blogs and announcements (official):
- [Automated deployments with GitHub Actions for Amazon ECS Express Mode](https://aws.amazon.com/blogs/containers/automated-deployments-with-github-actions-for-amazon-ecs-express-mode/) — AWS Containers Blog
- [Announcing Amazon ECS Express Mode](https://aws.amazon.com/about-aws/whats-new/2025/11/announcing-amazon-ecs-express-mode/) — 2025-11-21
- [Amazon ECS Express Mode is now available in AWS GovCloud (US) Regions](https://aws.amazon.com/about-aws/whats-new/2026/06/amazon-ecs-express-mode-govcloud/) — 2026-06-15
- [Amazon CloudFront announces VPC origins](https://aws.amazon.com/about-aws/whats-new/2024/11/amazon-cloudfront-vpc-origins/) — 2024-11-20

Pricing (read 2026-09-02):
- **AWS Price List Bulk API**, `ap-southeast-2` region files for `AmazonECS`, `AWSELB`, `AmazonVPC`, `AmazonEC2`, `AmazonECR`, `AmazonRDS`, `AmazonCloudWatch` — `https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/<service>/current/ap-southeast-2/index.json`
- [Elastic Load Balancing pricing](https://aws.amazon.com/elasticloadbalancing/pricing/) — LCU definitions, free-tier text
- [Amazon VPC pricing](https://aws.amazon.com/vpc/pricing/) — public IPv4, NAT gateway
- [Amazon ECS Managed Instances pricing](https://aws.amazon.com/ecs/managed-instances/pricing/)

Prior Compass research this note deliberately does **not** inherit:
- [`docs/research/app-runner-rds-connectivity.md`](./app-runner-rds-connectivity.md) — answers #8, App Runner-specific.
