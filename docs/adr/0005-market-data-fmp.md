# ADR 0005 — Financial Modeling Prep (FMP) as the market-data provider

**Status:** Accepted
**Date:** 2026-07-01

## Context

The calculator needs current base facts (price, TTM revenue, shares outstanding) and historical
financials (to derive sensible Bear/Base/Bull defaults). Providers weighed: FMP, SEC EDGAR +
Finnhub, Alpha Vantage, Polygon. The app is fundamentals-heavy, not price-tick-heavy.

## Decision

Use **Financial Modeling Prep (FMP)**, introduced in **iteration 1** (not the walking skeleton).

- One JSON API covers the whole data surface: income statements (revenue, net income → margin),
  shares outstanding, current quote, and historical statements for default-seeding.
- The **.NET API** calls FMP server-side and shapes the response for the frontend. The FMP API key
  is stored in **AWS Secrets Manager** (not in the client, not in source).
- Free tier is expected to suffice for personal use; paid (~$22–30/mo) is the escape hatch if
  usage grows.

## Consequences

- Base facts are fetched live and never persisted (see ADR 0004). Fields stay editable as a
  fallback when FMP lacks coverage for a ticker.
- Introduces a vendor dependency and rate limits — handle gracefully (cache recent lookups; show a
  clear error and fall back to manual entry on failure).
- Sequencing: the walking skeleton (auth → one endpoint → one row → deployed) ships **before** any
  FMP integration, so the vendor is not stacked onto seam-de-risking work.
- Fallback provider if FMP disappoints: SEC EDGAR (free fundamentals) + Finnhub (free quote).
