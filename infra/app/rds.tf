# The database, and the two properties that are structural here rather than merely careful.
#
# **The master password never enters Terraform state.** `manage_master_user_password` hands the
# password to RDS, which generates it into a Secrets Manager secret it owns, rotates it on its own
# schedule, and never tells Terraform the value — the only thing that comes back is the secret's
# ARN. The alternative shape, a password generated here and written into a Terraform-managed
# secret, was rejected as self-defeating: it puts the password back into state, which is the thing
# this is for.
#
# **Nothing reaches it but the task.** The instance sits in the foundation stack's private subnets,
# which have no route off the VPC in either direction, behind the security group that admits the
# task's group on 5432 and nothing else. `publicly_accessible = false` is stated below even though
# it is the default, because it is the difference between private and not.
#
# ⚠️ The 7-day tripwire. RDS rotates that managed secret every seven days by default,
# ECS injects its two keys once at task start, and AWS has no channel to push a new value into a
# running container — only a restart. This stack is safe from that *solely* because it is
# ephemeral: a work session is hours, not days. If this app stack ever stops being
# destroyed between sessions, move to an application-side password provider.

resource "aws_db_instance" "compass" {
  identifier = "compass"

  # Postgres 18 for parity with the local `postgres:18-alpine` (docker-compose.yml) and with the
  # Testcontainers image the API's own suite runs against, so the deployed database is not a
  # different major version from the one every test has ever seen.
  #
  # Verified: 18.1 through 18.6 are offered in ap-southeast-2, and `db.t4g.micro` on `gp3` is orderable against 18. Worth
  # checking rather than assuming: an engine version a region does not offer is refused in the
  # middle of the apply, after the load balancer has been provisioned.
  #
  #     aws rds describe-db-engine-versions --engine postgres --region ap-southeast-2 \
  #       --query "DBEngineVersions[?starts_with(EngineVersion, '18')].EngineVersion"
  #
  # The major version only. RDS resolves it to the current minor at creation and the provider
  # prefix-matches, so the minor upgrade below does not then show as permanent drift in every plan.
  engine         = "postgres"
  engine_version = "18"

  # Stated, because the line above leans on it: pinning the major version alone is only safe while
  # RDS is the thing allowed to move the minor.
  auto_minor_version_upgrade = true

  # 2 vCPU burstable, 1 GiB — the smallest current-generation instance, and the
  # class the pool ceiling below is sized against.
  instance_class = "db.t4g.micro"

  # 20 GiB is gp3's floor for RDS, and far more than a walking skeleton writes. Storage autoscaling
  # is deliberately left off: on a database that should never grow, it can only turn a runaway
  # write into a bill.
  allocated_storage = 20
  storage_type      = "gp3"

  # Free, and it needs nothing else: the default `aws/rds` key is AWS-managed, so no key policy and
  # no `kms:Decrypt` anywhere. (⚠️ A customer-managed key would need both, here and in iam.tf.)
  storage_encrypted = true

  db_name  = "compass"
  username = "compass"
  port     = local.foundation.database_port

  # See the header. The value is RDS's; `master_user_secret[0].secret_arn` is all this stack learns
  # of it, and iam.tf scopes the execution role to exactly that ARN.
  manage_master_user_password = true

  db_subnet_group_name   = local.foundation.db_subnet_group_name
  vpc_security_group_ids = [local.foundation.database_security_group_id]

  # Stated rather than left to the default, because it is the whole of "the database is unreachable
  # from the internet". Private subnets alone would not settle it: `publicly_accessible` is
  # what decides whether the endpoint resolves to a public address at all.
  publicly_accessible = false

  # Single-AZ, stated. The subnet group still spans two availability zones because RDS rejects a
  # one-AZ group at creation — see the foundation stack's network.tf.
  multi_az = false

  # --- Teardown has to actually finish -----------------------------------------------------------
  #
  # ⚠️ All three together, and each one alone is enough to break the cost lever this stack exists
  # for. Deletion protection makes `destroy` fail outright. A final snapshot makes it hang for
  # minutes and then bill for the snapshot it left behind, every session, forever. Retained
  # automated backups do the same more quietly.
  deletion_protection     = false
  skip_final_snapshot     = true
  backup_retention_period = 0

  # No point-in-time recovery follows from that retention, and that is correct rather than
  # regrettable: teardown is the cleanup mechanism, so there is no state here worth a backup
  # window's I/O.

  # Nothing here waits for a maintenance window. An ephemeral stack may not live until the next one.
  apply_immediately = true

  # Off. Free only for seven days of retention and this instance rarely lives seven hours; one
  # task's logs are the observability this phase needs (ADR 0009).
  performance_insights_enabled = false

  tags = { Name = "compass" }
}

locals {
  # The RDS-managed secret, as a JSON document with exactly two keys — `username` and `password`.
  # The execution role's inline policy (iam.tf) is scoped to this ARN and nothing else.
  database_secret_arn = aws_db_instance.compass.master_user_secret[0].secret_arn

  # How a task definition asks for one key out of that document.
  #
  # ⚠️ `<arn>:<json-key>:<version-stage>:<version-id>`, with the last two left empty for AWSCURRENT
  # and the current version. Written once because every task definition that reaches the database
  # needs both, and because dropping the selector is a silent failure of a particular kind: the
  # container receives the entire JSON document as the value, so it connects as the literal user
  # `{"username":"compass","password":...}` and the password is in the first log line that prints
  # a connection string.
  database_username_secret = "${local.database_secret_arn}:username::"
  database_password_secret = "${local.database_secret_arn}:password::"

  # Everything the API needs to reach the database *except* the credentials, which arrive
  # separately as injected secrets and are joined on at startup (DatabaseConnectionString.Resolve).
  #
  # Two of these settings are load-bearing and neither is obvious from this end:
  #
  #   * `Trust Server Certificate=true` — Npgsql 8 changed `SSL Mode=Require` to validate the
  #     server's certificate, and RDS's certificate authority is not in the runtime image's trust
  #     store. Without it the connection does not fall back to something weaker, it fails outright.
  #     `Verify-Full` with a baked-in RDS CA bundle was rejected as a second thing expiring on
  #     AWS's schedule.
  #   * `Maximum Pool Size=20` — Npgsql defaults to 100, and a deployment runs the old task and
  #     the new one at once, so the default would reach for 200 connections exactly while
  #     deploying. Two tasks × 20 is 40. ⚠️ The measured ceiling is **79**, not the ~100 the
  #     arithmetic suggests — 1 GiB ÷ 9531392 ≈ 112 counts memory RDS keeps for itself. Observed
  #     at first boot and recorded in infra/README.md: it fits, with 36 spare, and anything above
  #     38 would make a deployment the thing that exhausts the database.
  #
  # `address` and `port` rather than `endpoint`, which is the two of them already joined by a colon
  # and would need splitting apart again to be written as a Npgsql keyword pair.
  database_connection_string = join(";", [
    "Host=${aws_db_instance.compass.address}",
    "Port=${aws_db_instance.compass.port}",
    "Database=${aws_db_instance.compass.db_name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
    "Maximum Pool Size=20",
  ])
}
