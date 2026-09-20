import { BLANK, type PrintedOutputs } from "./figures";

/** The specimen prints no future share price, so it has none to give. */
type Column = Pick<PrintedOutputs, "x" | "cagr"> & Partial<PrintedOutputs>;

type Props = {
  /** Bear, Base, Bull, in that order; undefined for a column that gives no result, which prints the dash. */
  columns: readonly [Column | undefined, Column | undefined, Column | undefined];
  /** Whether the Future share price row is printed. */
  showFuturePrice?: boolean;
  /** Tints the rows for a moment after an edit. */
  tinted?: boolean;
};

/** The rows that close a statement's table: the Return multiple as its total, then CAGR and the future share price. */
export function OutputRows({ columns, showFuturePrice = true, tinted = false }: Props) {
  return (
    <>
      <tr className={`subtotal-rule total${tinted ? " changed" : ""}`}>
        <th scope="row" className="label">
          Return multiple
        </th>
        {columns.map((outputs, i) => (
          <td key={i} className="num x">
            {outputs ? (
              <>
                {outputs.x}
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
          CAGR <small>% per year</small>
        </th>
        {columns.map((outputs, i) => (
          <td key={i} className="num">
            {outputs?.cagr ?? BLANK}
          </td>
        ))}
      </tr>
      {showFuturePrice && (
        <tr className={tinted ? "changed" : undefined}>
          <th scope="row" className="label">
            Future share price <small>USD</small>
          </th>
          {columns.map((outputs, i) => (
            <td key={i} className="num">
              {outputs?.futurePrice ?? BLANK}
            </td>
          ))}
        </tr>
      )}
    </>
  );
}
