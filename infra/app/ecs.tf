# The three task definitions, the service that runs one of them, and the migration this stack
# refuses to serve traffic without.
#
# The service is the only long-running one. The other two are one-off tasks started with
# `aws ecs run-task` — `migrate` by the runner at the bottom of this file, on every apply that
# registers a new revision of its task definition (a new image tag, a new database endpoint, a
# change to the shared environment; an unchanged re-apply runs nothing, a fresh stack always runs
# it once), and `psql` by hand when someone needs to look inside a database nothing else can reach.

resource "aws_ecs_cluster" "compass" {
  name = "compass"

  # Container Insights is left off. It is a per-metric CloudWatch charge on a stack whose whole
  # point is that it costs nothing while it is torn down, and one task's logs are the observability
  # this phase needs.
}

# One group for all three task definitions below, which is why it is named for the stack and not
# for the service. They are told apart by their log stream prefixes — `api/`, `migrate/`, `psql/` —
# and share one retention policy rather than three.
#
# Destroyed with the stack, like everything else here, so a work session's logs do not outlive the
# stack they describe. Retention still matters: without it the group is created with logs
# kept forever, and a group recreated on every rebuild would accumulate them indefinitely.
resource "aws_cloudwatch_log_group" "compass" {
  name              = "/ecs/compass"
  retention_in_days = 7
}

locals {
  # Stated here rather than offered as variables, and the reason is money. AWS documents the
  # Fargate default two ways — 256/512 in the API reference, 1024/2048 in the developer guide — a
  # fourfold swing between roughly $10.80 and $43.22 a month, so ADR 0009 fixed the size rather
  # than letting whichever page you read decide the bill. Fixing it and then exposing it as an
  # overridable variable would put the same decision back on the command line.
  #
  # 256 CPU units is a quarter vCPU. It permits 512, 1024 or 2048 MiB of memory; 512 is the floor
  # and is what ADR 0009 chose.
  task_cpu    = 256
  task_memory = 512

  # --- Declared once, referenced by every task definition below ---------------------------------
  #
  # ⚠️ This is the reason the service task and the migration task cannot connect to different
  # databases (#28). Not discipline, and not a review checklist: there is one list, and a second
  # copy would have to be written deliberately. Anything a task needs to reach Postgres belongs
  # here rather than in a `container_definitions` block.

  task_environment = [
    # Set explicitly even though Production is already the value an unset ASPNETCORE_ENVIRONMENT
    # takes. The session cookie's SecurePolicy is Always outside Development, and that must not
    # rest on an implicit default (#19).
    { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },

    # Also explicit, and also already the base image's default via ASPNETCORE_HTTP_PORTS. Written
    # down rather than left to a base image's convention, for the reason main.tf gives. The
    # migration task inherits it and ignores it — efbundle listens on nothing — which is the price of
    # one shared list and a cheap one.
    { name = "ASPNETCORE_URLS", value = "http://+:${local.container_port}" },

    # Host, port, database and the two settings that are load-bearing against a real RDS instance.
    # rds.tf builds it and says why each part is there.
    { name = "ConnectionStrings__Default", value = local.database_connection_string },

    # The contact address in the User-Agent SEC EDGAR insists on; the API binds it as
    # `Edgar:ContactEmail` (ADR 0010). Not a secret, and not in source either — it arrives as a
    # variable with no default. Outside Development the API refuses to start without it, and that
    # includes the migration task, which boots the same host to find its DbContext: one more reason
    # it belongs in the shared list.
    { name = "Edgar__ContactEmail", value = var.edgar_contact_email },
  ]

  # Exactly three, and they are the whole of what the ECS agent fetches on this stack's behalf. The
  # application never sees Secrets Manager: these arrive as ordinary environment variables. The
  # first two DatabaseConnectionString.Resolve overlays onto the connection string above, through
  # the same code path the local tests walk; rds.tf builds the two selectors and says what happens
  # to a task definition that omits one. The third is the foundation stack's hand-set Finnhub key,
  # a plain string and so named by its bare ARN with no JSON-key selector; the API binds it as
  # `Finnhub:ApiKey` (ADR 0010).
  task_secrets = [
    { name = "DB_USERNAME", valueFrom = local.database_username_secret },
    { name = "DB_PASSWORD", valueFrom = local.database_password_secret },
    { name = "Finnhub__ApiKey", valueFrom = data.aws_secretsmanager_secret.finnhub_api_key.arn },
  ]

  # --- The image assertion, made by two task definitions ----------------------------------------
  #
  # Hoisted into locals because both task definitions name the image independently and Terraform
  # has no shared precondition. See main.tf for why the check reads the whole tag list rather than
  # asking after one tag, and why it lives on the task definitions rather than on the data source.
  image_tag_present = contains(data.aws_ecr_images.api.image_ids[*].image_tag, var.image_tag)

  image_tag_missing_message = "No image tagged ${var.image_tag} in ${local.ecr_repository_name}. Push one with scripts/push-image.sh, or name a tag that is there — a task definition would accept this tag and then fail to pull, with nothing in any application log."
}

resource "aws_ecs_task_definition" "api" {
  family = "compass-api"

  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"

  # Passed explicitly. See the locals above for the fourfold swing that hangs on not defaulting
  # these.
  cpu    = local.task_cpu
  memory = local.task_memory

  execution_role_arn = aws_iam_role.task_execution.arn

  # ⚠️ `task_role_arn` is deliberately absent. See iam.tf: the application makes no AWS calls, so
  # there is nothing for a task role to hold, and leaving it out is what makes that structural.

  # ⚠️ ARM64, matching what `scripts/push-image.sh` builds. Left at the X86_64 default the task
  # fails to pull with an image-manifest mismatch, before any application log exists to look at
  # (ADR 0009, amended).
  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "ARM64"
  }

  # The tag has to be in the registry before this names it. Checked here rather than by the data
  # source in main.tf so that it is an assertion about the deployment and not about being able to
  # read at all — Terraform does not evaluate preconditions for a resource it is destroying, so a
  # teardown never has to justify its tag.
  lifecycle {
    precondition {
      condition     = local.image_tag_present
      error_message = local.image_tag_missing_message
    }
  }

  container_definitions = jsonencode([
    {
      name      = local.container_name
      image     = local.image
      essential = true

      portMappings = [
        {
          containerPort = local.container_port
          protocol      = "tcp"
        },
      ]

      environment = local.task_environment
      secrets     = local.task_secrets

      # ⚠️ Deliberately not here: the origin header. The listener rule checks it in front of the
      # task, so the container is never configured with the value and has no code that reads it.
      # It does still arrive on the wire — an ALB cannot strip a request header — so the one way
      # it could leak from here is request-header logging. The API enables none, and must not.

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.compass.name
          "awslogs-region"        = var.region
          "awslogs-stream-prefix" = local.container_name
        }
      }
    },
  ])
}

# --- Migrations -----------------------------------------------------------------------------------
#
# The same image as the service, with a `command` override. `dotnet ef database update` needs the
# SDK and the sources and the runtime image has neither, so api/Dockerfile also builds `efbundle` —
# the committed migrations compiled into one executable — and the app is a CMD rather than an
# ENTRYPOINT precisely so this override replaces it outright.
#
# ⚠️ Migrations are never applied at application startup, and the reason is not tidiness. A
# deployment runs the old task and the new one at once, so a startup `Migrate()` races itself; and
# the Data Protection key ring lives in a migration-created table, so the loser of that race is not
# a missing feature — **nobody can log in** (#12, #19, ADR 0007).
resource "aws_ecs_task_definition" "migrate" {
  family = local.migrate_task_family

  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"

  cpu    = local.task_cpu
  memory = local.task_memory

  # The same execution role, because this task fetches the same secret. And, like the service,
  # deliberately no task role: efbundle makes no AWS calls either.
  execution_role_arn = aws_iam_role.task_execution.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "ARM64"
  }

  # The same assertion the service's task definition makes, from the same hoisted local. It has to
  # be made twice because this task definition names the image independently and Terraform has no
  # shared precondition — and it matters more here, because the runner below would otherwise spend
  # two minutes waiting for a task that can never pull.
  lifecycle {
    precondition {
      condition     = local.image_tag_present
      error_message = local.image_tag_missing_message
    }
  }

  container_definitions = jsonencode([
    {
      name      = local.migrate_container_name
      image     = local.image
      essential = true

      # No portMappings: nothing listens. This runs, applies what is outstanding, and exits.
      command = ["./efbundle"]

      # ⚠️ The same two lists the service uses, not copies of them. This is the criterion "declared
      # once and shared by both task definitions" (#28) actually holding: a migration applied to one
      # database while the service talks to another is a failure with no error message anywhere.
      environment = local.task_environment
      secrets     = local.task_secrets

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.compass.name
          "awslogs-region"        = var.region
          "awslogs-stream-prefix" = local.migrate_container_name
        }
      }
    },
  ])
}

# What makes the ordering structural on a first manual deploy, which is the question #28 left open
# and the one that breaks login if it is missed.
#
# There is no CI here yet, so there is no pipeline to sequence the two steps — and a sequence a
# human is asked to remember is not a mechanism. This is: `terraform apply` runs the migration task
# to completion, and the service below `depends_on` it, so a Terraform graph that has no way to
# reorder itself is what enforces "migrate, then serve". If the migration fails, the provisioner
# exits non-zero, the apply stops, and the service is never created or updated — so a stack whose
# migration did not run is a stack with no service in front of it, rather than a service serving
# 500s against a schema that is not there.
#
# ⚠️ A `local-exec` provisioner, which is a thing to reach for last and this is the case for it.
# ECS has no "run this task and block" resource; `aws_ecs_task_definition` only registers. The
# alternatives are worse in the specific way that matters: a second `terraform apply -target=...`
# is a step someone skips, and a startup `Migrate()` is the race described above.
resource "terraform_data" "migrate" {
  # Re-run whenever the task definition gets a new revision — a new image tag, a new database
  # endpoint, any change to the shared environment. An unchanged re-apply runs nothing, and a
  # rebuilt stack always runs once because the resource is created fresh with the state.
  triggers_replace = aws_ecs_task_definition.migrate.arn

  provisioner "local-exec" {
    command = "${path.module}/../../scripts/run-one-off-task.sh migrate"

    # Passed rather than looked up. The script defaults these from `terraform output` when it is
    # run by hand, but during this apply the outputs do not exist yet.
    environment = {
      REGION          = var.region
      CLUSTER         = aws_ecs_cluster.compass.name
      TASK_DEFINITION = aws_ecs_task_definition.migrate.arn
      SUBNETS         = join(",", local.foundation.public_subnet_ids)
      SECURITY_GROUPS = local.foundation.task_security_group_id
      LOG_GROUP       = aws_cloudwatch_log_group.compass.name
    }
  }

  # ⚠️ Nothing above references the execution role's *policies*, only the role itself, so the graph
  # is otherwise free to start this task before the agent can pull an image or read the secret.
  # Neither failure writes an application log — the task stops before its container runs, and the
  # reason is only in the stopped task's status.
  depends_on = [
    aws_iam_role_policy_attachment.task_execution,
    aws_iam_role_policy.task_execution_database_secret,
    aws_iam_role_policy.task_execution_finnhub_secret,
  ]
}

# --- Looking inside a database nothing else can reach ---------------------------------------------
#
# The instance is private, in subnets with no route off the VPC, behind a security group that
# admits one other security group. That is the design working, and it also means there is no way to
# run a query from a laptop — no bastion, no public endpoint, and no `ecs execute-command`, which
# would need the task role this stack deliberately does not have.
#
# So the way in is a task in the same network position as the API, with a client in it. It exists
# for the one observation #28 asks for — the live connection ceiling, recorded in infra/README.md —
# and is otherwise a diagnostic, not part of any deploy. It costs nothing while it is not running.
resource "aws_ecs_task_definition" "psql" {
  family = local.psql_task_family

  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"

  cpu    = local.task_cpu
  memory = local.task_memory

  execution_role_arn = aws_iam_role.task_execution.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "ARM64"
  }

  container_definitions = jsonencode([
    {
      name = local.psql_container_name

      # The same image as docker-compose.yml, pulled through ECR Public rather than Docker Hub: the
      # task authenticates to neither, and anonymous Docker Hub pulls are rate-limited per source
      # address, which a public-subnet Fargate task shares with the rest of the region.
      image     = "public.ecr.aws/docker/library/postgres:18-alpine"
      essential = true

      # Overridden per run by scripts/run-one-off-task.sh. This default is the observation the
      # ticket asks for, so the task is useful with no arguments at all.
      command = ["psql", "-c", "SHOW max_connections"]

      # ⚠️ Not local.task_environment. psql reads libpq's variables, not .NET's configuration, so
      # this is the one place the database is described a second way. The *credentials* still come
      # from the one secret below, and the endpoint still comes from the one resource — what
      # differs is only the spelling.
      environment = [
        { name = "PGHOST", value = aws_db_instance.compass.address },
        { name = "PGPORT", value = tostring(aws_db_instance.compass.port) },
        { name = "PGDATABASE", value = aws_db_instance.compass.db_name },
      ]

      # The same two selectors the API's task definitions use, under the names libpq reads.
      secrets = [
        { name = "PGUSER", valueFrom = local.database_username_secret },
        { name = "PGPASSWORD", valueFrom = local.database_password_secret },
      ]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.compass.name
          "awslogs-region"        = var.region
          "awslogs-stream-prefix" = local.psql_container_name
        }
      }
    },
  ])
}

resource "aws_ecs_service" "api" {
  name            = "compass-api"
  cluster         = aws_ecs_cluster.compass.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  # A public subnet and a public address, which looks backwards and is not. ECS has no
  # control-plane exemption: the agent pulls the image, fetches secrets and ships logs over the
  # same network path as application traffic, so a private, NAT-less, endpoint-less task does not
  # start slowly — it cannot start at all. NAT would fix that for about $43/month, more than
  # everything else here put together (ADR 0009). What keeps it safe is the security-group chain:
  # this group admits the load balancer on the container port and nothing else.
  #
  # `assign_public_ip` is the setting that decides it. The subnets' `map_public_ip_on_launch` does
  # nothing for Fargate.
  network_configuration {
    subnets          = local.foundation.public_subnet_ids
    security_groups  = [local.foundation.task_security_group_id]
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = local.container_name
    container_port   = local.container_port
  }

  # The app has no startup work to do — migrations are the one-off task above, already finished by
  # the time this service is touched — so this is short. It only has to cover the runtime starting
  # and Kestrel binding. Nothing here waits on the database: the health path is shallow and never
  # opens a connection, which is what keeps a database fault from becoming an endless
  # kill-and-replace loop.
  health_check_grace_period_seconds = 60

  # The apply does not finish until the service is stable, which is what makes `terraform apply`
  # itself the assertion that the image runs and the health check passes. Without it an apply
  # reports success while the task is still crash-looping, and the failure surfaces minutes later
  # as an unexplained 502 from the public site.
  wait_for_steady_state = true

  # What stops that wait from being an open-ended one. ECS otherwise replaces a failing task
  # forever, so a task that cannot start — a wrong image tag, the X86_64 architecture default, a
  # health check pointed at the wrong path — would keep the apply hanging until Terraform's own
  # timeout rather than failing. The circuit breaker gives up after a run of failed starts and says
  # so.
  #
  # `rollback = false` deliberately. There is no previous good deployment to roll back to on a
  # stack that is created from empty every session, and on a rebuild a rollback would only replace
  # a legible failure with a service quietly running the old image.
  deployment_circuit_breaker {
    enable   = true
    rollback = false
  }

  # Two orderings Terraform cannot infer from the arguments above, and one of them is the whole of
  # this ticket:
  #
  #   * **The migration first.** Nothing in this resource references the migration run, so without
  #     this edge Terraform is free to start the service while the schema is still empty — and the
  #     first thing that breaks is not a feature, it is login, because the Data Protection key ring
  #     lives in a migration-created table.
  #   * **The listener rule first.** ECS refuses to register targets in a target group that is not
  #     attached to a load balancer, and here the *rule* is what attaches it — the listener's own
  #     default action is a fixed response. Without this the first apply fails with a message about
  #     the target group having no load balancer, and the second one succeeds, which is a confusing
  #     way to learn it.
  depends_on = [
    terraform_data.migrate,
    aws_lb_listener_rule.cloudfront,
  ]
}
