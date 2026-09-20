# The Finnhub API key: the one credential Compass holds that AWS did not mint (ADR 0010).
#
# It lives *here*, in the persistent stack, for the reason the origin header does: the app stack is
# destroyed between work sessions, and a key that went with it would have to be pasted back in on
# every rebuild. The app stack finds it **by name** and hands it to the task as the environment
# variable `Finnhub__ApiKey`, which the API binds as `Finnhub:ApiKey`.
#
# ⚠️ Terraform creates the secret and never learns its value. It writes a placeholder once, when the
# secret is created, and the operator overwrites that by hand after the first apply
# (infra/README.md has the command). The key is therefore in no variable, no `.tfvars` and no state
# file.

locals {
  # A fixed name, because being fixed is the coupling between the two stacks. It is also the name
  # `scripts/verify-foundation.sh` looks up.
  finnhub_secret_name = "compass/finnhub-api-key"

  # Recognisable on purpose: `scripts/verify-foundation.sh` fails while the secret still holds it,
  # which is the only thing that tells "never set" apart from "set" without printing the key.
  finnhub_secret_placeholder = "placeholder-set-by-hand-after-apply"
}

resource "aws_secretsmanager_secret" "finnhub_api_key" {
  name        = local.finnhub_secret_name
  description = "The Finnhub API key the quote source calls with. Value set by hand; Terraform holds a placeholder."

  # No recovery window, as for the origin header: a deleted secret otherwise holds its name hostage
  # for 30 days, and the name is what the app stack looks up. The value is not lost with it — the
  # key can be read again from the Finnhub dashboard.
  recovery_window_in_days = 0
}

# The placeholder, written by a create-time provisioner rather than an
# `aws_secretsmanager_secret_version` with `ignore_changes`, and the difference is the second time
# the key is set. A version resource tracks one version id. The first hand-set value demotes that
# version to AWSPREVIOUS; the second leaves it with no staging label at all, the provider then reads
# it as deleted, and the next apply "recreates" it — putting the placeholder back as AWSCURRENT over
# a live key. `ignore_changes` does not help, because nothing changed: the resource is simply gone.
#
# A provisioner runs when this resource is created and never again, and the resource is replaced
# only if the secret itself is. So Terraform writes the placeholder exactly once per secret and
# holds no opinion about its value afterwards.
resource "terraform_data" "finnhub_api_key_placeholder" {
  triggers_replace = aws_secretsmanager_secret.finnhub_api_key.id

  provisioner "local-exec" {
    command = "aws secretsmanager put-secret-value --region \"$REGION\" --secret-id \"$SECRET_ID\" --secret-string \"$PLACEHOLDER\" --query VersionId --output text"

    environment = {
      REGION      = var.region
      SECRET_ID   = aws_secretsmanager_secret.finnhub_api_key.id
      PLACEHOLDER = local.finnhub_secret_placeholder
    }
  }
}
