import type { ReactNode } from "react";
import { APPLE_EXAMPLE } from "../../projection/appleExample";
import { projectionSteps } from "../../projection/calculator";
import { formatFigure, formatOutputs, toUi } from "../projection/figures";

const { facts } = APPLE_EXAMPLE;
const base = APPLE_EXAMPLE.scenarios.base;
const steps = projectionSteps(facts, base);
const printed = formatOutputs(steps);
const millions = (wire: number) => toUi(wire, "millions");
const percent = (wire: number) => `${toUi(wire, "percent")} %`;
const years = <sup>{base.horizon}</sup>;
const onEarnings = base.exitMultiple.kind === "pe";
const multipleName = onEarnings ? "P/E" : "P/S";

const compounded = (start: number, rate: number) => (
  <>
    {millions(start)} × {formatFigure(1 + rate, 3)}
    {years}
  </>
);

const ROWS: [step: string, working: ReactNode, result: ReactNode][] = [
  ["Future revenue", compounded(facts.ttmRevenue, base.growthRate), millions(steps.futureRevenue)],
  ["Future net income", `${millions(steps.futureRevenue)} × ${percent(base.futureNetMargin)}`, millions(steps.futureNetIncome)],
  [
    "Future market cap",
    `${millions(onEarnings ? steps.futureNetIncome : steps.futureRevenue)} × ${toUi(base.exitMultiple.value, "multiple")} ${multipleName}`,
    millions(steps.futureMarketCap),
  ],
  ["Future shares", compounded(facts.dilutedShares, base.dilutionRate), millions(steps.futureShares)],
  [
    "Future share price",
    `${millions(steps.futureMarketCap)} ÷ ${millions(steps.futureShares)}`,
    formatFigure(steps.futurePrice, 2),
  ],
  [
    "The X",
    `${formatFigure(steps.futurePrice, 2)} ÷ ${formatFigure(facts.currentPrice, 2)}`,
    <>
      {printed.x}
      <sup>×</sup>
    </>,
  ],
  [
    "CAGR",
    <>
      {formatFigure(steps.x, 4)}
      <sup>1/{base.horizon}</sup> − 1
    </>,
    printed.cagr,
  ],
];

/** The shared Apple example's Base scenario, run through the calculator's own formula one step at a time. */
export function WorkedExample() {
  return (
    <section className="guide-example" aria-labelledby="worked-example">
      <div className="caption">
        <span id="worked-example">Worked example</span>
        <span className="units">Apple Inc., Base scenario. USD millions, except per share. Illustrative, not a prediction</span>
      </div>
      <p className="footnote">
        Today: a share price of {formatFigure(facts.currentPrice, 2)}, revenue of {millions(facts.ttmRevenue)} and{" "}
        {millions(facts.dilutedShares)} diluted shares. The Base guesses: revenue growing {percent(base.growthRate)} a
        year for {base.horizon} years, a {percent(base.futureNetMargin)} net margin, a {multipleName} of{" "}
        {toUi(base.exitMultiple.value, "multiple")}, and a share count that{" "}
        {base.dilutionRate < 0
          ? `shrinks ${percent(-base.dilutionRate)} a year through buybacks`
          : `grows ${percent(base.dilutionRate)} a year`}
        .
      </p>
      <table className="statement-table worked-example" aria-labelledby="worked-example">
        <thead>
          <tr>
            <th scope="col">Step</th>
            <th scope="col">Working</th>
            <th scope="col">Result</th>
          </tr>
        </thead>
        <tbody>
          {ROWS.map(([step, working, result]) => (
            <tr key={step}>
              <th scope="row" className="label">
                {step}
              </th>
              <td className="num working">{working}</td>
              <td className="num">{result}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
