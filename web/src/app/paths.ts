export const calculatorPath = (ticker: string) => `/projections/${encodeURIComponent(ticker)}`;

/** The landing page's ticker box: the page focuses it on this fragment, and links aim at the path. */
export const TICKER_BOX_HASH = "#ticker";
export const TICKER_BOX_PATH = `/${TICKER_BOX_HASH}`;
