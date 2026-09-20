/**
 * The "not financial advice" framing (ADR 0006), in the one place it is worded. The footnote sits
 * directly under the calculator's output rows; the footer closes every page.
 */
export function Disclaimer({ as }: { as: "footnote" | "footer" }) {
  if (as === "footnote") {
    return (
      <p role="note" className="footnote">
        <b>Hypothetical.</b> These figures follow from the assumptions above, which you chose. They are not a
        prediction and not investment advice.
      </p>
    );
  }
  return (
    <p>
      Compass projections are hypothetical, follow from assumptions the user chooses, and are for education only. They
      are not investment advice.
    </p>
  );
}
