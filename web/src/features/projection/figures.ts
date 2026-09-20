// The UI's side of the units: percents (8, not 0.08, to one decimal: ADR 0011) and millions on the
// page, decimals and raw values on the wire and in the calculator (docs/roadmap.md, Phase 1).
// Negatives print in parentheses, the way a statement prints them (docs/design/phase1-look.md).

import type { ProjectionOutputs } from "../../projection/calculator";

/** How a field's figure relates to its wire value. */
export type Unit = "percent" | "millions" | "multiple" | "plain";

const SCALE: Record<Unit, number> = { percent: 0.01, millions: 1_000_000, multiple: 1, plain: 1 };

/** Digits with an optional fraction; commas only where thousands separators go, so 1,2 is mistyped, not 12. */
const FIGURE = /^(\d{1,3}(,\d{3})+\.?\d*|\d+\.?\d*|\.\d+)$/;

/**
 * What a field holds, as a number in the UI's units: null when empty, NaN when mistyped.
 * Accepts thousands separators, and parentheses or a minus sign for a negative, never both.
 */
export function parseFigure(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed === "") return null;
  const bracketed = /^\((.*)\)$/.exec(trimmed);
  const minus = !bracketed && trimmed.startsWith("-");
  const digits = (bracketed ? bracketed[1] : minus ? trimmed.slice(1) : trimmed).trim();
  if (!FIGURE.test(digits)) return NaN;
  const value = Number(digits.replace(/,/g, ""));
  return bracketed || minus ? -value : value;
}

/** A number printed with thousands separators and at most `decimals` places, negatives in parentheses. */
export function formatFigure(value: number, decimals: number): string {
  const rounded = Number(Math.abs(value).toFixed(decimals));
  const text = rounded.toLocaleString("en-US", { maximumFractionDigits: decimals });
  return value < 0 && rounded !== 0 ? `(${text})` : text;
}

/** Small figures keep their cents; large ones are printed whole. */
const decimalsFor = (value: number) => (Math.abs(value) >= 100 ? 0 : 2);

/**
 * A wire value as the text its field shows. Empty for a figure that is not there. A percent is a
 * percent in the roadmap's sense, 8 rather than 0.08, and keeps one decimal (ADR 0011): a seeded 1.3% of buybacks shown as 1 would save as a different assumption.
 */
export function toUi(wire: number | null | undefined, unit: Unit): string {
  if (wire == null || !Number.isFinite(wire)) return "";
  const value = wire / SCALE[unit];
  switch (unit) {
    case "percent":
    case "multiple":
      return formatFigure(value, 1);
    case "millions":
      return formatFigure(value, decimalsFor(value));
    case "plain":
      return formatFigure(value, 2);
  }
}

/** A parsed field value as the wire carries it. The rounding drops the binary tail a scale leaves (7.7 × 0.01). */
export function toWire(value: number, unit: Unit): number {
  return Number((value * SCALE[unit]).toPrecision(12));
}

export function formatOutputs(outputs: ProjectionOutputs): { x: string; cagr: string; futurePrice: string } {
  return {
    x: formatFigure(outputs.x, 1),
    cagr: `${formatFigure(outputs.cagr * 100, 1)} %`,
    futurePrice: formatFigure(outputs.futurePrice, decimalsFor(outputs.futurePrice)),
  };
}
