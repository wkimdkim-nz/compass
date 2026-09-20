import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

// The shared vectors file (docs/glossary.md, "The core formula"), read from the repo root rather
// than copied, so a new case reaches every suite that imports this: web/src/test -> ../../../spec.
export const KINDS = ["pe", "ps"] as const;
export type Kind = (typeof KINDS)[number];
export type VectorCase = {
  id: string;
  inputs: {
    currentPrice: number;
    ttmRevenue: number;
    dilutedShares: number;
    growthRate: number;
    futureNetMargin: number;
    exitMultiple: { kind: Kind; value: number };
    dilutionRate: number;
    horizon: number;
  };
  expected: { futurePrice: number; x: number; cagr: number };
  working: string[];
};
export type VectorsFile = {
  formula: string;
  tolerance: { relative: number; rule: string };
  cases: VectorCase[];
};

export const vectors = JSON.parse(
  readFileSync(
    resolve(fileURLToPath(import.meta.url), "..", "..", "..", "..", "spec", "projection-vectors.json"),
    "utf8",
  ),
) as VectorsFile;

/** The tolerance rule in the file's header. */
export const within = (actual: number, expected: number) =>
  Math.abs(actual - expected) <= vectors.tolerance.relative * Math.max(1, Math.abs(expected));
