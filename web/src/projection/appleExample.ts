import type { AssumptionSet, BaseFacts } from "./calculator";

/**
 * The one Apple example every page that shows a worked projection runs through the calculator, so
 * no two can disagree. Illustrative figures, not a prediction, in wire units like everything the
 * calculator takes.
 */
export const APPLE_EXAMPLE = {
  facts: {
    currentPrice: 228,
    ttmRevenue: 391_035_000_000,
    dilutedShares: 15_408_000_000,
  },
  scenarios: {
    bear: scenario(0.03, 22),
    base: scenario(0.06, 28),
    bull: scenario(0.09, 34),
  },
} as const satisfies { facts: BaseFacts; scenarios: Record<"bear" | "base" | "bull", AssumptionSet> };

function scenario(growthRate: number, exitPe: number): AssumptionSet {
  return {
    growthRate,
    futureNetMargin: 0.25,
    exitMultiple: { kind: "pe", value: exitPe },
    dilutionRate: -0.025,
    horizon: 5,
  };
}
