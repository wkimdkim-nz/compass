// PROTOTYPE — throwaway (branch prototype/phase2-ui). Content condensed from docs/glossary.md's input guide.

export type Entry = {
  id: string;
  name: string;
  kind: "fact" | "assumption" | "output";
  what: string;
  anchor?: string;
  where: { label: string; href: string }[];
  filing?: string;
  sanity?: string;
  specimen: string;
};

const EDGAR = { label: "SEC EDGAR", href: "https://www.sec.gov/edgar/search/" };
const SA = { label: "stockanalysis.com", href: "https://stockanalysis.com" };
const MT = { label: "Macrotrends", href: "https://www.macrotrends.net" };
const YF = { label: "Yahoo Finance", href: "https://finance.yahoo.com" };

export const ENTRIES: Entry[] = [
  {
    id: "price",
    name: "Share price",
    kind: "fact",
    what: "What one share costs today. The denominator of the X.",
    where: [YF, { label: "your broker", href: "#" }],
    filing: "Not in a filing — it is a market quote. Compass gets it from Finnhub.",
    specimen: "$228.00",
  },
  {
    id: "revenue",
    name: "Revenue, trailing twelve months",
    kind: "fact",
    what: "Sales over the last four reported quarters. The starting point growth compounds from.",
    where: [SA, MT, EDGAR],
    filing: "Add the last four quarters' “Total net sales” from the 10-Qs, or take the 10-K's figure if it is fresh.",
    specimen: "$391,035m",
  },
  {
    id: "shares",
    name: "Shares outstanding, diluted",
    kind: "fact",
    what: "The average share count including options and convertibles — what each slice of profit is divided by.",
    where: [MT, YF, EDGAR],
    filing: "Income statement, “Weighted-average shares — diluted”, near earnings per share.",
    specimen: "15,408m",
  },
  {
    id: "net-margin",
    name: "Net margin",
    kind: "fact",
    what: "Net income ÷ revenue today. Printed, not typed: it is the reference for your future margin.",
    where: [SA, MT],
    filing: "Divide “Net income” by “Total net sales” on the income statement.",
    specimen: "24.0 %",
  },
  {
    id: "horizon",
    name: "Horizon",
    kind: "assumption",
    what: "How many years ahead you are projecting.",
    anchor: "The holding period you are weighing — commonly 5 or 10 years.",
    where: [],
    sanity: "Longer means more compounding and less reliability.",
    specimen: "5 years",
  },
  {
    id: "growth",
    name: "Revenue growth",
    kind: "assumption",
    what: "How fast sales grow each year, on average, over the horizon.",
    anchor: "The company's own trailing 3–5 year revenue CAGR (Compass seeds Base with it), checked against analyst estimates.",
    where: [SA, MT],
    sanity: "Above 40% a year is rarely sustained; the bigger the base, the harder high growth gets.",
    specimen: "6.0 % / yr",
  },
  {
    id: "margin",
    name: "Future net margin",
    kind: "assumption",
    what: "The share of revenue left as profit at the end of the horizon.",
    anchor: "Today's margin, and where mature peers in the same sector land.",
    where: [SA],
    sanity: "Software matures high (20–40%); retail and hardware low (single digits).",
    specimen: "25.0 %",
  },
  {
    id: "multiple",
    name: "Exit multiple (P/E or P/S)",
    kind: "assumption",
    what: "What the market pays per dollar of earnings (P/E) or sales (P/S) at the end of the horizon.",
    anchor: "The sector's typical multiple and the company's own, usually assuming it compresses as growth slows.",
    where: [SA, YF],
    sanity: "Use P/S until the company is reliably profitable. The biggest swing factor of all.",
    specimen: "28 × P/E",
  },
  {
    id: "dilution",
    name: "Share dilution",
    kind: "assumption",
    what: "How fast the share count grows each year. Negative means buybacks.",
    anchor: "The historical share-count CAGR (Compass seeds it from filings).",
    where: [MT, EDGAR],
    filing: "Statement of stockholders' equity, or compare diluted share counts across 10-Ks.",
    sanity: "Buyback companies < 0; stable 0–1%; growth companies 2–5%; capital-raisers 5–10%+.",
    specimen: "(2.5) % / yr",
  },
  {
    id: "x",
    name: "The X",
    kind: "output",
    what: "Future price ÷ today's price. A 2X doubles your money — if every guess above holds.",
    where: [],
    specimen: "1.2 X",
  },
  {
    id: "cagr",
    name: "CAGR",
    kind: "output",
    what: "The X spread evenly over the horizon as a yearly return.",
    where: [],
    specimen: "3 % / yr",
  },
];

export const byId = (id: string) => ENTRIES.find((e) => e.id === id)!;
