# The VPC the ephemeral app stack starts in. Free at rest — a VPC, its subnets, its route tables
# and an internet gateway cost nothing while nothing is running — which is why the network persists
# here rather than being torn down with the ALB, the service and the database it will hold.
#
# The shape is settled (#10, #18, ADR 0009) and the reasoning is worth keeping close, because the
# obvious "more secure" rearrangement does not work:
#
#   * **The task runs in a public subnet with a public IP.** ECS has no control-plane exemption.
#     App Runner could pull an image on your behalf; a Fargate task cannot. It pulls from ECR,
#     fetches its secret and ships its logs over the *same* network path as application traffic, so
#     a private, NAT-less, endpoint-less task does not start slowly or degrade — it cannot start at
#     all, and the failure surfaces as a task that stops before the container ever runs.
#   * A NAT gateway would fix that for about $43/month in this region, which is more than the rest
#     of the running stack put together. Interface endpoints are *worse* here, not better: across
#     two AZs they cost about $57/month (#8's contrary figure is wrong for ap-southeast-2).
#   * The public subnet is also cheaper *later*: Phase 1's outbound market-data call leaves through
#     the internet gateway at no charge, where every private shape meters it.
#
# What keeps that safe is not the subnet, it is the security-group chain in security-groups.tf:
# the task admits the load balancer and nothing else, and the database admits the task and nothing
# else. The database subnets have no route off the VPC at all.

# Asked rather than hard-coded, but sliced deterministically: `names` comes back sorted, so the
# first two are stable across runs. Local Zones are excluded — they are opt-in, and RDS and Fargate
# are not offered in them.
data "aws_availability_zones" "available" {
  state = "available"

  filter {
    name   = "opt-in-status"
    values = ["opt-in-not-required"]
  }
}

locals {
  # Two, not three. The database is single-AZ and the load balancer needs exactly two, so a third
  # AZ would add subnets nothing is ever placed in.
  azs = slice(data.aws_availability_zones.available.names, 0, 2)

  # /24s out of a /16, keyed by AZ so the map is readable in a plan. Public and private are
  # separated by a wide gap in the third octet so a later pair — a second private tier, say — can
  # be added without renumbering anything that already exists.
  public_subnets  = { for i, az in local.azs : az => cidrsubnet(var.vpc_cidr, 8, i) }
  private_subnets = { for i, az in local.azs : az => cidrsubnet(var.vpc_cidr, 8, i + 10) }
}

resource "aws_vpc" "compass" {
  cidr_block = var.vpc_cidr

  # Both are required for RDS: the instance is reached by its endpoint name, and without DNS
  # hostnames that name has nothing to resolve to from inside the VPC.
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = { Name = "compass" }
}

# IPv4 only, deliberately. IPv6 would need an egress-only gateway for the private tier and dual
# stack decisions in every security group, to reach an origin CloudFront already speaks IPv4 to.
resource "aws_internet_gateway" "compass" {
  vpc_id = aws_vpc.compass.id

  tags = { Name = "compass" }
}

# The load balancer lives here, and so does the task. What makes these subnets public is the route
# to the internet gateway below — not `map_public_ip_on_launch`, which is left off because it does
# nothing for Fargate: the ECS service requests the task's public address itself with
# `assign_public_ip = ENABLED`, and that setting is the one that decides whether the task starts.
resource "aws_subnet" "public" {
  for_each = local.public_subnets

  vpc_id            = aws_vpc.compass.id
  availability_zone = each.key
  cidr_block        = each.value

  tags = { Name = "compass-public-${each.key}" }
}

# The database's subnets. No route table entry beyond the VPC-local one, so there is no path off
# the VPC in either direction — the database's unreachability from the internet is a property of
# the routing table, not only of a security group someone could later widen.
resource "aws_subnet" "private" {
  for_each = local.private_subnets

  vpc_id            = aws_vpc.compass.id
  availability_zone = each.key
  cidr_block        = each.value

  tags = { Name = "compass-private-${each.key}" }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.compass.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.compass.id
  }

  tags = { Name = "compass-public" }
}

# Declared with no routes rather than left to the VPC's main route table. The main table is
# implicit, shared, and the thing a later change edits by accident; an empty table named for its
# tier makes "the database has no way out" something you can read.
resource "aws_route_table" "private" {
  vpc_id = aws_vpc.compass.id

  tags = { Name = "compass-private" }
}

resource "aws_route_table_association" "public" {
  for_each = aws_subnet.public

  subnet_id      = each.value.id
  route_table_id = aws_route_table.public.id
}

resource "aws_route_table_association" "private" {
  for_each = aws_subnet.private

  subnet_id      = each.value.id
  route_table_id = aws_route_table.private.id
}

# Two subnets in two availability zones, even though the instance is single-AZ. RDS requires it
# regardless: a subnet group with one AZ is rejected at creation, and the error arrives in the
# middle of the app stack's apply rather than here.
resource "aws_db_subnet_group" "compass" {
  name        = "compass"
  description = "The private subnets the Postgres instance is placed in"
  subnet_ids  = [for subnet in aws_subnet.private : subnet.id]
}
