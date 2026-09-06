# The service, its task definition, and where its logs go.

resource "aws_ecs_cluster" "compass" {
  name = "compass"

  # Container Insights is left off. It is a per-metric CloudWatch charge on a stack whose whole
  # point is that it costs nothing while it is torn down, and one task's logs are the observability
  # this phase needs.
}

# Destroyed with the stack, like everything else here, so a work session's logs do not outlive the
# environment they describe. Retention still matters: without it the group is created with logs
# kept forever, and a group recreated on every rebuild would accumulate them indefinitely.
resource "aws_cloudwatch_log_group" "api" {
  name              = "/ecs/compass-api"
  retention_in_days = 7
}

locals {
  # ⚠️ A placeholder, and the only thing in this stack that is. #44 replaces it with the RDS
  # endpoint and adds the two injected secrets beside it.
  #
  # It cannot simply be left out. The API resolves its connection string once at startup and throws
  # when there is none (DatabaseConnectionString.Resolve), so the task needs *a* value to start —
  # but nothing in this slice ever opens a connection, because the health path is shallow by design
  # and no other request is made. That shallowness is exactly what lets this slice land green with
  # no database, and `HealthTests` in the API's own suite is the proof: it boots the app against a
  # database that is not there and demands 200 anyway.
  #
  # 127.0.0.1 rather than a plausible-looking hostname, for two reasons. It is refused instantly
  # instead of waiting out a DNS lookup, so the one background attempt the Data Protection key ring
  # makes at startup fails immediately rather than delaying the first health check. And it can
  # never be mistaken for a database someone forgot to point somewhere.
  #
  # That attempt does leave three `fail:` lines in the log on every task start, ending in
  # `KeyRingProvider[48]` — "Failed to connect to 127.0.0.1:5432". They are expected until #44, and
  # they are not why a task would be unhealthy: the container answers /api/health 200 about two
  # seconds after start with exactly this environment, which is the shape of the whole slice.
  #
  # The rest of the string is already production-shaped, so #44 changes the host and nothing else:
  # `Trust Server Certificate=true` because Npgsql 8 validates certificates under `SSL Mode=Require`
  # and RDS's authority is not in the image's trust store, and `Maximum Pool Size=20` because
  # Npgsql defaults to 100 while a db.t4g.micro tops out near it — and a deployment runs two tasks.
  placeholder_connection_string = "Host=127.0.0.1;Port=5432;Database=compass;SSL Mode=Require;Trust Server Certificate=true;Maximum Pool Size=20;Timeout=2"

  # Declared once, so that when #44 adds the migration task definition the two cannot drift apart
  # and connect to different databases.
  task_environment = [
    # Set explicitly even though Production is already the value an unset ASPNETCORE_ENVIRONMENT
    # takes. The session cookie's SecurePolicy is Always outside Development, and that must not
    # rest on an implicit default (#19).
    { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },

    # Also explicit, and also already the base image's default via ASPNETCORE_HTTP_PORTS. The
    # target group depends on this number silently, so it is written where the target group's
    # reader will find it rather than left to a base image's convention.
    { name = "ASPNETCORE_URLS", value = "http://+:${local.container_port}" },

    { name = "ConnectionStrings__Default", value = local.placeholder_connection_string },
  ]
}

resource "aws_ecs_task_definition" "api" {
  family = "compass-api"

  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"

  # Passed explicitly. See variables.tf for the fourfold swing that hangs on not defaulting these.
  cpu    = var.task_cpu
  memory = var.task_memory

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

      # ⚠️ Deliberately not here: the origin header. The listener rule checks it in front of the
      # task, so the container is never given the value and has no code that would read it.

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.api.name
          "awslogs-region"        = var.region
          "awslogs-stream-prefix" = local.container_name
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

  # The app has no startup work to do — migrations are a separate one-off task (#44) — so this is
  # short. It only has to cover the runtime starting and Kestrel binding.
  health_check_grace_period_seconds = 60

  # The apply does not finish until the service is stable, which is what makes `terraform apply`
  # itself the assertion that the image runs and the health check passes. Without it an apply
  # reports success while the task is still crash-looping, and the failure surfaces minutes later
  # as an unexplained 502 from the public site.
  wait_for_steady_state = true

  # ECS refuses to register targets in a target group that is not attached to a load balancer, and
  # here the *rule* is what attaches it — the listener's own default action is a fixed response.
  # Without this the first apply fails with a message about the target group having no load
  # balancer, and the second one succeeds, which is a confusing way to learn it.
  depends_on = [aws_lb_listener_rule.cloudfront]
}
