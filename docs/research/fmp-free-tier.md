# Research — Does the FMP free tier cover what Compass needs?

Tests the assumption in [ADR 0005](../adr/0005-market-data-fmp.md): *"Free tier is expected to suffice for personal use; paid (~$22–30/mo) is the escape hatch."* Research date: **2026-09-13**. Every FMP page cited was fetched live that day, either as raw HTML with `curl` or rendered in a browser (FMP's site returns HTTP 403 to plain fetchers, and several tables only appear once JavaScript runs). Prices were read from the personal-use pricing page that day, with both the Monthly and Annual toggles. This note reports **facts only**. Whether to change ADR 0005, move to a paid tier or use the named fallback (SEC EDGAR + Finnhub) is a separate decision.

What Compass needs, from the ADR and the brief: a server-side .NET API that gets **current quote/price**, **TTM revenue**, **net income (→ margin)**, **shares outstanding**, and **3–5 years of annual statements** to seed Bear/Base/Bull defaults. Results are cached, there is one user, and he is in New Zealand.

---

## Bottom line

- **The free (Basic) plan limits fundamentals and quotes to a fixed list of 87 US symbols.** FMP's plan comparison marks Income Statement, Balance Sheet, Cash Flow, Key Metrics, Ratios, Key Metrics TTM, Ratios TTM, Enterprise Values, Company Share Float and Stock Quote for Basic as *"Symbol Limited to AAPL, TSLA, AMZN and 84 more"* ([pricing](https://site.financialmodelingprep.com/pricing-plans)). The Income Statement docs give the full list under Free (§4). All 87 are US tickers. **Outside that list, the free tier gives Compass no statements and no quote.** This is the central finding, and it contradicts the ADR's assumption.
- **The needed endpoints exist on `/stable/`, but TTM statements are not on free.** `income-statement`, `balance-sheet-statement`, `cash-flow-statement`, `key-metrics`, `ratios`, `quote`, `profile`, `shares-float`, `enterprise-values` and `search-symbol` are all documented. **Income/Balance/Cash-flow Statements TTM are "—" on Basic, Starter and Premium; only Ultimate has them.** `key-metrics-ttm` and `ratios-ttm` are on Basic, limited to the 87 symbols. Symbol and name search is **global on Basic**. Legacy `/api/v3/` routes have been *"accessible only after sign-in, and only to eligible accounts"* since 2025-08-27 ([changelog](https://site.financialmodelingprep.com/developer/docs/changelog)). A new signup should assume `/stable/` only.
- **Rate limits:** Free is **250 calls/day** and a **500 MB trailing-30-day bandwidth** cap. Starter is 300/min (20 GB), Premium 750/min (50 GB), Ultimate 3,000/min (150 GB). The documented error for exceeding a limit is **HTTP 429**, *"Too many requests (rate limit exceeded)"*. FMP does not document the 429 response body or how the daily cap resets.
- **History depth:** Basic's **"Historical Data Range" is 5 years**, and the statement `limit` parameter on Free is capped at *"Maximum 5 Responses per API Call"*. **That satisfies 3–5 years of annual data, but only for the 87 sample symbols.** FMP contradicts itself on the exact depth: the general FAQ says *"up to 5 years of annual statements for US companies"*, while the Income Statement page's FAQ says *"4-year income statements available for free users"*. Either figure clears 3 years. From the docs I cannot establish whether Free returns quarterly periods.
- **Non-US coverage:** **Basic has none. It is the 87 US symbols for fundamentals and quotes, and US exchanges only for profiles.** Starter adds all **US** symbols. Premium covers **US, UK and Canada**, so `.L` and `.TO`. Only **Ultimate** is *"Full Global Coverage"*. **No FMP page I fetched names ASX or NZX.** FMP says only *"60+ stock exchanges"*, so I cannot establish from FMP's docs that `.AX` or `.NZ` are covered on any plan. Coverage of Euronext and Tokyo is also not named on the plan comparison.
- **Prices (personal use, read 2026-09-13):** Starter **$29/mo** monthly or $19/mo billed annually. Premium **$69/mo** or $49/mo annually. Ultimate **$139/mo** or $99/mo annually. **The ADR's "~$22–30/mo escape hatch" matches Starter, and Starter is US-only with no TTM statements.** The cheapest plan that is even labelled global is Ultimate, at $99–$139/mo.
- **Terms:** the personal-use licence is *"strictly for their own personal, non-business and non-commercial purposes"*. It forbids integrating the data *"into any tools or applications accessible by any third parties"* and sharing the API key ([ToS §2.2.1](https://site.financialmodelingprep.com/terms-of-service)). Showing FMP data in applications *"designed for utilization by multiple individuals"* needs a separate agreement (§2.2.2). The Basic tier's **Timeframe is "End of Day"**, against "Real-time" on paid plans. I found no attribution requirement.
- **Verdict: the free tier does not meet Compass's needs.** It meets the 3–5-year annual depth, but only for 87 hard-coded US tickers. It has no TTM statements and no ASX/NZX (or any non-US) coverage. The ADR's price estimate buys Starter, which fixes the symbol list for US tickers only. Nothing FMP publishes shows that **any** plan covers NZX.

---

## 1. Which endpoints the free plan includes

### Stable vs legacy

- **Stable is current.** The docs give the base URL as `https://financialmodelingprep.com/stable/` ([docs quick start](https://site.financialmodelingprep.com/developer/docs)). Stable routes shipped as a beta on 2025-01-10: *"Beta Release for Stable Routes (v2): Introduced modular documentation, standardized endpoints…"* ([changelog](https://site.financialmodelingprep.com/developer/docs/changelog)).
- **Legacy is gated.** The changelog entry for 2025-08-27, *"Legacy routes now auth-gated"*, reads: *"Legacy API routes are no longer shown in the public documentation. They are now accessible only after sign-in, and only to eligible accounts."* A public index of "V1 Endpoints" is still listed at [/developer/docs/legacy-endpoints](https://site.financialmodelingprep.com/developer/docs/legacy-endpoints). **FMP does not define "eligible accounts"**, and I found no statement on whether new free signups qualify. The FAQ still uses `/api/v3/quote/AAPL` in its examples ([FAQ](https://site.financialmodelingprep.com/faqs)), which looks stale next to the changelog.
- **What this means for a new signup today:** build against `/stable/`. Do not count on `/api/v3/` working.

### Authentication

*"All API requests must be authorized using an API key. Authorization can be done using either request headers or URL query parameters"*, with the header written as `apikey: YOUR_API_KEY` ([docs](https://site.financialmodelingprep.com/developer/docs)). The header form keeps the key out of URLs and access logs.

### Endpoint × plan (personal-use plans)

Cell values are the icon alt text from the plan comparison on the [pricing page](https://site.financialmodelingprep.com/pricing-plans), matched row by row to plan columns in the page HTML. **87 sample** = *"Symbol Limited to AAPL, TSLA, AMZN and 84 more"*. **—** = not included.

| Compass need | Stable route | Basic (free) | Starter | Premium | Ultimate |
|---|---|---|---|---|---|
| Quote / price | `/stable/quote` | 87 sample | US | US, UK, CA | Global |
| Quote (short) | `/stable/quote-short` | *"AAPL, TSLA, AMZN and 204 more"* | US | US, UK, CA | Global |
| Batch quote | `/stable/batch-quote` | — | — | US, UK, CA | Global |
| Company profile | `/stable/profile` | **US exchanges** | Global | Global | Global |
| Income statement | `/stable/income-statement` | 87 sample | US | US, UK, CA | Global |
| Balance sheet | `/stable/balance-sheet-statement` | 87 sample | US | US, UK, CA | Global |
| Cash-flow statement | `/stable/cash-flow-statement` | 87 sample | US | US, UK, CA | Global |
| Income statement **TTM** | `/stable/income-statement-ttm` | — | — | — | Global |
| Balance sheet / cash flow **TTM** | `…-ttm` | — | — | — | Global |
| Key metrics | `/stable/key-metrics` | 87 sample | US | US, UK, CA | Global |
| Financial ratios | `/stable/ratios` | 87 sample | US | US, UK, CA | Global |
| Key metrics TTM / Ratios TTM | `/stable/key-metrics-ttm`, `/stable/ratios-ttm` | 87 sample | US | US, UK, CA | Global |
| Enterprise values | `/stable/enterprise-values` | 87 sample | US | US, UK, CA | Global |
| Shares float | `/stable/shares-float` | 87 sample | US | US, UK, CA | Global |
| Market cap | (pricing row "Company Market Cap"; route not checked) | 87 sample | US | US, UK, CA | Global |
| Symbol search / name search | `/stable/search-symbol`, `/stable/search-name` | **Global** | Global | Global | Global |
| Company symbols / statement-symbols list | `/stable/financial-statement-symbol-list` | — | US | US, UK, CA | Global |
| Stock screener | — | — | US exchanges | US, UK, CA exchanges | Global |

Notes on the table:
- **Shares outstanding** has no dedicated route in the comparison. The Income Statement sample response has `weightedAverageShsOut` and `weightedAverageShsOutDil` ([income-statement docs](https://site.financialmodelingprep.com/developer/docs/stable/income-statement)). The FAQ says *"at the moment we provide only the company's current shares float data"* ([FAQ](https://site.financialmodelingprep.com/faqs)).
- **TTM revenue on free** cannot come from `income-statement-ttm`, which is "—" below Ultimate. The alternatives are deriving it from quarterly statements, where free-plan quarterly access is not established (§3), or using the TTM metrics/ratios routes for the 87 symbols.
- The income-statement docs list `period` values `Q1, Q2, Q3, Q4, FY, annual, quarter` and *"Maximum 1000 records per request"*. That is the paid ceiling; see §3 for the Free cap.

---

## 2. Rate limits

### Plans (personal use, read 2026-09-13)

From the pricing cards, the "Compare Individual Use Plans" table and the bandwidth footnote on the [pricing page](https://site.financialmodelingprep.com/pricing-plans):

| Plan | Monthly | Billed annually | API calls | Bandwidth (trailing 30 days) | Timeframe | Historical data range | Coverage (card wording) | Fundamentals (card wording) |
|---|---|---|---|---|---|---|---|---|
| **Basic** | Free | Free | **250 / day** | **500 MB** | **End of Day** | 5 years | — (see §4) | — ("Profile and Reference Data") |
| **Starter** | **$29/mo** | $19/mo | 300 / minute | 20 GB | Real-time | 5 years | "US Coverage" | "Annual Fundamentals and Ratios" |
| **Premium** | **$69/mo** | $49/mo | 750 / minute | 50 GB | Real-time | 30+ years | "UK and Canada Coverage" | "Full Fundamentals and Ratios" |
| **Ultimate** | **$139/mo** | $99/mo | 3,000* / minute | 150 GB | Real-time | 30+ years | "Global Coverage" | "Full Historical Access" |

All four are labelled **Usage: Individual**. Commercial plans, including "Build" (100 GB) and "Enterprise" (1 TB+), sit on a separate tab and were not priced here.

### What counts, and what happens on exceed

- *"One request counts as one API call. A request can include a range of dates, data, and symbols, and when the request is made, it counts as one call"* ([FAQ](https://site.financialmodelingprep.com/faqs)).
- The docs' error-code table lists **401** *"Invalid or missing API key"*, **429** *"Too many requests (rate limit exceeded)"* (resolution: *"Add a time delay in your code or decrease number of threads"*) and **500** ([docs quick start](https://site.financialmodelingprep.com/developer/docs)).
- **Bandwidth** is *"a rolling 30-day limit that updates daily. There is not a specific date that the limit is restored. The data consumed amount will slowly decrease day by day"* ([FAQ](https://site.financialmodelingprep.com/faqs)). Once over it, the stated remedy is to upgrade or buy more.
- The ToS reserves suspension for abuse, e.g. *"using excessive bandwidth"*, and says limits *"are subject to modification at any time, with or without prior notice"* ([ToS §2.9](https://site.financialmodelingprep.com/terms-of-service)).
- **Not documented:** the response body of a 429, whether exceeding the **daily** 250 returns 429 or some other status, when the daily counter resets (UTC midnight? rolling 24h?), and what happens once the 500 MB bandwidth cap is hit.

**Arithmetic, not an FMP statement:** seeding one ticker takes roughly quote + profile + income statement + balance sheet, about 4 calls. On 250/day that is about 60 new tickers a day before caching. Call volume is not what binds Compass on free. The symbol list is.

### Observed: the demo key does not work

Every request made with `apikey=demo` on 2026-09-13 returned **HTTP 401**, on `/stable/quote`, `/stable/income-statement`, `/stable/profile`, `/stable/key-metrics`, `/stable/search-symbol`, `/stable/quote?symbol=BHP.AX`, `/stable/quote?symbol=FPH.NZ` and `/api/v3/quote/AAPL`, with the body:

```json
{ "Error Message": "Invalid API KEY. Feel free to create a Free API Key or visit https://site.financialmodelingprep.com/faqs?search=why-is-my-api-key-invalid for more information." }
```

So **no live response data could be observed without an account**, and the 401 body shape above is the only error body verified.

---

## 3. History depth

| Source | What it says |
|---|---|
| Pricing, "Historical Data Range" row | Basic **5 years**; Starter 5 years; Premium and Ultimate 30+ years ([pricing](https://site.financialmodelingprep.com/pricing-plans)) |
| Income Statement docs, `limit` parameter → Parameter Limitations | **Free: "Maximum 5 Responses per API Call"** (no Starter/Premium row shown) ([income-statement docs](https://site.financialmodelingprep.com/developer/docs/stable/income-statement)) |
| Income Statement docs, `symbol` parameter → Parameter Limitations | Free: the 87-symbol list; Starter: *"Symbol Limited to US Exchanges"*; Premium: *"Symbol Limited to US, UK, and Canada Exchanges"* (same page) |
| General FAQ, "difference between free and paid" | *"up to 250 market data API requests per day, for example you can access up to 5 years of annual statements for US companies"* ([FAQ](https://site.financialmodelingprep.com/faqs)) |
| Income Statement page FAQ | *"…beyond the 4-year income statements available for free users"* ([income-statement docs](https://site.financialmodelingprep.com/developer/docs/stable/income-statement)) |
| General FAQ, "more than 5 years in one query" | *"implement a loop that iterates over 5 year intervals… using the 'from' and 'to' parameters"* (this reads as price history; statements take `limit`/`period`, not `from`/`to`) |

- **Annual 3–5 years: yes on Basic, for the 87 sample symbols only.** Whether the true figure is 4 or 5 years (FMP says both), it clears 3.
- **Quarterly on free: cannot establish.** On the income-statement docs page, only `symbol` and `limit` carry a "Limited Access" badge; `period` does not. But the i18n strings the site ships include *"Period Limited to Annual Reports"*, and the Starter card says *"Annual Fundamentals and Ratios"* while Premium says *"Full Fundamentals and Ratios"*. Read together, those suggest quarterly periods may need Premium. That matters because summing four quarters is the obvious way to get TTM revenue without `income-statement-ttm`.
- **Starter's statement `limit` cap** was not shown in the Parameter Limitations modal. Its stated range is 5 years.

---

## 4. Non-US coverage

### The free symbol list

The `symbol` Parameter Limitations modal on the [Income Statement docs](https://site.financialmodelingprep.com/developer/docs/stable/income-statement) lists the Free plan's symbols in full, as rendered on 2026-09-13:

> AAPL, TSLA, AMZN, MSFT, NVDA, GOOGL, META, NFLX, JPM, V, BAC, PYPL, DIS, T, PFE, COST, INTC, KO, TGT, NKE, SPY, BA, BABA, XOM, WMT, GE, CSCO, VZ, JNJ, CVX, PLTR, SQ, SHOP, SBUX, SOFI, HOOD, RBLX, SNAP, AMD, UBER, FDX, ABBV, ETSY, MRNA, LMT, GM, F, LCID, CCL, DAL, UAL, AAL, TSM, SONY, ET, MRO, COIN, RIVN, RIOT, CPRX, VWO, SPYG, NOK, ROKU, VIAC, ATVI, BIDU, DOCU, ZM, PINS, TLRY, WBA, MGM, NIO, C, GS, WFC, ADBE, PEP, UNH, CARR, HCA, TWTR, BILI, SIRI, FUBO, RKT

Every entry is a US listing (some are US ADRs of foreign companies: BABA, TSM, SONY, NOK, BIDU, NIO, BILI). **No `.AX`, `.NZ`, `.L`, `.TO`, Euronext or Tokyo symbol appears.** Several are delisted or renamed tickers (TWTR, VIAC, ATVI, MRO, SQ). What those return is unknown.

### Coverage by plan

| Exchange | Basic | Starter | Premium | Ultimate |
|---|---|---|---|---|
| US (NYSE/NASDAQ) | 87 sample symbols (fundamentals, quote); US exchanges (profile) | All US | All US | All |
| LSE `.L` | — | — | **Yes** ("US, UK, and Canada") | Yes |
| TSX `.TO` | — | — | **Yes** | Yes |
| Euronext, Tokyo `.T` | — | — | — | "Full Global Coverage" (exchanges not named) |
| **ASX `.AX`** | — | — | — | **Not named. Cannot establish.** |
| **NZX `.NZ`** | — | — | — | **Not named. Cannot establish.** |

Sources: pricing comparison icons and Parameter Limitations, above. FMP's only statement of breadth is *"70,000+ securities across 60+ stock exchanges"* ([FAQ](https://site.financialmodelingprep.com/faqs)). The docs pages for Available Exchanges, Exchange Market Hours, All Exchange Market Hours, Holidays by Exchange and Exchange Variants **do not mention ASX, NZX, Australia or New Zealand** anywhere in their embedded content. FMP's own company pages (`site.financialmodelingprep.com/financial-statements/FPH.NZ`, `…/BHP.AX`, `…/VOD.L`, `…/SHOP.TO`, `…/7203.T`) return HTTP 200 but carry `requiresAuth` and no data when signed out, so they prove nothing.

One more relevant fact: *"The filing date field is sourced from the SEC. Since international stocks don't report to the SEC, this field isn't available for them and is instead mapped to the report date"* ([FAQ](https://site.financialmodelingprep.com/faqs)). US fundamentals are *"sourced from the Securities and Exchange Commission (SEC) open source website Edgar"*, the same source as the ADR's fallback.

---

## 5. Other points that matter for the ADR

### Personal-use licence ([ToS](https://site.financialmodelingprep.com/terms-of-service))

- **§2.2.1 Personal Use:** *"strictly for their own personal, non-business and non-commercial purposes."* The customer *"may not share FMP Services or Data, resell, permit other users access… integrate the Data or Services into any tools or applications accessible by any third parties, or use the Services to host, share, display, or provide content for others."* It also says *"The Customer may not copy or download any content from the Services except with the prior written approval of FMP."*
- "Commercial Use" includes *"Any association with a company, organization, or non-personal domain"*.
- **§2.2.2 Data Display:** without a specific agreement, customers may not showcase FMP data on *"websites, blogs, software products, or applications designed for utilization by multiple individuals, irrespective of whether such usage is complimentary or paid."* The pricing page footer repeats it: *"Displaying or redistributing data sourced from FMP requires a specific Data Display and Licensing Agreement with FMP."*
- **API key:** *"Sharing your account login or API Key with any other individuals or entities… is strictly prohibited."* This fits the ADR's server-side, Secrets Manager design.
- **Caching and storage:** *"Customer affirms that all locations where FMP data is stored have implemented industry-standard physical, technical, and administrative controls to ensure that Data is accessible only by Customer…"* On termination: *"Customer must delete all Data it has received from FMP… including data cached."*
- **Changes:** access to *"specific datasets and endpoints, applicable data query limits and data consumption limits… are subject to change at any time without prior notice."*
- **Attribution:** I found **no attribution requirement** in the ToS or on the pricing page. The binding constraint is the display/redistribution clause, not attribution.

How these clauses apply to a single-user app deployed on the public internet behind auth (Compass) is a reading of the licence. It is not established by the text, and it is flagged in §6.

### Data freshness

- Basic's **Timeframe: "End of Day"**; Starter, Premium and Ultimate: "Real-time" ([pricing](https://site.financialmodelingprep.com/pricing-plans)). The Basic card also lists *"End of Day Historical Data"*. Whether `/stable/quote` on Basic returns the prior close or a delayed intraday price is not stated.
- *"Access to real-time or delayed data may be subject to additional agreements with the relevant exchange, and applicable licensing fees may be required"* ([FAQ](https://site.financialmodelingprep.com/faqs)).
- New statements: *"Typically, we are able to gather financials through an 8-K within 24 hours of an earnings report… this data is not as reliable or as full as the data from their 10-Q"* (same FAQ).

### Other

- No free trial of paid plans: *"We currently do not offer free trials"*. The FAQ mentions a student discount of 30% (same FAQ).

---

## 6. Open questions — confirm with a real free key, or ask FMP

Things FMP's published pages do not settle. Treat them as open, not as facts.

1. **Does a non-sample US ticker on the free key fail, and how?** Status code and body for e.g. `/stable/income-statement?symbol=CRM` (not in the 87). It could be 402, 403, an empty `[]`, or a JSON "Error Message". Compass's "fall back to manual entry" path depends on telling this apart from "ticker unknown".
2. **What does `/stable/quote?symbol=FPH.NZ` / `BHP.AX` return on a free key?** Also whether `search-symbol?query=Fisher` (global on Basic) lists `.NZ`/`.AX` symbols at all. That is the cheapest first-party way to learn whether FMP carries NZX/ASX.
3. **Does any plan cover NZX and ASX fundamentals?** FMP says "Global Coverage" and "60+ exchanges" without naming them. Ask FMP support, or test on a paid key, before treating Ultimate as the answer.
4. **Does Free return `period=quarter`?** And how many annual rows come back with `limit=5`: 4 or 5? The docs contradict each other (§3).
5. **Is the free quote end-of-day or delayed intraday?**
6. **Daily-cap mechanics:** the status and body on call 251, and when the counter resets.
7. **Is a new free account "eligible" for legacy `/api/v3/`?** FMP does not define eligibility. Moot if the build uses `/stable/` only.
8. **Does the personal-use licence (§5) cover a publicly reachable single-user web app that displays FMP data to its owner only?** This is a licence interpretation. Ask FMP if it matters.
9. **Starter's `limit` cap and whether Starter allows quarterly periods.** The docs modal showed only the Free row for `limit`, and the Starter card says "Annual Fundamentals".
10. **Basic-plan behaviour for the delisted or renamed tickers in the sample list** (TWTR, VIAC, ATVI, MRO, SQ). Not material to Compass, but it shows how the list is maintained.

---

## Sources

All fetched 2026-09-13. FMP blocks plain fetchers (HTTP 403), so pages were read with `curl` and a browser user-agent, or rendered in a browser. Content that only exists client-side (the Parameter Limitations modals) was read from the rendered page.

FMP pricing and plans:
- [Pricing Plans (personal use)](https://site.financialmodelingprep.com/pricing-plans): plan cards (Monthly and Annual toggles), "Compare Individual Use Plans" table, per-endpoint icon alt text, bandwidth footnote. The load-bearing page for §1, §2 and §4.

FMP developer documentation:
- [API documentation / Quick start](https://site.financialmodelingprep.com/developer/docs): base URL, authentication, error codes
- [Stable API index](https://site.financialmodelingprep.com/developer/docs/stable): route list
- [Income Statement API](https://site.financialmodelingprep.com/developer/docs/stable/income-statement): parameters, `symbol` and `limit` Parameter Limitations, page FAQ ("4-year")
- Stable docs pages also read: [balance-sheet-statement](https://site.financialmodelingprep.com/developer/docs/stable/balance-sheet-statement), [cashflow-statement](https://site.financialmodelingprep.com/developer/docs/stable/cashflow-statement), [key-metrics](https://site.financialmodelingprep.com/developer/docs/stable/key-metrics), [key-metrics-ttm](https://site.financialmodelingprep.com/developer/docs/stable/key-metrics-ttm), [metrics-ratios](https://site.financialmodelingprep.com/developer/docs/stable/metrics-ratios), [quote](https://site.financialmodelingprep.com/developer/docs/stable/quote), [profile-symbol](https://site.financialmodelingprep.com/developer/docs/stable/profile-symbol), [shares-float](https://site.financialmodelingprep.com/developer/docs/stable/shares-float), [enterprise-values](https://site.financialmodelingprep.com/developer/docs/stable/enterprise-values), [search-symbol](https://site.financialmodelingprep.com/developer/docs/stable/search-symbol), [available-exchanges](https://site.financialmodelingprep.com/developer/docs/stable/available-exchanges), [exchange-market-hours](https://site.financialmodelingprep.com/developer/docs/stable/exchange-market-hours), [all-exchange-market-hours](https://site.financialmodelingprep.com/developer/docs/stable/all-exchange-market-hours), [holidays-by-exchange](https://site.financialmodelingprep.com/developer/docs/stable/holidays-by-exchange), [search-exchange-variants](https://site.financialmodelingprep.com/developer/docs/stable/search-exchange-variants)
- [Changelog](https://site.financialmodelingprep.com/developer/docs/changelog): 2025-01-10 stable beta; 2025-08-27 legacy routes auth-gated
- [Legacy (V1) endpoints index](https://site.financialmodelingprep.com/developer/docs/legacy-endpoints)

FMP policy:
- [FAQs](https://site.financialmodelingprep.com/faqs): free vs paid, symbols/exchanges, what counts as a call, bandwidth reset, filing-date note, invalid-key help
- [Terms of Service](https://site.financialmodelingprep.com/terms-of-service): §2.2.1 Personal Use, §2.2.2 Data Display, key sharing, §2.9 Data Limits, data deletion on termination

First-party API observations (demo key, 2026-09-13):
- `https://financialmodelingprep.com/stable/{quote,profile,income-statement,balance-sheet-statement,cash-flow-statement,key-metrics,ratios-ttm,income-statement-ttm,enterprise-values,shares-float,search-symbol}?…&apikey=demo` and `https://financialmodelingprep.com/api/v3/quote/AAPL?apikey=demo`: all HTTP 401 "Invalid API KEY" (§2)

Not used as sources: third-party blogs, Reddit, or other secondary write-ups about FMP.
