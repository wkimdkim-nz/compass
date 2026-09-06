#!/usr/bin/env bash
#
# Creates the S3 bucket that holds Terraform state, once, before the first `terraform init`.
#
# This is the one piece of the foundation stack that is not described in Terraform, for the
# obvious reason: a configuration cannot create the bucket its own state lives in. Everything
# else about the bucket is here rather than in a wiki — versioning, so a corrupted state file can
# be rolled back, and a hard public block, because state describes the infrastructure in detail.
#
# Locking is S3-native (`use_lockfile` in infra/foundation/versions.tf), so there is no DynamoDB
# table to create here.
#
# Safe to re-run: every step is checked before it is applied.
set -Eeuo pipefail

ACCOUNT_ID="625495455169"
REGION="ap-southeast-2"
BUCKET="compass-tfstate-${ACCOUNT_ID}"

# The bucket name is hard-coded in the backend block — Terraform does not allow interpolation
# there — so a session pointed at the wrong account would silently write state somewhere else.
caller_account="$(aws sts get-caller-identity --query Account --output text)"
if [[ "$caller_account" != "$ACCOUNT_ID" ]]; then
  echo "This AWS session is account ${caller_account}, not Compass's ${ACCOUNT_ID}." >&2
  exit 1
fi

if aws s3api head-bucket --bucket "$BUCKET" 2>/dev/null; then
  echo "Bucket ${BUCKET} already exists."
else
  echo "Creating ${BUCKET} in ${REGION}..."
  aws s3api create-bucket \
    --bucket "$BUCKET" \
    --region "$REGION" \
    --create-bucket-configuration "LocationConstraint=${REGION}" >/dev/null
fi

echo "Blocking public access..."
aws s3api put-public-access-block \
  --bucket "$BUCKET" \
  --public-access-block-configuration \
  "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"

echo "Enabling versioning..."
aws s3api put-bucket-versioning \
  --bucket "$BUCKET" \
  --versioning-configuration Status=Enabled

echo "Enforcing encryption at rest..."
aws s3api put-bucket-encryption \
  --bucket "$BUCKET" \
  --server-side-encryption-configuration \
  '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"},"BucketKeyEnabled":true}]}'

# Versioning keeps every state file forever otherwise, and state is written on every apply.
echo "Expiring old state versions after 90 days..."
aws s3api put-bucket-lifecycle-configuration \
  --bucket "$BUCKET" \
  --lifecycle-configuration \
  '{"Rules":[{"ID":"expire-noncurrent-state","Status":"Enabled","Filter":{},"NoncurrentVersionExpiration":{"NoncurrentDays":90},"AbortIncompleteMultipartUpload":{"DaysAfterInitiation":7}}]}'

echo
echo "Ready. Next: terraform -chdir=infra/foundation init"
