import { useEffect, useRef } from "react";

type Props = {
  ticker: string;
  /** Absent for a projection saved a moment ago on this page. */
  savedOn: string | undefined; busy: boolean; onKeep: () => void; onReplace: () => void };

/** The confirmation an Update waits on. Opens on the safe choice, and Escape takes it. */
export function ConfirmReplaceDialog({ ticker, savedOn, busy, onKeep, onReplace }: Props) {
  const keep = useRef<HTMLButtonElement>(null);
  useEffect(() => keep.current?.focus(), []);

  return (
    <div className="modal-backdrop" onKeyDown={(e) => e.key === "Escape" && onKeep()}>
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby="confirm-title">
        <h2 id="confirm-title">Replace your saved projection for {ticker}?</h2>
        <p>
          The assumptions you saved{savedOn && ` on ${savedOn}`} will be overwritten with the ones on this page. Filed figures are
          never stored.
        </p>
        <div className="actions">
          <button ref={keep} className="btn btn-quiet" type="button" onClick={onKeep}>
            Keep saved
          </button>
          <button className="btn" type="button" disabled={busy} onClick={onReplace}>
            Replace
          </button>
        </div>
      </div>
    </div>
  );
}
