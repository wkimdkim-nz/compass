# ADR 0002 — Supabase issues identity; .NET validates the JWT

**Status:** Accepted
**Date:** 2026-07-01

## Context

The app needs authentication. Options weighed: Supabase, AWS Cognito, and self-hosted ASP.NET
Core Identity. Cognito is more AWS-native (a project goal) but has notoriously rough DX; the
project already gets heavy AWS exposure elsewhere (RDS, App Runner, S3/CloudFront, IAM). Supabase
has excellent React DX and gets a beginner to a working logged-in app fastest.

## Decision

Use **Supabase** as the identity provider for v1. Supabase is the identity provider **only**:

- Supabase authenticates users and issues a signed **JWT**.
- The **.NET API validates that JWT** on every request (via Supabase's JWT signing secret / JWKS)
  and reads the **`sub` claim** — the Supabase user id — to identify the caller.
- The React frontend logs in via the Supabase client SDK and attaches the JWT as a
  `Bearer` token on calls to the .NET API.
- .NET does **not** call Supabase's auto-generated data API. Supabase is treated exactly like
  any external IdP: identity in, everything else is ours.

## Consequences

- .NET configures JWT bearer authentication pointing at Supabase's issuer/keys.
- The user's identity inside our domain is a `uuid` = the Supabase `sub`. See ADR 0003.
- **Provider-swappable by design:** because the API only trusts a validated JWT keyed by `sub`,
  swapping to AWS Cognito later is a config change, not a rewrite. Doing so is noted as an optional
  future exercise to gain AWS-native auth experience.
