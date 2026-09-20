import { describe, expect, it } from "vitest";
import { KINDS, vectors, within } from "../test/projectionVectors";

// Shape guard for the shared vectors file (docs/glossary.md, "The core formula"). The
// calculators (ticket #70) assert every case against the formula; this test only proves the
// file is well-formed and that each case's three outputs agree with each other, so a
// transcription slip is caught here rather than blamed on a calculator.
describe("spec/projection-vectors.json", () => {
  it("names the formula and states a tolerance", () => {
    expect(vectors.formula).toContain("docs/glossary.md");
    expect(vectors.tolerance.relative).toBeGreaterThan(0);
    expect(vectors.tolerance.relative).toBeLessThanOrEqual(1e-6);
  });

  it("covers every path and edge the roadmap lists", () => {
    const ids = vectors.cases.map((c) => c.id);
    expect(new Set(ids).size).toBe(ids.length);
    const kinds = new Set(vectors.cases.map((c) => c.inputs.exitMultiple.kind));
    expect(kinds).toEqual(new Set(KINDS));
    expect(vectors.cases.some((c) => c.inputs.growthRate === 0)).toBe(true);
    expect(vectors.cases.some((c) => c.inputs.dilutionRate < 0)).toBe(true);
    expect(vectors.cases.some((c) => c.inputs.horizon === 1)).toBe(true);
    expect(
      vectors.cases.some((c) => c.inputs.exitMultiple.kind === "ps" && c.inputs.futureNetMargin === 0),
    ).toBe(true);
    expect(vectors.cases.some((c) => c.inputs.horizon === 5)).toBe(true);
  });

  it.each(vectors.cases.map((c) => [c.id, c] as const))("%s is complete and self-consistent", (_, c) => {
    for (const v of [
      c.inputs.currentPrice,
      c.inputs.ttmRevenue,
      c.inputs.dilutedShares,
      c.inputs.growthRate,
      c.inputs.futureNetMargin,
      c.inputs.exitMultiple.value,
      c.inputs.dilutionRate,
      c.inputs.horizon,
      c.expected.futurePrice,
      c.expected.x,
      c.expected.cagr,
    ]) {
      expect(typeof v).toBe("number");
      expect(Number.isFinite(v)).toBe(true);
    }
    expect(c.working.length).toBeGreaterThan(0);
    // X is the price ratio and CAGR is its horizon-th root: the three outputs must agree.
    expect(within(c.expected.futurePrice / c.inputs.currentPrice, c.expected.x)).toBe(true);
    expect(within(c.expected.x ** (1 / c.inputs.horizon) - 1, c.expected.cagr)).toBe(true);
  });
});
