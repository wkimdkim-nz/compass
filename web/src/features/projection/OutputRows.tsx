import { GuideHelp } from "../guide/GuideHelp";
import { BLANK, type PrintedOutputs } from "./figures";
import type { ScenarioName } from "./projectionApi";
import { SCENARIO_NAMES } from "./projectionForm";

/** A scenario that gives no result is left out, and prints the dash. */
type Columns<Column> = Partial<Record<ScenarioName, Column>>;

type Props = (
  | { columns: Columns<PrintedOutputs>; showFuturePrice?: true }
  /** The specimen prints no Future share price row, so its columns have none to give. */
  | { columns: Columns<Omit<PrintedOutputs, "futurePrice">>; showFuturePrice: false }
) & {
  /** Tints the rows for a moment after an edit. */
  tinted?: boolean;
  withGuideHelp?: boolean;
};

/** The rows that close a statement's table: the Return multiple as its total, then CAGR and the future share price. */
export function OutputRows({ tinted = false, withGuideHelp = false, ...props }: Props) {
  const { columns } = props;
  const labelled = (id: string, label: string) => (withGuideHelp ? <GuideHelp id={id}>{label}</GuideHelp> : label);
  return (
    <>
      <tr className={`subtotal-rule total${tinted ? " changed" : ""}`}>
        <th scope="row" className="label">
          {labelled("the-x", "Return multiple")}
        </th>
        {SCENARIO_NAMES.map((name) => (
          <td key={name} className="num x">
            {columns[name] ? (
              <>
                {columns[name].x}
                <sup>×</sup>
              </>
            ) : (
              BLANK
            )}
          </td>
        ))}
      </tr>
      <tr className="double-rule" aria-hidden="true">
        <td colSpan={4} />
      </tr>
      <tr className={tinted ? "changed" : undefined}>
        <th scope="row" className="label">
          {labelled("cagr", "CAGR")} <small>% per year</small>
        </th>
        {SCENARIO_NAMES.map((name) => (
          <td key={name} className="num">
            {columns[name]?.cagr ?? BLANK}
          </td>
        ))}
      </tr>
      {props.showFuturePrice !== false && (
        <tr className={tinted ? "changed" : undefined}>
          <th scope="row" className="label">
            {labelled("future-price", "Future share price")} <small>USD</small>
          </th>
          {SCENARIO_NAMES.map((name) => (
            <td key={name} className="num">
              {props.columns[name]?.futurePrice ?? BLANK}
            </td>
          ))}
        </tr>
      )}
    </>
  );
}
