# ADR 0010 — SEC EDGAR for fundamentals and Finnhub for the quote; US domestic filers only

**Status:** Accepted
**Date:** 2026-09-17
**Supersedes:** ADR 0005

## Context

ADR 0005 chose Financial Modeling Prep on the expectation that *"free tier is expected to suffice
for personal use; paid (~$22–30/mo) is the escape hatch."* Two research notes tested that before
any adapter was written:

- [`../research/fmp-free-tier.md`](../research/fmp-free-tier.md) (2026-09-13): the free plan limits
  fundamentals and quotes to a **fixed list of 87 US symbols**; TTM statements exist only on the
  $99–139/mo Ultimate plan; no FMP page names ASX or NZX on any plan; the personal-use licence
  forbids integrating the data into *"applications accessible by any third parties"*. The ADR's
  price estimate buys Starter, which is US-only and still has no TTM statements, and costs more per
  month than the whole ephemeral AWS stack.
- [`../research/edgar-finnhub-alternatives.md`](../research/edgar-finnhub-alternatives.md)
  (2026-09-14): ADR 0005's named fallback, **SEC EDGAR + Finnhub free**, covers every base fact
  and the history for **US domestic SEC filers**, at $0. EDGAR's coverage was **observed**;
  Finnhub's quote endpoint is taken from its **documentation**, since every call without an
  account returned 401 and no Finnhub data was seen. The cost moves
  into Compass: EDGAR returns raw filed XBRL facts, so revenue-tag mapping, TTM derivation and the
  share-count choice are ours. Neither it nor any other free source found covers ASX or NZX.

The decision was taken in the Phase 1 grilling session of 2026-09-17, which also reconciled the
draft build tickets #54–#63.

## Decision

**Fundamentals come from SEC EDGAR; the current price comes from Finnhub's free tier. FMP is out.**

- **Scope: US domestic SEC filers only.** The ticker lookup resolves tickers through EDGAR's
  ticker file and normalises 10-K/10-Q facts. A ticker EDGAR does not know is a **404**. A ticker
  EDGAR knows but Compass cannot normalise — a foreign private issuer filing a 20-F in IFRS tags
  with no quarterly facts, for example — is a **422 with a plain reason** ("not a US domestic
  filer"), never a 404, so the user knows to type the numbers in rather than suspect a typo.
  Anything outside this scope is manual entry, which ADR 0004 already makes a first-class path.
  NZX/ASX lookup is a Later item with a research spike attached; no free source was found.
- **Two ports, not one.** A *fundamentals source* (EDGAR) and a *quote source* (Finnhub) are
  separate seams with separate stubs, composed by one lookup service behind
  `GET /api/tickers/{symbol}`. They are different vendors with different failure modes and
  different licences, and the price is one field.
- **Partial failure is a 200.** If the quote source fails and the fundamentals source succeeds,
  the response carries the fundamentals, a null price, and a list naming what could not be
  fetched; the form pre-fills everything else and the user types the price. If the fundamentals
  source fails there is nothing to seed, so that is a **503**.
- **Shares outstanding is the diluted weighted average** (`us-gaap:WeightedAverageNumberOfDilutedSharesOutstanding`)
  for both the current figure and the history. It was the only share concept present for every
  US filer checked (the cover-page `dei` count is absent for Alphabet and Shopify), and the
  dilution default is a CAGR over history, which needs one concept every year. It is a period
  average, not a point-in-time count, and the glossary says so.
- **TTM is derived.** EDGAR has no TTM concept, and recent 10-Ks carry no fourth-quarter facts, so
  Q4 = FY − nine-month YTD. Revenue needs an ordered candidate list of tags per period, because
  the tag a company files under changes over time. Net income is `NetIncomeLoss`. Current net
  margin is TTM net income ÷ TTM revenue. **Duplicates are resolved by filing date:** the same
  period appears more than once in `companyfacts`, as a comparative in later filings and as a
  restatement when one is made, so for each `(start, end)` period the fact taken is the one from
  the **latest-filed 10-K or 10-Q** (including `/A` amendments), which is also the row EDGAR puts
  the `frame` tag on. Facts from other forms are ignored. This normalisation is unit-tested against
  **recorded `companyfacts` fixtures** checked into the test project, including at least one
  repeated comparative and one restated period.
- **Staleness is visible.** `companyfacts` was observed missing recent filings for three of
  fourteen filers checked. The lookup reports the period end its TTM figures run to, and the UI
  shows "figures to <period end>" beside the base facts.
- **Cache in memory, not in the database.** Quote 15 minutes; fundamentals and the ticker map
  24 hours; one copy per running task, lost on restart. The environment is ephemeral and
  single-user, so a process cache loses nothing worth keeping, and it keeps ADR 0004's "no base
  facts in the database" literally true. A Postgres cache stays a Later item. How this squares
  with "fetched live" is recorded as an amendment to ADR 0004.
- **Configuration.** The Finnhub API key lives in the **foundation** stack's Secrets Manager, so
  it survives `terraform destroy`; its value is set by hand after the apply and never enters
  Terraform state or variables; the app stack's task definition injects it **by name**, the way
  it already injects the database credentials, mirroring the origin-header pattern of ADR 0009.
  Locally it comes from `dotnet user-secrets`. EDGAR needs no key but refuses requests without a
  **User-Agent carrying a contact address**; that address is an **environment variable** in every
  environment, not a value in source. **With either setting absent, the stub is registered only in
  Development and test; in every other environment the API refuses to start** and names the missing
  setting, the way a missing database credential already does. A deployed task therefore either
  talks to the real providers or is not healthy, and the smoke test cannot be satisfied by a stub.
  Tests run against the stubs only. The two configuration key names — `Finnhub:ApiKey` and
  `Edgar:ContactEmail` — and where each environment gets them from are recorded in one place,
  [`infra/README.md`](../../infra/README.md#the-two-market-data-settings) (#68).

## Consequences

- ADR 0005 is superseded. Every "FMP" in the glossary, roadmap and ADR 0004 is read as the
  fundamentals source or the quote source as appropriate; ADR 0009's remark that "Phase 1's FMP
  call costs nothing" holds unchanged for EDGAR and Finnhub, since the reason was the public
  subnet, not the vendor.
- **Compass owns a normalisation module** — the largest single piece of Phase 1 work and the
  reason it is its own build ticket. It is the part of the domain a projection calculator should
  understand anyway, and it tests well against recorded fixtures.
- **Licence posture.** EDGAR content may be copied and redistributed without permission, so
  caching it is unconstrained. Finnhub's personal-use terms forbid redistribution of data "or
  derived results" and require deletion when the subscription ends; the quote is cached for
  15 minutes in memory and never written to the database, which satisfies that clause.
- **Two accounts are one.** Only Finnhub needs a sign-up; the key is a human step. There is no
  FMP account to create.
- Rate limits stop being a design input: EDGAR asks for under 10 requests/second and Finnhub free
  allows 60/minute, against one user with a 24-hour fundamentals cache.
- The deployed smoke test hits the **real** providers on one fixed ticker (AAPL), because a stub
  in production would prove the seam but not the joint.
