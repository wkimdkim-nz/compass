/** What every figure on the calculator means, how to choose it, and where to look it up. */
export type GuideEntry = {
  /** The entry's address on the guide, `/guide#<id>`. Links point at it, so it never changes. */
  id: string;
  name: string;
  group: "fact" | "assumption" | "output";
  /** One sentence. */
  what: string;
  /** What to base the guess on. */
  anchor?: string;
  sanity?: string;
  /** Home pages only: a ticker-specific address rots, and the guide has no ticker to build one from. */
  sources: { label: string; href: string }[];
  /** Where the figure sits in a 10-K or 10-Q. */
  filing?: string;
};

const EDGAR = { label: "SEC EDGAR full-text search", href: "https://www.sec.gov/edgar/search/" };
const STOCK_ANALYSIS = { label: "stockanalysis.com", href: "https://stockanalysis.com/" };
const MACROTRENDS = { label: "Macrotrends", href: "https://www.macrotrends.net/" };
const YAHOO = { label: "Yahoo Finance", href: "https://finance.yahoo.com/" };

export const GUIDE_ENTRIES: readonly GuideEntry[] = [
  {
    id: "share-price",
    name: "Share price",
    group: "fact",
    what: "What one share costs today: a fact, not a guess, and the figure the X divides by.",
    sources: [YAHOO],
    filing: "Not in a filing: it is a market quote. Compass takes it from Finnhub, and any broker shows it.",
  },
  {
    id: "revenue-ttm",
    name: "Revenue (TTM)",
    group: "fact",
    what: "Sales over the trailing twelve months: the starting point that revenue growth compounds from.",
    sources: [STOCK_ANALYSIS, MACROTRENDS, EDGAR],
    filing:
      "Income statement, “Total net sales” or “Revenue”. Add the last four quarters from the 10-Qs, or take the 10-K’s figure when it is the latest; for the fourth quarter, take the year less its first nine months. Compass works it out from the filings on EDGAR.",
  },
  {
    id: "diluted-shares",
    name: "Shares outstanding (diluted)",
    group: "fact",
    what: "The diluted weighted-average share count for the latest period, an average over the period rather than a count on one day, which the dilution rate grows into the future share count.",
    sources: [MACROTRENDS, YAHOO, EDGAR],
    filing:
      "Income statement, “Weighted-average shares outstanding, diluted”, beside earnings per share. The 10-K cover gives a count on one day instead. Compass takes it from the filings on EDGAR.",
  },
  {
    id: "net-margin",
    name: "Net margin",
    group: "fact",
    what: "Net income ÷ revenue over the trailing twelve months, printed rather than typed as the reference point for choosing a future margin.",
    sources: [STOCK_ANALYSIS, MACROTRENDS],
    filing: "Divide “Net income” by “Total net sales” or “Revenue” on the income statement, both over the same twelve months.",
  },
  {
    id: "horizon",
    name: "Horizon",
    group: "assumption",
    what: "How many whole years ahead the projection looks.",
    anchor: "The holding period you are weighing, commonly 5 or 10 years.",
    sanity: "Longer means more compounding, and guesses that are less reliable.",
    sources: [],
  },
  {
    id: "revenue-growth",
    name: "Revenue growth",
    group: "assumption",
    what: "How fast revenue grows each year, on average, over the horizon.",
    anchor:
      "The company’s own revenue CAGR over the last 3–5 years of filed history, which Compass pre-fills from, cross-checked against analyst estimates and the size of its market (TAM).",
    sanity:
      "Above 40 % a year is rarely sustained past a few years; the bigger the revenue base, the harder high growth gets.",
    sources: [STOCK_ANALYSIS, MACROTRENDS],
    filing: "Compare revenue on the income statements of the last few 10-Ks.",
  },
  {
    id: "future-net-margin",
    name: "Future net margin",
    group: "assumption",
    what: "The share of revenue left as net income at the end of the horizon; a P/S exit multiple does not use it.",
    anchor: "Today’s net margin, and where mature peers in the same sector land.",
    sanity:
      "Software matures high (20–40 %), retail and hardware low (single digits). Rising from a low margin today assumes operating leverage: reasonable for a company that is scaling, optimistic for others.",
    sources: [STOCK_ANALYSIS, MACROTRENDS],
  },
  {
    id: "exit-multiple",
    name: "Exit multiple (P/E or P/S)",
    group: "assumption",
    what: "The valuation multiple applied at the end of the horizon: price to earnings (P/E) or price to sales (P/S).",
    anchor:
      "The sector’s typical multiple and the company’s own current one, which Compass pre-fills, usually assuming it compresses as growth slows.",
    sanity:
      "Use P/S while the company is not reliably profitable, and P/E once its margins are stable. This is the single biggest swing factor: small changes here move the X a lot.",
    sources: [STOCK_ANALYSIS, YAHOO],
    filing:
      "Not in a filing: divide today’s share price by diluted earnings per share (P/E), or market cap by revenue (P/S).",
  },
  {
    id: "share-dilution",
    name: "Share dilution",
    group: "assumption",
    what: "How fast the share count grows each year; a negative rate means buybacks.",
    anchor:
      "The historical share-count CAGR, (shares now ÷ shares then)^(1 ÷ years) − 1, from filed history; Compass computes it as the default.",
    sanity:
      "Buyback companies below 0; stable ones 0–1 %; growth companies 2–5 %, mostly stock-based compensation; frequent capital-raisers 5–10 % or more. Ignoring it overstates per-share returns.",
    sources: [MACROTRENDS, EDGAR],
    filing:
      "The 10-K’s statement of stockholders’ equity, or compare the diluted share counts across several 10-Ks. Macrotrends charts it as “Shares Outstanding”.",
  },
  {
    id: "future-price",
    name: "Future share price",
    group: "output",
    what: "Future market cap ÷ future share count: what one share would be worth at the end of the horizon if every guess holds.",
    sources: [],
  },
  {
    id: "the-x",
    name: "The X (return multiple)",
    group: "output",
    what: "Future share price ÷ today’s price: a 2X doubles your money, if every guess holds.",
    sanity: "Precise-looking, but built on guesses: read the range from Bear to Bull, not one figure.",
    sources: [],
  },
  {
    id: "cagr",
    name: "CAGR",
    group: "output",
    what: "The X spread evenly over the horizon as a yearly return: X^(1 ÷ years) − 1.",
    sources: [],
  },
];

export function guideEntry(id: string): GuideEntry {
  const entry = GUIDE_ENTRIES.find((candidate) => candidate.id === id);
  if (!entry) throw new Error(`No guide entry has the id "${id}".`);
  return entry;
}
