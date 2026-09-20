// The live-preview projection calculator: the glossary's core formula (docs/glossary.md) in
// double, with no I/O. Its twin is `ProjectionCalculator` in the API, which is authoritative on
// save; both assert every case in spec/projection-vectors.json, so the two cannot drift
// (ADR 0004, amended 2026-09-17). Decimals and raw values in, as on the wire: percents
// and millions belong to the UI (ADR 0011).

export type ExitMultiple = { kind: "pe"; value: number } | { kind: "ps"; value: number };

export type BaseFacts = {
  currentPrice: number;
  ttmRevenue: number;
  dilutedShares: number;
};

/** One scenario's Assumption Set (docs/glossary.md). */
export type AssumptionSet = {
  growthRate: number;
  /** Ignored on the P/S path. */
  futureNetMargin: number;
  exitMultiple: ExitMultiple;
  dilutionRate: number;
  /** Whole years, >= 1. */
  horizon: number;
};

export type ProjectionOutputs = { futurePrice: number; x: number; cagr: number };

export type RejectionReason =
  | "notFinite"
  | "horizonNotPositive"
  /** The API takes whole years only, so it has no twin for this one. */
  | "horizonNotWhole"
  | "priceNotPositive"
  | "sharesNotPositive"
  | "revenueNegative"
  | "growthBelowMinusOne"
  | "dilutionNotAboveMinusOne"
  | "exitMultipleNegative"
  | "marginNegativeOnPe";

export type ProjectionResult =
  | { status: "ok"; outputs: ProjectionOutputs }
  /** A field is still empty: nothing to show yet, and nothing wrong either. */
  | { status: "incomplete" }
  | { status: "rejected"; reason: RejectionReason };

/** What a form holds mid-keystroke: any field may be absent, and a half-typed number is NaN. */
type Draft<T> = { readonly [K in keyof T]?: T[K] | null };
export type DraftBaseFacts = Draft<BaseFacts>;
export type DraftAssumptionSet = Draft<Omit<AssumptionSet, "exitMultiple">> & {
  readonly exitMultiple?: { readonly kind?: ExitMultiple["kind"] | null; readonly value?: number | null } | null;
};

const rejected = (reason: RejectionReason): ProjectionResult => ({ status: "rejected", reason });

const present = (v: number | null | undefined): v is number => v != null && !Number.isNaN(v);

/**
 * Future price, X and CAGR for one scenario. Never throws and never answers NaN or infinity: a
 * value that is present and wrong is rejected with its reason, even while other fields are still
 * empty, and only a set with nothing wrong and something missing is `incomplete`.
 */
export function calculateProjection(facts: DraftBaseFacts, assumptions: DraftAssumptionSet): ProjectionResult {
  const { currentPrice, ttmRevenue, dilutedShares } = facts;
  const { growthRate, futureNetMargin, dilutionRate, horizon } = assumptions;
  const kind = assumptions.exitMultiple?.kind;
  const multiple = assumptions.exitMultiple?.value;
  // The margin plays no part on the P/S path, so there it is neither required nor checked.
  const margin = kind === "ps" ? 1 : futureNetMargin;
  // A multiple that does not yet say what it multiplies is a field still being filled in.
  const kindChosen = kind === "pe" || kind === "ps";

  const values = [currentPrice, ttmRevenue, dilutedShares, growthRate, margin, multiple, dilutionRate, horizon];
  if (values.some((v) => present(v) && !Number.isFinite(v))) return rejected("notFinite");
  if (present(horizon) && horizon <= 0) return rejected("horizonNotPositive");
  if (present(horizon) && !Number.isInteger(horizon)) return rejected("horizonNotWhole");
  if (present(currentPrice) && currentPrice <= 0) return rejected("priceNotPositive");
  if (present(dilutedShares) && dilutedShares <= 0) return rejected("sharesNotPositive");
  if (present(ttmRevenue) && ttmRevenue < 0) return rejected("revenueNegative");
  // Below -100% a company has lost more than all of its revenue, and the sign of the compounded
  // figure would flip with the horizon's parity.
  if (present(growthRate) && growthRate < -1) return rejected("growthBelowMinusOne");
  // At -100% every share is bought back and the per-share price divides by zero.
  if (present(dilutionRate) && dilutionRate <= -1) return rejected("dilutionNotAboveMinusOne");
  if (present(multiple) && multiple < 0) return rejected("exitMultipleNegative");
  // A loss at the horizon has no P/E price; the glossary's answer is to use P/S instead.
  if (kind === "pe" && present(margin) && margin < 0) return rejected("marginNegativeOnPe");

  if (
    !kindChosen ||
    !present(currentPrice) ||
    !present(ttmRevenue) ||
    !present(dilutedShares) ||
    !present(growthRate) ||
    !present(margin) ||
    !present(multiple) ||
    !present(dilutionRate) ||
    !present(horizon)
  ) {
    return { status: "incomplete" };
  }

  const futureRevenue = ttmRevenue * (1 + growthRate) ** horizon;
  const futureShares = dilutedShares * (1 + dilutionRate) ** horizon;
  // `margin` is 1 on the P/S path, which makes this the revenue multiple the glossary states.
  const futureMarketCap = futureRevenue * margin * multiple;
  const futurePrice = futureMarketCap / futureShares;
  const x = futurePrice / currentPrice;
  const cagr = x ** (1 / horizon) - 1;

  // Every input was in range, so anything non-finite here is a double overflowing.
  if (![futurePrice, x, cagr].every(Number.isFinite)) return rejected("notFinite");
  return { status: "ok", outputs: { futurePrice, x, cagr } };
}
