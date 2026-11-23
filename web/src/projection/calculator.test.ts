import { describe, expect, it } from "vitest";
import { vectors, within } from "../test/projectionVectors";
import { calculateProjection } from "./calculator";
import type { AssumptionSet, BaseFacts, ExitMultiple, ProjectionResult } from "./calculator";

// The live-preview calculator against the shared vectors: a new case in the file
// fails here and in the .NET suite until each side is fixed. `vectors.test.ts` guards the file's
// own shape; this one only trusts it.
const facts: BaseFacts = { currentPrice: 40, ttmRevenue: 1_000_000_000, dilutedShares: 100_000_000 };
const assumptions: AssumptionSet = {
  growthRate: 0.1,
  futureNetMargin: 0.2,
  exitMultiple: { kind: "pe", value: 20 },
  dilutionRate: 0.02,
  horizon: 5,
};

const outputsOf = (result: ProjectionResult) => {
  if (result.status !== "ok") throw new Error(`expected outputs, got ${JSON.stringify(result)}`);
  return result.outputs;
};

describe("calculateProjection", () => {
  it("loads at least one vector", () => {
    expect(vectors.cases.length).toBeGreaterThan(0);
  });

  it.each(vectors.cases.map((c) => [c.id, c] as const))("matches vector %s within the file's tolerance", (_, c) => {
    const outputs = outputsOf(calculateProjection(c.inputs, c.inputs));

    for (const key of ["futurePrice", "x", "cagr"] as const) {
      expect(within(outputs[key], c.expected[key]), `${key}: ${outputs[key]}`).toBe(true);
    }
  });

  it.each([
    ["a horizon of zero", {}, { horizon: 0 }, "horizonNotPositive"],
    ["a negative horizon", {}, { horizon: -3 }, "horizonNotPositive"],
    ["a fractional horizon", {}, { horizon: 2.5 }, "horizonNotWhole"],
    ["a price of zero", { currentPrice: 0 }, {}, "priceNotPositive"],
    ["a negative price", { currentPrice: -1 }, {}, "priceNotPositive"],
    ["a share count of zero", { dilutedShares: 0 }, {}, "sharesNotPositive"],
    ["a negative share count", { dilutedShares: -5 }, {}, "sharesNotPositive"],
    ["negative revenue", { ttmRevenue: -1 }, {}, "revenueNegative"],
    ["growth below a total loss", {}, { growthRate: -1.5 }, "growthBelowMinusOne"],
    ["dilution that erases every share", {}, { dilutionRate: -1 }, "dilutionNotAboveMinusOne"],
    ["a negative exit multiple", {}, { exitMultiple: { kind: "ps", value: -2 } as ExitMultiple }, "exitMultipleNegative"],
    ["a negative margin on P/E", {}, { futureNetMargin: -0.1 }, "marginNegativeOnPe"],
    ["an infinite input", { ttmRevenue: Infinity }, {}, "notFinite"],
    ["outputs too large for a double", { ttmRevenue: 1e300 }, { growthRate: 1e10 }, "notFinite"],
  ] as const)("rejects %s", (_, factsPatch, assumptionsPatch, reason) => {
    const result = calculateProjection({ ...facts, ...factsPatch }, { ...assumptions, ...assumptionsPatch });

    expect(result).toEqual({ status: "rejected", reason });
  });

  it("ignores a negative margin on the P/S path", () => {
    const result = calculateProjection(facts, {
      ...assumptions,
      futureNetMargin: -0.4,
      exitMultiple: { kind: "ps", value: 4 },
    });

    expect(result.status).toBe("ok");
  });

  it.each([
    ["zero margin on P/E", {}, { futureNetMargin: 0 }],
    ["zero revenue", { ttmRevenue: 0 }, {}],
    ["zero revenue on P/S", { ttmRevenue: 0 }, { exitMultiple: { kind: "ps", value: 6 } as ExitMultiple }],
    ["a total loss of revenue", {}, { growthRate: -1 }],
  ] as const)("answers %s with a worthless share, not NaN", (_, factsPatch, assumptionsPatch) => {
    const outputs = outputsOf(
      calculateProjection({ ...facts, ...factsPatch }, { ...assumptions, ...assumptionsPatch }),
    );

    expect(outputs).toEqual({ futurePrice: 0, x: 0, cagr: -1 });
  });

  it("never lets NaN or infinity out, whatever the combination", () => {
    const samples = [-1e308, -2, -1, -0.5, 0, 0.5, 1, 7, 1e308, Infinity, -Infinity, NaN];
    for (const kind of ["pe", "ps"] as const) {
      for (const a of samples) {
        for (const b of samples) {
          const results = [
            calculateProjection({ ...facts, currentPrice: a, ttmRevenue: b }, { ...assumptions, exitMultiple: { kind, value: a } }),
            calculateProjection({ ...facts, dilutedShares: a }, { ...assumptions, growthRate: b, exitMultiple: { kind, value: 3 } }),
            calculateProjection(facts, { ...assumptions, dilutionRate: a, futureNetMargin: b, exitMultiple: { kind, value: 3 } }),
            calculateProjection(facts, { ...assumptions, horizon: a, growthRate: b, exitMultiple: { kind, value: 3 } }),
          ];
          for (const result of results) {
            if (result.status !== "ok") continue;
            for (const v of Object.values(result.outputs)) expect(Number.isFinite(v)).toBe(true);
          }
        }
      }
    }
  });

  // The UI calls this on every keystroke, with whatever the form holds at that moment.
  it.each([
    ["nothing at all", {}, {}],
    ["a missing price", { ...facts, currentPrice: undefined }, assumptions],
    ["a cleared field", facts, { ...assumptions, growthRate: null }],
    ["a half-typed number", facts, { ...assumptions, dilutionRate: NaN }],
    ["no exit multiple", facts, { ...assumptions, exitMultiple: undefined }],
    ["an exit multiple with a kind but no value", facts, { ...assumptions, exitMultiple: { kind: "pe" } }],
    ["an exit multiple with a value but no kind", facts, { ...assumptions, exitMultiple: { value: 20 } }],
    ["a loss with no kind chosen yet", facts, { ...assumptions, futureNetMargin: -0.1, exitMultiple: { value: 20 } }],
    ["a missing margin on P/E", facts, { ...assumptions, futureNetMargin: undefined }],
  ] as const)("answers %s with no outputs rather than a throw", (_, f, a) => {
    expect(calculateProjection(f, a)).toEqual({ status: "incomplete" });
  });

  it("does not need a margin on the P/S path", () => {
    const result = calculateProjection(facts, {
      ...assumptions,
      futureNetMargin: undefined,
      exitMultiple: { kind: "ps", value: 4 },
    });

    expect(result.status).toBe("ok");
  });

  it("reports a rejection ahead of the fields still missing", () => {
    // A zero horizon is wrong now; waiting for the rest of the form would hide that.
    expect(calculateProjection({}, { horizon: 0 })).toEqual({ status: "rejected", reason: "horizonNotPositive" });
  });
});
