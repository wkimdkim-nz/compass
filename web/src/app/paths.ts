export const PROJECTIONS_PATH = "/projections";
export const CALCULATOR_ROUTE = `${PROJECTIONS_PATH}/:ticker`;
export const calculatorPath = (ticker: string) => `${PROJECTIONS_PATH}/${encodeURIComponent(ticker)}`;

/** The landing page's ticker box: the page focuses it on this fragment, and links aim at the path. */
export const TICKER_BOX_HASH = "#ticker";
export const TICKER_BOX_PATH = `/${TICKER_BOX_HASH}`;
