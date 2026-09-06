#!/usr/bin/env bash
#
# Builds the SPA and publishes it to the foundation stack's bucket.
#
# Deploying the SPA is a step of its own, not a side effect of `terraform apply`: Terraform owns
# the bucket, and this owns what is in it. The two are separable because the bucket persists.
#
# Usage: scripts/deploy-spa.sh
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

bucket="$(terraform -chdir=infra/foundation output -raw spa_bucket)"
distribution="$(terraform -chdir=infra/foundation output -raw cloudfront_distribution_id)"

echo "Building the SPA..."
npm --prefix web ci
npm --prefix web run build

# Two passes, because the two kinds of file want opposite cache lifetimes. Vite fingerprints
# everything under assets/, so those can be cached forever; index.html names them, so a cached
# copy of it would pin the browser to the previous build.
echo "Syncing to s3://${bucket}..."
aws s3 sync web/dist "s3://${bucket}" \
  --delete \
  --exclude "index.html" \
  --cache-control "public, max-age=31536000, immutable"
aws s3 sync web/dist "s3://${bucket}" \
  --exclude "*" \
  --include "index.html" \
  --cache-control "no-cache"

echo "Invalidating the distribution..."
aws cloudfront create-invalidation \
  --distribution-id "$distribution" \
  --paths "/*" \
  --query 'Invalidation.Id' --output text

echo
echo "Published. Verify with: scripts/verify-foundation.sh"
