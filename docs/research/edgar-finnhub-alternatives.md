# Research — Does SEC EDGAR + Finnhub (free) cover what Compass needs?

Tests the fallback named in [ADR 0005](../adr/0005-market-data-fmp.md): *"Fallback provider if FMP disappoints: SEC EDGAR (free fundamentals) + Finnhub (free quote)."* The [FMP free-tier note](fmp-free-tier.md) found that FMP's free plan does not suffice, so this note checks whether the fallback does, and compares the two. Research date: **2026-09-14** (NZST; the servers' `Date` headers read 2026-09-13 UTC).

**Method.**
- **EDGAR:** every endpoint was called live with `curl` and a declared User-Agent, `Compass research contact@example.com`. SEC documentation pages were fetched the same way: sec.gov returned HTTP 403 to a browser-style User-Agent and 200 to the declared one.
- **Finnhub:** the docs, pricing and terms pages are rendered client-side, so they were read in a browser. The OpenAPI file (`/static/swagger.json`) was fetched with `curl`. API calls were made only unauthenticated or with `token=demo`. No account was created with any provider.
- **Other providers:** pages were read the same way, and only their own demo keys were used.

This note reports **facts only**. Whether to change ADR 0005 is a separate decision.

What Compass needs, from the ADR, [glossary](../glossary.md) and [roadmap](../roadmap.md) Phase 1: a server-side .NET API that looks up a ticker and returns **current quote/price**, **TTM revenue**, **net income (→ net margin)**, **shares outstanding** (the glossary says diluted), and **3–5 years of annual statements** to pre-fill Bear/Base/Bull. Results are cached server-side, fields stay editable as a manual fallback, and there is one user, who is in New Zealand.

---

## Bottom line

- **For US domestic filers (10-K/10-Q), EDGAR + Finnhub free covers all five needs, but Compass has to do the normalisation itself.**
  - **Quote:** Finnhub `/quote`, documented for US stocks.
  - **TTM revenue and net income:** derived from EDGAR quarterly and annual XBRL facts.
  - **Shares outstanding:** EDGAR `dei:EntityCommonStockSharesOutstanding` or `us-gaap:WeightedAverageNumberOfDilutedSharesOutstanding`, or Finnhub `/stock/profile2` `shareOutstanding`.
  - **Annual history:** EDGAR has back to roughly 2009, well beyond 3–5 years.
- **Compared with FMP free, this removes the 87-symbol ceiling for US tickers. It does not change non-US coverage.**
- **EDGAR has no TTM field and no single revenue tag, so both must be derived.**
  - **Revenue tags change over time.** Across the six US filers checked, the tag moved between `SalesRevenueNet`, `Revenues`, `RevenueFromContractWithCustomerExcludingAssessedTax`, `SalesRevenueGoodsNet` and `RevenuesNetOfInterestExpense` (JPM). A single company changed tags two or three times (§1.3).
  - **The fourth quarter is not filed as its own fact.** Recent 10-Ks carry only full-year values, so Q4 = FY − 9-month YTD. Apple's TTM revenue to 2026-06-27 works out to **$466,823M** that way (§1.6).
  - **Net income is the consistent one.** `NetIncomeLoss` was present for every US filer checked.
- **Shares outstanding is not uniform on EDGAR.** `dei:EntityCommonStockSharesOutstanding` is **absent from companyfacts for Alphabet and Shopify** (§1.5). `WeightedAverageNumberOfDilutedSharesOutstanding` was present for every US filer checked.
- **EDGAR freshness is documented as "under a minute", but three of the 14 filers checked were stale on 2026-09-14** (§1.7):
  - **Coca-Cola:** its 10-Q filed 2026-07-29 was missing from `companyfacts`, although the `frames` API had it.
  - **BHP:** its 20-F filed 2026-08-18 was missing.
  - **TSMC:** its 20-F filed 2026-04-16 added only cover-page facts, no IFRS financials.
- **Finnhub free:**
  - **Price and limits:** $0, labelled "Personal Use", **60 API calls/minute**, plus a platform-wide 30 calls/second.
  - **Included:** fundamental coverage is **US**. "Financials As Reported" and "Key Metrics" are ticked for Free. `/stock/profile2` is the free version of the company profile.
  - **Excluded:** **"Standardized Financial Statements" is not included**, and the `/stock/financials` docs heading carries a **Premium** badge. That route is the one with `freq=ttm`.
  - **Quote and international data:** `/quote` has no Premium badge and is described as real-time US data. International market data is not ticked for Free (§2).
- **Finnhub's docs do not establish whether free `/stock/metric` returns TTM revenue, a TTM margin or shares.** The response map is untyped in the OpenAPI spec, and the docs' sample shows only a `netMargin` annual series and a few price metrics. **Confirming it needs a real key** (§10).
- **Non-US coverage is where the pair is weakest.**
  - **EDGAR** covers SEC registrants only. Foreign private issuers (BHP, TSMC) file an **annual** 20-F in `ifrs-full` tags, sometimes in a non-USD currency (TSMC reports TWD), with almost no XBRL on 6-Ks. That means **no quarterly TTM**, and `ifrs-full` **frames returned 404** on every call tried.
  - **ASX and NZX listings are not in EDGAR at all.**
  - **Finnhub free** fundamental and market-data coverage is **US only**, although Finnhub's own exchange-code sheet includes `AX` (ASX) and `NZ` (NZX).
- **Terms:**
  - **EDGAR:** needs no key; the request must *"declare your user agent"* and stay under *"10 requests per second"*. Its content *"may be copied or further distributed … without the SEC's permission"*.
  - **Finnhub:** all plans are personal use unless stated otherwise. Redistribution or shared access, including of **derived results**, needs written approval, and data must be deleted when the subscription ends (§7).
- **Verdict:**
  - **Covered:** EDGAR + Finnhub free covers Compass's needs **for US-listed domestic SEC filers**, which FMP free does not beyond 87 symbols.
  - **Biggest gaps:**
    - (1) No ASX/NZX coverage.
    - (2) Weak foreign-filer coverage: annual only, IFRS tags, no quarterly TTM.
    - (3) Compass must own revenue-tag mapping, TTM derivation and share-count choice.
    - (4) Observed `companyfacts` staleness for some filers.
    - (5) Finnhub's free TTM metric fields cannot be confirmed without a key.

---

## 1. SEC EDGAR (data.sec.gov)

### 1.1 Endpoints

From [EDGAR Application Programming Interfaces](https://www.sec.gov/search-filings/edgar-application-programming-interfaces). The page header is dated June 6, 2024 and the page was *"Last Reviewed or Updated: April 8, 2025"*.

- **Auth:** *"These APIs do not require any authentication or API keys to access."*
- **Scope:** *"the submissions history by filer and the XBRL data from financial statements (forms 10-Q, 10-K,8-K, 20-F, 40-F, 6-K, and their variants)."*
- **Aggregation rule:** the XBRL APIs aggregate facts that use *"a non-custom taxonomy (e.g. us-gaap, ifrs-full, dei, or srt)"* and that *"Apply to the entire filing entity"*. Company-specific extension tags are therefore never returned.

| Endpoint | What it returns | Compass use |
|---|---|---|
| `https://www.sec.gov/files/company_tickers.json` | `{"0":{"cik_str":1045810,"ticker":"NVDA","title":"NVIDIA CORP"},…}` (observed; 798 KB uncompressed; `last-modified: Fri, 11 Sep 2026`) | ticker → CIK |
| `https://www.sec.gov/files/company_tickers_exchange.json` | `{"fields":["cik","name","ticker","exchange"],"data":[[1045810,"NVIDIA CORP","NVDA","Nasdaq"],…]}` (observed) | ticker → CIK + exchange |
| `data.sec.gov/submissions/CIK##########.json` | Entity metadata (`tickers`, `exchanges`, `fiscalYearEnd`, `category`…) and `filings.recent` as columnar arrays (`accessionNumber`, `filingDate`, `reportDate`, `acceptanceDateTime`, `form`, `isXBRL`, `isInlineXBRL`, `primaryDocument`…). The docs say it holds *"at least one year's of filing or to 1,000 (whichever is more)"*, with older pages under `filings.files`. | Latest 10-K/10-Q, fiscal year end |
| `data.sec.gov/api/xbrl/companyfacts/CIK##########.json` | Every concept for one company: `facts.{taxonomy}.{tag}.units.{unit}[]` | **One call gets all history for a ticker** |
| `data.sec.gov/api/xbrl/companyconcept/CIK##########/{taxonomy}/{tag}.json` | One concept for one company | Narrow fetch |
| `data.sec.gov/api/xbrl/frames/{taxonomy}/{tag}/{unit}/{period}.json` | One fact per company for a calendar period: `CY####` (annual, *"365 days +/- 30 days"*), `CY####Q#` (quarter, *"91 days +/- 30 days"*), `CY####Q#I` (instant) | Cross-sectional; see pitfalls in §1.6 |

**Bulk:** `companyfacts.zip` and `submissions.zip` are *"recompiled nightly"* (same page).

**Size:** Apple's `companyfacts` was 3.79 MB uncompressed and 272 KB gzipped (observed). It has 503 `us-gaap` concepts and 2 `dei` concepts.

### 1.2 Shape of a fact (observed, Apple CIK 0000320193)

```json
{"start":"2025-12-28","end":"2026-03-28","val":111184000000,"accn":"0000320193-26-000013",
 "fy":2026,"fp":"Q2","form":"10-Q","filed":"2026-05-01","frame":"CY2026Q1"}
```

- **`fy`/`fp`/`form` describe the filing the fact came from, not the fact's period.** A 10-Q with `fp:"Q2"` carries both the 3-month value (2025-12-28 → 2026-03-28) and the 6-month YTD value (2025-09-28 → 2026-03-28). Both have `fp:"Q2"`; only `start`/`end` tell them apart.
- **Comparatives repeat.** The Q3 FY2025 3-month revenue (94,036M) appears twice: from the 2025 10-Q and again as a comparative in the 2026 10-Q.
- **`frame` sits on at most one row per period.** On the repeated comparative, the frame tag moved to the later filing (`CY2025Q2` is on the row filed 2026-07-31).
- **Instant facts have only `end`** (e.g. `dei:EntityCommonStockSharesOutstanding`, frame `CY2026Q2I`).

### 1.3 Revenue: tag inconsistency (observed)

The periods below are observed period-end ranges of each tag in each company's `companyfacts`, fetched 2026-09-14.

| Company | Tags seen, by period covered |
|---|---|
| **Apple** (FYE late Sep) | `SalesRevenueNet` FY2007 → Jun 2018 (label: *"Revenue, Net (Deprecated 2018-01-31)"*); `Revenues` 11 facts, only in the FY2018 10-K; `RevenueFromContractWithCustomerExcludingAssessedTax` Sep 2017 → Jun 2026 |
| **Microsoft** (FYE Jun) | `Revenues` 2007 → 2010; `SalesRevenueNet` 2009 → Mar 2018; `RevenueFromContractWithCustomerExcludingAssessedTax` Jun 2016 → Jun 2026 |
| **Tesla** | `Revenues` 2009 → Jun 2026, continuous; `RevenueFromContractWithCustomerExcludingAssessedTax` 2018 → 2026 (45 facts, a subset) |
| **Coca-Cola** | `SalesRevenueGoodsNet` 2007 → Jun 2018; `Revenues` Dec 2016 → Apr 2026 |
| **JPMorgan** (bank) | `InterestAndDividendIncomeOperating` 2007 → 2012; `Revenues` 2007 → Dec 2025 (latest filing Feb 2026 10-K); `RevenuesNetOfInterestExpense` 2013 → Jun 2026 |
| **Shopify** | `RevenueFromContractWithCustomerExcludingAssessedTax` 2017 → 2023 (40-F); `Revenues` 2022 → Jun 2026 (10-K/10-Q) |
| **BHP** (20-F, IFRS) | `ifrs-full:Revenue` (USD) 2016 → Jun 2025 |
| **TSMC** (20-F, IFRS) | `ifrs-full:Revenue` in **TWD** 2015 → 2024 and **USD** 2017 → 2024; also `ifrs-full:RevenueFromContractsWithCustomers` (TWD) |

What follows (observation, not an SEC statement):
- **No single tag gives a continuous 5-year revenue series across companies.** A lookup needs an ordered candidate list, chosen per period. Tags overlap in time: Apple's FY2017 value (229,234M) appears under both `SalesRevenueNet` and `RevenueFromContract…`.
- **Some companies report more than one revenue concept at once.** JPMorgan's 10-Q revenue is under `RevenuesNetOfInterestExpense`, while `Revenues` stops at the 10-K.
- **The SEC does not normalise.** The API page only says the facts are standard-taxonomy facts that are *"comparable between companies and across time"*. It gives no guidance on choosing between revenue tags.

### 1.4 Net income

`us-gaap:NetIncomeLoss` (label *"Net Income (Loss) Attributable to Parent"*) was present and current for Apple, Microsoft, Tesla, Coca-Cola, JPMorgan and Shopify. Coca-Cola and Tesla also report `ProfitLoss`, which includes non-controlling interests. IFRS filers use `ifrs-full:ProfitLossAttributableToOwnersOfParent` and `ifrs-full:ProfitLoss` (BHP, TSMC).

**Net margin is not a reported fact.** It is net income ÷ revenue, so it inherits the revenue-tag problem in §1.3.

### 1.5 Shares outstanding

| Concept | Kind | What it is (observed) | Coverage in companyfacts (observed) |
|---|---|---|---|
| `dei:EntityCommonStockSharesOutstanding` | Instant, cover page | Description: *"Indicate number of shares or other units outstanding of each of registrant's classes of capital or common stock…"*. Dated at the cover-page date, e.g. Apple **14,594,180,000** at 2026-07-17 (10-Q filed 2026-07-31) | Present: Apple, Microsoft, Tesla, Coca-Cola, JPMorgan; BHP and TSMC (annual, 20-F only). **Absent: Alphabet and Shopify.** Both have only `dei:EntityPublicFloat` |
| `us-gaap:WeightedAverageNumberOfDilutedSharesOutstanding` | Duration | Diluted weighted average for the period, e.g. Apple **14,714,676,000** for the quarter to 2026-06-27 | Present for every US filer checked, **including Alphabet** (12,274,000,000, six months to 2026-06-30) **and Shopify** (1,302,627,927) |
| `us-gaap:CommonStockSharesOutstanding` | Instant, balance sheet | Balance-sheet count | Alphabet 12,230,000,000 at 2026-06-30. Shopify's only values are 2015 and 0 |

- **Why the `dei` count is missing for Alphabet and Shopify: cannot establish.** The likely explanation is that the cover-page count is reported per share class, which would fail the *"entire filing entity"* rule in §1.1. The SEC page does not say so for this tag.
- **The glossary's "Shares outstanding (diluted)" matches `WeightedAverageNumberOfDilutedSharesOutstanding`**, which was the only share concept present for every US filer checked. It is a period average, not a point-in-time count.

### 1.6 Annual vs quarterly, TTM, and frames pitfalls

- **Annual:** 10-K rows with `fp:"FY"` and a ~365-day `start`→`end`.
- **Quarterly:** 10-Q rows carry 3-month and YTD durations (§1.2).
- **No fiscal-Q4 value.** Apple's FY2021+ 10-Ks carry **no 3-month facts** for revenue; the FY2020 10-K was the last with any (observed). So Q4 = FY − 9-month YTD.
- **No TTM concept or TTM endpoint.** The API page documents none.

**Worked TTM from Apple's observed facts** (period to 2026-06-27, USD millions):

| Item | Revenue | Net income |
|---|---|---|
| FY2025 (2024-09-29 → 2025-09-27, 10-K) | 416,161 | 112,010 |
| − 9M FY2025 YTD (→ 2025-06-28) | 313,695 | 84,544 |
| + 9M FY2026 YTD (→ 2026-06-27) | 364,357 | 101,464 |
| **TTM** | **466,823** | **128,930** (net margin 27.6%) |

Summing four quarters gives the same revenue: (416,161 − 313,695) + 143,756 + 111,184 + 109,417 = 466,823.

**Pitfalls with `frames`:**
- **Last filing wins, even a proxy.** *"The xbrl/frames API aggregates one fact for each reporting entity that is last filed that most closely fits the calendrical period requested."* JPMorgan's `NetIncomeLoss` frames for **CY2024 and CY2025 are attached to rounded values from a DEF 14A proxy filing** (58,500,000,000 and 57,000,000,000), not the 10-K values (58,471,000,000 and 57,048,000,000) (observed).
- **Frames are calendar-aligned, not fiscal.** *"Data users should be mindful different reporting start and end dates"* (API page). Apple's fiscal-Q4 3-month value has no frame because no such fact exists.
- **`ifrs-full` frames returned 404** (`NoSuchKey`) for `Revenue/USD/CY2024`, `CY2025`, `CY2026`, `Revenue/EUR/CY2024` and `ProfitLoss/USD/CY2023`. `us-gaap` frames returned 200. The API page lists `ifrs-full` among the aggregated taxonomies, so **the docs and the live behaviour disagree**.

### 1.7 Freshness

**Documented:**
- *"The JSON structures are updated throughout the day, in real time, as submissions are disseminated."*
- *"the xbrl APIs are updated with a typical processing delay of under a minute. However these processing delays may be longer during peak filing times."* ([API page](https://www.sec.gov/search-filings/edgar-application-programming-interfaces))
- The Webmaster FAQ says filings are *"often available on sec.gov within 1-3 minutes of the EDGAR system timestamp"* ([Webmaster FAQ](https://www.sec.gov/about/webmaster-frequently-asked-questions)).

**Observed 2026-09-14:** the latest periodic filings in `submissions` compared with the facts present in `companyfacts`:

| Ticker | Latest periodic filing (submissions) | In companyfacts? |
|---|---|---|
| NVDA, MSFT, AMZN, GOOGL, META, PEP, JPM, WMT, SHOP, NKE, CRM | 10-Q/10-K filed 2026-07-09 → 2026-08-28 | Yes (latest two each) |
| **KO** | 10-Q filed **2026-07-29** (`0001628280-26-050503`, `isXBRL=1`) | **No.** Newest fact filed 2026-04-30. But `frames/us-gaap/Revenues/USD/CY2026Q2` **does** carry KO from that accession (13,380,000,000). `companyconcept/CIK0000021344/us-gaap/Revenues.json` returned `"units":{"USD":{}}` on two fetches |
| **BHP** | 20-F filed **2026-08-18** (`isXBRL=1`) | **No.** Newest fact filed 2025-08-22 |
| **TSM** | 20-F filed **2026-04-16** (`isXBRL=1`) | Only `dei` facts from it; newest `ifrs-full:Revenue` is FY2024 |

Whether the KO, BHP and TSM gaps are transient or systemic **cannot be established** from SEC documentation.

### 1.8 Access rules, auth, CORS

- **Auth:** none (quoted in §1.1).
- **Rate:** *"Current max request rate: 10 requests/second"* ([Accessing EDGAR Data](https://www.sec.gov/search-filings/edgar-search-assistance/accessing-edgar-data)). Developer Resources says *"no more than 10 requests per second, regardless of the number of machines used"* ([Developer Resources](https://www.sec.gov/about/developer-resources)). The Privacy and Security Policy adds: *"further requests from the IP address(es) may be limited for a brief period. Once the rate of requests has dropped below the threshold for 10 minutes, the user may resume"* ([Privacy](https://www.sec.gov/about/privacy-information)).
- **User-Agent:** *"Please declare your user agent in request headers"*, with the sample `User-Agent: Sample Company Name AdminContact@<sample company domain>.com` (Accessing EDGAR Data; Webmaster FAQ).
- **Observed without a declared User-Agent:**
  - A request to `data.sec.gov` returned **HTTP 403** from Akamai, as did one to `www.sec.gov/files/company_tickers.json`. The body was an HTML page titled **"Request Rate Threshold Exceeded"**, returned for a single request.
  - A browser-style User-Agent also got 403 on sec.gov HTML pages. The declared User-Agent got 200 everywhere.
- **CORS:** *"data.sec.gov does not support Cross Origin Resource Scripting (CORS)."* (API page).
  - **Observed:** a `GET` sent with an `Origin` header returned 200 with no `Access-Control-Allow-Origin`. An `OPTIONS` preflight returned 403 (`MissingAuthenticationTokenException`) while carrying `access-control-allow-origin: *`.
  - **Impact:** none, because Compass calls from the .NET API.
- **Support:** *"we cannot provide technical support for developing or debugging scripted downloading processes"* (API page).

### 1.9 Coverage: foreign filers, ASX, NZX

- **Only SEC registrants are covered.** A company that does not file with the SEC has no CIK and no facts.
- **Foreign private issuers** (BHP, TSMC) file an **annual 20-F** with IFRS facts (`ifrs-full:*`). Their interim reports go out as 6-Ks, and **1 of TSMC's 664 and 3 of BHP's 814** recent 6-Ks have `isXBRL=1` (observed). **So there is no quarterly XBRL series from which to build a TTM.** Currency follows the filing: TSMC reports TWD and USD.
- **40-F filers** (Canadian) can appear: Shopify's history includes 40-F facts before it moved to 10-K/10-Q.
- **ASX and NZX:** EDGAR has no data for companies listed only there. Dual-listed companies with US-registered securities (e.g. BHP, NYSE-listed per `submissions`) appear only through their SEC filings.

---

## 2. Finnhub free tier

### 2.1 Plans (read from [finnhub.io/pricing](https://finnhub.io/pricing), 2026-09-14)

The page shows two plans: **Free** ($0/month) and **All-In-One** ($3,500/month, billed annually). Both are labelled "Personal Use. Terms apply". Cells were read from the rendered table: a green check icon means included, a grey cross icon means not.

| Row | Free | All-In-One |
|---|---|---|
| Limit | **60 API calls/minute** | Market data 900/min; Fundamental 300/min |
| Fundamental data coverage | **US** | Global |
| Company Profile | **v2** | v1 with more information |
| Standardized Financial Statements | **✗** | 30+ years annually and quarterly |
| Financials As Reported | **✓** | ✓ |
| Key Metrics | **✓** | ✓ |
| SEC filings | ✓ | ✓ |
| Dividends | ✗ | 30 years |
| US market data coverage | US | US |
| OHLC (US) | ✗ | 30+ years |
| Websocket | 50 symbols | Unlimited |
| International market data (TSX, LSE, Euronext, Deutsche Börse) | **✗** | Tick-level data |
| Estimates coverage | US | Global |

- **Footnote:** outside LSE (15-minute delay), international markets on All-In-One are end-of-day only.
- **Not on the table:** no row for "Quote", and no row for ASX or NZX.
- **Self-contradiction:** the FAQ refers to plans named "Market data Basic", "Standard", "Fundamental-1" and "Fundamental-2", none of which appear on the pricing page ([FAQ](https://finnhub.io/faq)). The current lineup of paid tiers between $0 and $3,500 **cannot be established** from Finnhub's pages.

### 2.2 Endpoints relevant to Compass

From the [API docs](https://finnhub.io/docs/api) (rendered) and [`swagger.json`](https://finnhub.io/static/swagger.json). Base URL `https://finnhub.io/api/v1`. "Premium" means the docs heading carries a Premium badge.

| Compass need | Route | Docs badge | What the docs say (paraphrased) |
|---|---|---|---|
| Quote | `/quote?symbol=` | none | Real-time quote for US stocks. Constant polling is discouraged in favour of websockets. International real-time prices are for Enterprise clients via a partner feed. Fields: `c` current, `d`, `dp`, `h`, `l`, `o`, `pc` (the sample also shows `t`) |
| Basic financials | `/stock/metric?symbol=&metric=all` | none | Basic financials such as margin, P/E and 52-week range. Response has `metric` (key → value map) and `series` (time series). **The OpenAPI schema types both maps only as `object`.** The sample shows `series.annual.netMargin`, `salesPerShare`, `currentRatio` and a few price metrics (`52WeekHigh`, `beta`…). **No `revenueTTM`, TTM margin or shares field appears in the docs.** |
| Company profile | `/stock/profile2?symbol=` | none | The free version of Company Profile (`/stock/profile` is **Premium**). Includes `shareOutstanding` and `marketCapitalization`, **with no unit stated**. The sample shows `shareOutstanding: 4375.47998046875` for AAPL, which reads as millions, but that is an inference |
| As-reported statements | `/stock/financials-reported?symbol=&freq=annual\|quarterly` | none | As-reported financials. Also offered as a bulk "SEC Financials" dataset on Kaggle. Filterable by `symbol`, `cik`, `accessNumber`, `from`/`to`. The sample is an Apple 10-K |
| Standardised statements | `/stock/financials?statement=ic&freq=annual\|quarterly\|ttm\|ytd` | **Premium** | Standardised global statements, 30+ years; `ttm` is offered for the income and cash-flow statements |
| Symbol search | `/search?q=`, `/stock/symbol?exchange=` | none | Symbology is `TICKER.EXCHANGE_CODE`. The docs' `/search` sample returns `AAPL.SW`, `APC.BE`, `APC.DE` alongside `AAPL` |
| International filings | `/stock/international-filings` | **Premium** | Filings for international companies; the docs describe these as the documents behind Finnhub's fundamental data |

- **Where the data comes from:** the FAQ says fundamentals come from companies' filings, and market data from the exchanges, ActivFinancial, EDI and QuoteMedia ([FAQ](https://finnhub.io/faq)).
- **Tag normalisation in `financials-reported`: cannot establish.** The docs point it at an SEC dataset, so it is presumably SEC filers only. Whether its statements carry the same raw tag variety as EDGAR (§1.3) is not stated.

### 2.3 Auth, CORS, observed responses

- **Auth:** the docs say every GET needs the key either as the `token` query parameter or in an `X-Finnhub-Token` header ([docs, Authentication](https://finnhub.io/docs/api)). `swagger.json` declares only the query form (`"api_key": {"type":"apiKey","name":"token","in":"query"}`). The header form keeps the key out of URLs and logs.
- **Observed without a key (2026-09-14):** `/quote?symbol=AAPL`, `/stock/metric`, `/stock/profile2`, `/stock/financials-reported`, `/stock/financials` and `/quote?symbol=BHP.AX` all returned **HTTP 401** `{"error":"Please use an API key."}`. The same routes with `token=demo` returned **401** `{"error":"Invalid API key."}`. **No Finnhub data could be observed without an account.**
- **CORS:** the 401 responses carried `access-control-allow-origin: *`. This does not matter for a server-side caller.

---

## 3. Coverage: need × source

**FMP free** figures are from the [FMP note](fmp-free-tier.md). **"87 US"** = FMP's fixed 87-symbol list. **US domestic** = companies filing 10-K/10-Q.

| Compass need | FMP free | SEC EDGAR | Finnhub free | EDGAR + Finnhub combined |
|---|---|---|---|---|
| **Current quote** | 87 US; Timeframe "End of Day" | **None** (no price data) | `/quote`, US stocks, documented as real-time; no Premium badge | **US listings** (Finnhub) |
| **TTM revenue** | No TTM statements; `key-metrics-ttm` for 87 US | **Derived**: FY − 9M + 9M from 10-K/10-Q facts; revenue tag chosen per company and period; US domestic only | Standardised `freq=ttm` is **Premium**. `/stock/metric` TTM keys **undocumented** (cannot establish). As-reported quarterly is ticked Free, so derivation is possible | **US domestic, derived** from EDGAR |
| **Net income → margin** | 87 US | `NetIncomeLoss`, consistent across US filers checked; margin derived | `series.annual.netMargin` in docs sample; TTM margin key cannot establish | **US domestic** |
| **Shares outstanding** | `weightedAverageShsOut(Dil)` in statements, 87 US | Diluted weighted average for all US filers checked; `dei` cover count **missing for some** (GOOGL, SHOP) | `/stock/profile2` `shareOutstanding` (free version; unit unstated), US | **US** |
| **3–5 years annual** | 87 US; 5 years (FMP also says 4) | **Yes**; facts from ~2009 (Apple from FY2007) | As-reported ticked Free, depth for Free unstated; standardised is Premium | **Yes** (EDGAR) |
| **Foreign filers with US listings** (BHP, TSM) | Some in the 87 (TSM, SONY, BABA…) | Annual only (20-F, `ifrs-full`, possibly non-USD); **no quarterly TTM**; stale facts observed | Quote for a US listing likely falls under "US" (cannot establish without key) | Annual fundamentals + US quote, **no TTM**, currency/ADR handling unresolved |
| **ASX `.AX` / NZX `.NZ`** | None | **None** | **Not on Free** (coverage "US"; international ✗), though exchange codes `AX` and `NZ` exist | **None** |
| **Ticker lookup** | `search-symbol` global | `company_tickers.json` (SEC registrants) | `/search`, `/stock/symbol` | Both |
| **Key needed** | Yes | **No** (User-Agent with contact) | Yes | One key (Finnhub) |

---

## 4. Rate limits

| Source | Limit | On exceed | Source |
|---|---|---|---|
| EDGAR | **10 requests/second** per user, across all machines | IP limited "for a brief period"; resumes after 10 minutes below threshold. An undeclared User-Agent got 403 on the first request | [Privacy and Security Policy](https://www.sec.gov/about/privacy-information), [Developer Resources](https://www.sec.gov/about/developer-resources) |
| EDGAR | No daily cap documented | — | — |
| Finnhub Free | **60 API calls/minute** | **HTTP 429**; 429 body not documented | [Pricing](https://finnhub.io/pricing), [docs, Rate Limits](https://finnhub.io/docs/api/rate-limit) |
| Finnhub (all plans) | **30 API calls/second** on top of plan limits | 429 | [Docs](https://finnhub.io/docs/api/rate-limit), [ToS "API Limit and Access"](https://finnhub.io/terms-of-service) |
| FMP Free (comparison) | 250 calls/day, 500 MB / 30 days | 429 | [FMP note §2](fmp-free-tier.md) |

**Arithmetic, not a provider statement:**
- **EDGAR:** seeding a ticker is 1 `companyfacts` call, plus the ticker map, which can be cached for a day. Optionally add 1 `submissions` call to check freshness.
- **Finnhub:** 1 `/quote` call plus optionally 1 `/stock/profile2`.
- **Result:** about 3–4 calls per new ticker. For one user, neither limit binds.

---

## 5. History depth

| Source | What it says / what was observed |
|---|---|
| EDGAR, documentation | XBRL *"was first required by the SEC in 2009"* ([API page](https://www.sec.gov/search-filings/edgar-application-programming-interfaces)). *"EDGAR started in 1994/1995"* ([Accessing EDGAR Data](https://www.sec.gov/search-filings/edgar-search-assistance/accessing-edgar-data)), but that refers to filings, not XBRL facts |
| EDGAR, observed | Apple revenue/net income from FY2007 (comparatives in its 2009 10-K); Microsoft from 2007; Tesla from 2009; JPMorgan from 2007; Shopify from 2013; TSMC from 2015; BHP from 2016. **More than 5 annual years for every company checked** |
| EDGAR Financial Statement Data Sets | Quarterly bulk sets *"January 2009 - June 2026"* ([page](https://www.sec.gov/data-research/sec-markets-data/financial-statement-data-sets)) |
| Finnhub, FAQ | 30 years of fundamental data globally; 35+ years of US tick data and 1 year for international ([FAQ](https://finnhub.io/faq)). Not broken down by plan |
| Finnhub, pricing | Standardised statements "30+ years annually and quarterly" on All-In-One only. **The depth of Free "Financials As Reported" and "Key Metrics" series is not stated. Cannot establish.** |

---

## 6. Non-US coverage

| Market | FMP Free | EDGAR | Finnhub Free |
|---|---|---|---|
| US domestic filers | 87 symbols | **All with XBRL filings** | **US** (fundamentals, quote) |
| US-listed foreign filers (20-F / 40-F) | Some ADRs in the 87 | Annual IFRS or US GAAP facts; no quarterly | Quote probably (US listing): cannot establish |
| LSE, TSX, Euronext, Xetra | — | Only via any SEC filing | **✗** (international market data not on Free) |
| **ASX `.AX`** | — | **None** | **✗ on Free.** The exchange code `AX` ("ASX - ALL MARKETS", MIC `XASX`) is in Finnhub's exchange list |
| **NZX `.NZ`** | — | **None** | **✗ on Free.** The exchange code `NZ` ("NEW ZEALAND EXCHANGE LTD", MIC `XNZE`) is in Finnhub's exchange list |

- **Exchange list:** from the Google Sheet linked in Finnhub's `/stock/symbol` docs, exported as CSV on 2026-09-14.
- **Not stated anywhere:** no Finnhub page fetched says whether **any** plan includes ASX or NZX **fundamentals**. The docs mention ASX only for the Premium `/ca/symbol-change` route, and Australia only for insider transactions. **Cannot establish.**

---

## 7. Terms and licence

### SEC EDGAR

- **Reuse:** *"Information presented on sec.gov is considered public information and may be copied or further distributed by users of the web site without the SEC's permission. Please consider appropriate citation to the SEC as the source."* The same section asks users not to use the SEC seal or logos ([Privacy and Security Policy, "Website Dissemination"](https://www.sec.gov/about/privacy-information)).
- **Automated access:** *"The SEC does not allow 'unclassified' bots or automated tools to crawl the site"*. Requests must declare a User-Agent (§1.8). *"Note that this policy may change"* (same page).
- **No key, account, caching restriction or deletion obligation** was found on the pages read. Citing the SEC is a request, not a requirement.

### Finnhub ([Terms of Service](https://finnhub.io/terms-of-service))

These clauses are paraphrased, except for one short quote.

- **Personal use only.** Every plan on the website is personal use unless explicitly stated otherwise. A business may not use a personal plan, even internally, without written approval. You do not qualify for a personal plan if you are a registered securities professional, use the data for a business, register under a business name, or deduct the cost as a business expense. A personal signup certifies non-professional status on an exchange subscriber agreement, citing the UTP plan data policies.
- **No redistribution.** Users agree to *"not redistribute or share access to data or derived results"* from Finnhub with any third party without written approval.
- **Data deletion.** Data must be deleted when the subscription to it ends. The FAQ repeats this and ties it to exchange and partner terms ([FAQ](https://finnhub.io/faq)).
- **Intellectual property.** Using the site grants no ownership or licence to the data. Users are responsible for complying with copyright on data accessed through it.
- **Limits and changes.** Access is 30 calls/second over plan limits; there is one limit for fundamental data and one for market data. The terms can change without notice. Inactive users may be deleted from time to time.
- **Commercial use** needs a commercial licence via sales. Free accounts need no credit card, and personal plans have no trial ([FAQ](https://finnhub.io/faq)).
- **Attribution:** none required that I found in the ToS, pricing page or FAQ.

**Open interpretation:** how the Finnhub clauses apply to a single-user app on the public internet that shows projections derived from Finnhub prices to its owner. That depends on reading "derived results" and "share access", which the text does not settle; it is flagged in §10. The FMP note flagged the same open point for FMP.

---

## 8. Other free/cheap alternatives (brief)

Each entry is limited to the specific gap it might close. Provider pages are paraphrased.

- **Alpha Vantage.** The most material gap-closer found, but only for **TTM without derivation**.
  - **Free limits:** 25 API requests/day ([support](https://www.alphavantage.co/support/)). Premium starts at $49.99/month for 75 requests/min with no daily limit ([premium](https://www.alphavantage.co/premium/)).
  - **`OVERVIEW` (observed, demo key, IBM):** returned `RevenueTTM` (69094998000), `ProfitMargin` (0.155), `SharesOutstanding` (942134000), `DilutedEPSTTM` and `LatestQuarter` (2026-06-30) in one call. That covers TTM revenue, margin and shares in one call, for symbols it supports.
  - **Quotes:** `GLOBAL_QUOTE` updates end-of-day for all users; real-time and 15-minute-delayed US quotes are premium ([documentation](https://www.alphavantage.co/documentation/)).
  - **Markets:** the documentation's sample tickers cover the US, LSE (`.LON`), TSX/TSXV (`.TRT`, `.TRV`), Shanghai/Shenzhen, XETRA (`.DEX`) and BSE. **No ASX or NZX sample.** The demo key refused `OVERVIEW` for `BHP.AX`.
  - **Source of fundamentals:** the income statement is described as normalised to the SEC's GAAP and IFRS taxonomies.
  - **Unestablished:** international fundamentals coverage.
- **EODHD.**
  - **Plans ([pricing](https://eodhd.com/pricing)):** Free Package $0, 20 API calls/day, data range "past year", personal use. Fundamentals Data Feed $59.99/mo ($49.99/mo annually). EOD All World $19.99/mo. All-in-One $99.99/mo.
  - **Demo key:** `fundamentals/AAPL.US` returned data. `fundamentals/FPH.NZ` and `real-time/FPH.NZ` returned *Forbidden*.
  - **NZX:** the static [list-of-stock-markets](https://eodhd.com/list-of-stock-markets) HTML did not mention New Zealand; the page may render client-side. It advertises ASX data through sales.
  - **Unestablished:** whether Free includes fundamentals, and NZX coverage (feature-matrix icons were not captured).
- **Twelve Data ([pricing](https://twelvedata.com/pricing)).**
  - **Basic (free):** 8 API credits/minute (800/day), 3 markets. Usage is labelled internal non-display: the tooltip says the data cannot be displayed to users or used in production.
  - **Credit cost:** `/income_statement` costs **100 credits per symbol**, more than Basic's 8/minute. By that arithmetic, statements are unreachable on Basic.
  - **Paid:** Grow is shown at $29–$79/mo depending on credits. Delayed ASX data is a data add-on on a higher tier.
- **Massive (formerly Polygon; `polygon.io/pricing` redirects to [massive.com/pricing](https://massive.com/pricing)).**
  - **Stocks Basic:** $0, 5 API calls/minute, 2 years history, end-of-day, all US tickers, individual use. Starter is $29/mo.
  - **Financials:** priced separately, in a section that loads client-side and was not captured. **Cannot establish.**
  - **Markets:** US only.
- **Tiingo.** The pricing page is client-rendered (37 characters of static text). **Not assessed.**

None of these showed free ASX/NZX fundamentals on the pages read.

---

## 9. What this means for the ADR (facts only)

- **The named fallback covers US domestic filers on free tiers, and FMP free does not** beyond 87 symbols. It **does not** cover ASX/NZX, which FMP free does not either (§3, §6).
- **The fallback moves work into Compass.** EDGAR returns raw filed facts. The glossary's Base Facts and the Phase 1 pre-fill would need Compass to own:
  - an ordered revenue-tag list per period (§1.3)
  - TTM derivation, including Q4 = FY − 9M (§1.6)
  - a share-count choice (`dei` vs diluted weighted average, §1.5)
  - de-duplication of repeated comparatives (§1.2)
  - avoiding `frames` values from proxy filings (§1.6)
- **Staleness shows in `companyfacts` for some filers** (§1.7). `submissions` lists the filing within minutes (documented); on 2026-09-14 `frames` held KO's newest quarter while `companyfacts` did not.
- **Secrets and config.**
  - **Finnhub:** needs one API key, which fits the ADR's Secrets Manager design; the header form `X-Finnhub-Token` is supported.
  - **EDGAR:** needs no key, but needs a **User-Agent containing a contact address**. Without one, requests are refused (observed 403).
- **Licence posture differs.**
  - **EDGAR:** public information with no restriction on caching or display.
  - **Finnhub:** personal-use, no redistribution of data "or derived results", and deletion when the subscription ends (§7). ADR 0004 says base facts are "never persisted", while this brief says results are cached server-side. Finnhub's deletion clause applies to whatever is stored.
- **CORS is irrelevant to the ADR's server-side design:** EDGAR does not support it and Compass registers no CORS policy.
- **Finnhub fields:** whether free Finnhub can replace EDGAR derivation for TTM revenue or margin depends on undocumented `/stock/metric` keys (§2.2). The only documented TTM route, `/stock/financials?freq=ttm`, is Premium.

---

## 10. Open questions — confirm with a real key

1. **Finnhub `/stock/metric?metric=all` on a free key:** which `metric` keys come back for a US ticker (any `revenueTTM`, TTM net margin, shares)? Are `series.annual` revenue or margin series present, and how many years?
2. **Finnhub `/quote` for `BHP.AX`, `FPH.NZ` and ADR `BHP` on a free key:** status and body. Is the US quote real-time, delayed or end-of-day on Free? The docs say real-time; the pricing page has no quote row.
3. **Finnhub `/stock/financials-reported` on a free key:** does it return data, how many years, does `freq=quarterly` work, does it cover 20-F filers, and are concepts raw `us-gaap_*` tags (inheriting §1.3) or normalised?
4. **Finnhub `/stock/profile2` `shareOutstanding` unit and as-of date** (the docs sample implies millions; no unit is stated).
5. **Finnhub 429 body and reset behaviour** at 61 calls/minute and at 31 calls/second.
6. **EDGAR `companyfacts` staleness:** is the KO gap transient or recurring, and what about BHP's 2026 20-F and TSMC's FY2025 IFRS facts? Re-fetch in a few days, and/or ask `structureddata@sec.gov`, listed on Accessing EDGAR Data for structured-data questions.
7. **EDGAR `companyconcept` returning `"units":{"USD":{}}`** for KO `Revenues` while `companyfacts` holds 106 facts: bug or rebuild window?
8. **`ifrs-full` frames returning 404** although the API page names `ifrs-full` as aggregated.
9. **Why `dei:EntityCommonStockSharesOutstanding` is absent** for Alphabet and Shopify: are per-class cover-page facts excluded by the "entire filing entity" rule? Also, which share concept should Compass use for multi-class companies?
10. **ADR share ratio:** for US-listed ADRs such as BHP and TSM, per-share maths needs ADR-to-ordinary conversion. None of the endpoints read here was seen to provide it.
11. **Finnhub licence reading:** does showing projections derived from Finnhub prices to the app's single owner, on a public-internet app behind auth, fall under "derived results" or "share access"? Ask Finnhub if it matters.
12. **Does any Finnhub plan include ASX/NZX fundamentals?** The pricing page says only "Global"; the exchange codes exist.

---

## Sources

All fetched **2026-09-14** (NZST).
- **SEC pages:** fetched with `curl` and a declared User-Agent (sec.gov returned 403 to a browser User-Agent).
- **Finnhub docs, pricing, terms and FAQ:** rendered in a browser; static HTML is a JavaScript shell.
- **Everything else:** as noted per entry.

SEC documentation:
- [EDGAR Application Programming Interfaces](https://www.sec.gov/search-filings/edgar-application-programming-interfaces): endpoints, no auth, forms covered, taxonomy/entity rule, frames periods, CORS, bulk ZIPs, update delay
- [Accessing EDGAR Data](https://www.sec.gov/search-filings/edgar-search-assistance/accessing-edgar-data): 10 requests/second, User-Agent declaration, EDGAR start year
- [Developer Resources](https://www.sec.gov/about/developer-resources): 10 requests/second across machines, unclassified bots
- [Webmaster FAQ](https://www.sec.gov/about/webmaster-frequently-asked-questions): rate, User-Agent sample, 1–3 minute filing availability
- [Privacy and Security Policy](https://www.sec.gov/about/privacy-information) (redirect target of `sec.gov/privacy`): rate enforcement and 10-minute recovery, "Website Dissemination" reuse clause
- [Financial Statement Data Sets](https://www.sec.gov/data-research/sec-markets-data/financial-statement-data-sets): 2009 – June 2026 coverage

SEC first-party API observations (declared User-Agent):
- `https://www.sec.gov/files/company_tickers.json`, `https://www.sec.gov/files/company_tickers_exchange.json`
- `https://data.sec.gov/api/xbrl/companyfacts/CIK{0000320193,0000789019,0001318605,0000021344,0000019617,0001594805,0001652044,0000811809,0001046179}.json` (Apple, Microsoft, Tesla, Coca-Cola, JPMorgan, Shopify, Alphabet, BHP, TSMC)
- `companyfacts` and `submissions` for NVDA, AMZN, META, PEP, WMT, NKE, CRM (freshness table)
- `https://data.sec.gov/submissions/CIK{0000320193,0000021344,0001046179,0000811809}.json`
- `https://data.sec.gov/api/xbrl/companyconcept/CIK0000320193/us-gaap/RevenueFromContractWithCustomerExcludingAssessedTax.json`, `…/CIK0000021344/us-gaap/Revenues.json`
- `https://data.sec.gov/api/xbrl/frames/us-gaap/RevenueFromContractWithCustomerExcludingAssessedTax/USD/CY2025Q1.json`, `…/dei/EntityCommonStockSharesOutstanding/shares/CY2025Q2I.json`, `…/us-gaap/Revenues/USD/CY2026Q2.json`, `…/us-gaap/Revenues/USD/CY2024.json`, `…/us-gaap/NetIncomeLoss/USD/CY2025.json` (200); `…/ifrs-full/Revenue/USD/CY2024|CY2025|CY2026.json`, `…/ifrs-full/Revenue/EUR/CY2024.json`, `…/ifrs-full/ProfitLoss/USD/CY2023.json` (404)
- The same companyfacts URL with no User-Agent (403 "Request Rate Threshold Exceeded"), with an `Origin` header (no ACAO), and as an `OPTIONS` preflight (403)

Finnhub:
- [Pricing](https://finnhub.io/pricing): plan table, per-cell check/cross icons, limits, footnotes
- [API documentation](https://finnhub.io/docs/api), including [rate-limit](https://finnhub.io/docs/api/rate-limit), [quote](https://finnhub.io/docs/api/quote), [company-basic-financials](https://finnhub.io/docs/api/company-basic-financials), [company-profile2](https://finnhub.io/docs/api/company-profile2), [financials-reported](https://finnhub.io/docs/api/financials-reported), [financials](https://finnhub.io/docs/api/financials): authentication, 429, 30/second, endpoint descriptions, Premium badges, sample responses
- [OpenAPI spec `swagger.json`](https://finnhub.io/static/swagger.json): security definition, parameters, response schemas (`MetricMap` untyped)
- [Terms of Service](https://finnhub.io/terms-of-service): personal use, redistribution, deletion, 30/second
- [FAQ](https://finnhub.io/faq): data sourcing, years of data, commercial use, free account, data deletion, plan names
- [Exchange codes sheet](https://docs.google.com/spreadsheets/d/1I3pBxjfXB056-g_JYf_6o3Rns3BV2kMGG1nCatb91ls/edit?usp=sharing), linked from the `/stock/symbol` docs and read via CSV export: `AX` and `NZ` rows
- API observations: `https://finnhub.io/api/v1/{quote?symbol=AAPL, stock/metric?symbol=AAPL&metric=all, stock/profile2?symbol=AAPL, stock/financials-reported?symbol=AAPL&freq=annual, stock/financials?symbol=AAPL&statement=ic&freq=annual, quote?symbol=BHP.AX}`, with no token (401 "Please use an API key.") and `token=demo` (401 "Invalid API key.")

Other providers (§8):
- Alpha Vantage: [support](https://www.alphavantage.co/support/), [premium](https://www.alphavantage.co/premium/), [documentation](https://www.alphavantage.co/documentation/); demo-key calls `query?function=OVERVIEW&symbol=IBM`, `GLOBAL_QUOTE&symbol=IBM`, `OVERVIEW&symbol=BHP.AX` (refused), `SYMBOL_SEARCH&keywords=tesco`
- EODHD: [pricing](https://eodhd.com/pricing), [list of stock markets](https://eodhd.com/list-of-stock-markets); demo-key calls `api/fundamentals/AAPL.US`, `api/fundamentals/FPH.NZ` and `api/real-time/FPH.NZ` (Forbidden)
- Twelve Data: [pricing](https://twelvedata.com/pricing)
- Massive (formerly Polygon): [massive.com/pricing](https://massive.com/pricing) (`polygon.io/pricing` redirects there)
- Tiingo: [pricing](https://www.tiingo.com/about/pricing) (client-rendered; not assessed)

Not used as sources: third-party blogs, Reddit, or other secondary write-ups.
