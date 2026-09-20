import { describe, expect, it } from "vitest";
import { formatFigure, formatOutputs, parseFigure, toUi, toWire } from "./figures";

describe("reading a typed figure", () => {
  it("reads thousands separators, parentheses as a negative, and a minus sign", () => {
    expect(parseFigure("391,035")).toBe(391035);
    expect(parseFigure("(2)")).toBe(-2);
    expect(parseFigure("-2.5")).toBe(-2.5);
    expect(parseFigure(" 8 ")).toBe(8);
  });

  it("reads an empty field as nothing and a mistyped one as not a number", () => {
    expect(parseFigure("")).toBeNull();
    expect(parseFigure("   ")).toBeNull();
    expect(parseFigure("abc")).toBeNaN();
    expect(parseFigure("1.2.3")).toBeNaN();
    expect(parseFigure("12abc")).toBeNaN();
    expect(parseFigure("(")).toBeNaN();
  });
});

describe("the UI's units", () => {
  it("shows a decimal rate as a whole-number percent and sends it back as the decimal", () => {
    expect(toUi(0.08, "percent")).toBe("8");
    expect(toUi(-0.02, "percent")).toBe("(2)");
    expect(toWire(8, "percent")).toBe(0.08);
    expect(toWire(-2, "percent")).toBe(-0.02);
    // 7.7 / 100 must not pick up a binary tail on the way to the wire.
    expect(toWire(7.7, "percent")).toBe(0.077);
  });

  it("shows a raw value in millions and sends it back raw", () => {
    expect(toUi(391_035_000_000, "millions")).toBe("391,035");
    expect(toWire(391_035, "millions")).toBe(391_035_000_000);
    expect(toUi(12_340_000, "millions")).toBe("12.34");
  });

  it("leaves a price and a multiple in their own units", () => {
    expect(toUi(234.07, "plain")).toBe("234.07");
    expect(toUi(28.04, "multiple")).toBe("28");
    expect(toWire(28, "multiple")).toBe(28);
  });

  it("shows nothing for a figure that is not there", () => {
    expect(toUi(null, "plain")).toBe("");
    expect(toUi(undefined, "percent")).toBe("");
  });
});

describe("printing a figure", () => {
  it("prints negatives in parentheses, never with a minus sign", () => {
    expect(formatFigure(-6, 0)).toBe("(6)");
    expect(formatFigure(1234.5, 0)).toBe("1,235");
    // A figure that rounds to zero is not a negative zero.
    expect(formatFigure(-0.04, 1)).toBe("0");
  });

  it("prints the outputs as the statement does", () => {
    expect(formatOutputs({ x: 1.234, cagr: -0.0612, futurePrice: 281.4 })).toEqual({
      x: "1.2",
      cagr: "(6) %",
      futurePrice: "281",
    });
    expect(formatOutputs({ x: 0.5, cagr: 0.14, futurePrice: 12.345 }).futurePrice).toBe("12.35");
  });
});
