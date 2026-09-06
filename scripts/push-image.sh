#!/usr/bin/env bash
#
# Builds the API's runtime image and pushes it to the foundation stack's ECR repository.
#
# ⚠️ This deploys nothing. A standard ECS service has no auto-deploy-on-push — that was App
# Runner's behaviour, and App Runner is out (ADR 0009). This puts a tag in the registry; naming it
# in a task definition and rolling the service is a separate, explicit step in the app stack.
#
# The tag is the commit the image was built from, and the script refuses to build a dirty `api/`
# tree, because a tag that does not identify exactly what is inside the image is worse than no tag:
# it reads like evidence and is not. Pass a tag explicitly to override — for a throwaway
# experiment, say — and accept that the tag then means whatever you remember it meaning.
#
# Usage: scripts/push-image.sh [tag]
set -Eeuo pipefail

# Fargate is ~20% cheaper on ARM64, the .NET base images are multi-arch, and the development
# machine is Apple silicon, so this builds natively rather than under emulation.
#
# ⚠️ The app stack's task definition must therefore declare
# `runtime_platform { cpu_architecture = "ARM64" }`. Leave it at the X86_64 default and the task
# never starts — it fails to pull with an image-manifest mismatch, before any application log
# exists. `scripts/verify-foundation.sh` asserts the pushed image's architecture for that reason.
PLATFORM="${PLATFORM:-linux/arm64}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

for cmd in aws docker git terraform; do
  command -v "$cmd" >/dev/null || { echo "missing required command: $cmd" >&2; exit 2; }
done

repository="$(terraform -chdir=infra/foundation output -raw ecr_repository_url)"
# <account>.dkr.ecr.<region>.amazonaws.com/<name> — the registry and region are already in the URL,
# so there is no second place to keep them in step.
registry="${repository%%/*}"
region="$(cut -d. -f4 <<<"$registry")"
repository_name="${repository#*/}"

if [[ -n "${1:-}" ]]; then
  tag="$1"
else
  # Only `api/` goes into the image, so only `api/` has to be clean for the commit to describe it.
  if [[ -n "$(git status --porcelain -- api)" ]]; then
    echo "api/ has uncommitted changes, so a commit tag would not describe the image." >&2
    echo "Commit them, or pass a tag explicitly: scripts/push-image.sh <tag>" >&2
    exit 1
  fi
  tag="$(git rev-parse --short=12 HEAD)"
fi

image="${repository}:${tag}"

# Tags are immutable in this repository, so a second push of *different* bytes under an existing
# tag is refused by ECR — with a message about layer uploads that does not mention immutability.
# Catching it here says what actually happened.
if existing="$(aws ecr describe-images --region "$region" \
  --repository-name "$repository_name" --image-ids "imageTag=${tag}" \
  --query 'imageDetails[0].imageDigest' --output text 2>/dev/null)" && [[ -n "$existing" && "$existing" != "None" ]]; then
  echo "${image} already exists (${existing})."
  echo "Tags are immutable: commit your change and push under its own tag."
  exit 1
fi

echo "Building ${image} for ${PLATFORM}..."
docker build --platform "$PLATFORM" -t "$image" api

echo "Signing in to ${registry}..."
aws ecr get-login-password --region "$region" | docker login --username AWS --password-stdin "$registry"

echo "Pushing ${image}..."
docker push "$image"

digest="$(aws ecr describe-images --region "$region" \
  --repository-name "$repository_name" --image-ids "imageTag=${tag}" \
  --query 'imageDetails[0].imageDigest' --output text)"

echo
echo "Pushed. Nothing is deployed — the app stack names this image and rolls the service."
echo "  image:  ${image}"
echo "  digest: ${digest}"
