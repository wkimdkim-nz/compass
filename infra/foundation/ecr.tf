# Where the container image lives. A standard ECS service has no source-based deployment — there is
# no "point it at a repository and it builds" — so an image in a registry is the only thing a task
# definition can name, and ECR is the registry inside the account.
#
# ⚠️ Pushing an image deploys nothing. There is no auto-deploy-on-push for a standard ECS service.
# A push puts a new tag in this
# repository and changes nothing else; a deployment is a separate, explicit step in the app stack.
#
# The repository persists because it is the thing the ephemeral stack starts *from*: tearing it down
# nightly would mean rebuilding and re-pushing an image before anything could run. Storage is
# $0.10/GB-month and the image is a few hundred megabytes, so the standing cost is cents.

resource "aws_ecr_repository" "api" {
  name = "compass-api"

  # Immutable tags are what make "a tag identifies exactly what is deployed" true rather than
  # conventional. With mutable tags, `compass-api:abc1234` is a pointer someone can move, so the
  # tag recorded in a task definition stops being evidence of what actually ran. Any push naming a
  # tag that already exists is refused — ECR checks the tag, not the digest, so identical bytes are
  # refused too. `scripts/push-image.sh` checks first and says so in plain words, because ECR's
  # own message is about a layer upload and never mentions immutability.
  image_tag_mutability = "IMMUTABLE"

  # Free basic scanning against the CVE database on every push. It reports rather than blocks.
  image_scanning_configuration {
    scan_on_push = true
  }

  # AES256 with an AWS-owned key, stated rather than defaulted. A customer-managed KMS key would
  # add a monthly charge and force `kms:Decrypt` into the execution role for the image pull.
  encryption_configuration {
    encryption_type = "AES256"
  }
}

# Storage is cheap but unbounded: every build pushes a new layer set, and nothing removes the old
# ones.
#
# A push is more than one manifest. `docker push` of a BuildKit image lands an index under the tag
# and, beneath it, the per-architecture image and a provenance attestation as *untagged* entries —
# three images in ECR's count for one tag. That is why the retention rule below counts tagged
# images only: counted as manifests, "keep ten" would be crossed on the fourth push. The untagged
# children are safe from the first rule too — ECR will not expire an image an index still
# references until the index itself goes — so what it actually removes is residue from an
# interrupted push.
#
# ⚠️ Neither rule knows what is deployed. ECR lifecycle policies cannot see ECS, so the tag a live
# task definition names is expired like any other once ten newer tags sit on top of it — and
# because tags are immutable, it cannot simply be pushed back under the same name. Ten is chosen to
# make that implausible rather than impossible: a work session pushes a handful of images, not
# eleven. If the count is ever lowered, or deploys get far more frequent, this becomes a real edge.
# Recovering from it means re-tagging from the commit the tag names, which is exactly why the tag
# is a commit.
resource "aws_ecr_lifecycle_policy" "api" {
  repository = aws_ecr_repository.api.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Untagged images no index references are residue from an interrupted push"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = 1
        }
        action = { type = "expire" }
      },
      {
        # `tagged` requires a pattern, and `*` is every tag: commit hashes share no prefix.
        rulePriority = 2
        description  = "Keep the last ten tags, which is more history than a manual deploy needs"
        selection = {
          tagStatus      = "tagged"
          tagPatternList = ["*"]
          countType      = "imageCountMoreThan"
          countNumber    = 10
        }
        action = { type = "expire" }
      },
    ]
  })
}
