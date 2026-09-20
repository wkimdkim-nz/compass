import { useEffect, useRef } from "react";

type Props = {
  ticker: string;
  /** Absent for a projection saved a moment ago on this page. */
  savedOn: string | undefined;
  busy: boolean;
  onKeep: () => void;
  onReplace: () => void;
};

/**
 * The confirmation an Update waits on. Opens on the safe choice, which Escape and a click on the
 * dimmed page also take; Tab stays within it, and focus goes back where it was on closing.
 */
export function ConfirmReplaceDialog({ ticker, savedOn, busy, onKeep, onReplace }: Props) {
  const dialog = useRef<HTMLDivElement>(null);
  const keep = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const opener = document.activeElement;
    keep.current?.focus();
    return () => {
      if (opener instanceof HTMLElement) opener.focus();
    };
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onKeep();
      if (event.key !== "Tab") return;
      const buttons = [...(dialog.current?.querySelectorAll<HTMLButtonElement>("button:not(:disabled)") ?? [])];
      if (buttons.length === 0) return;
      const at = buttons.indexOf(document.activeElement as HTMLButtonElement);
      const next = event.shiftKey ? at - 1 : at + 1;
      event.preventDefault();
      buttons[(next + buttons.length) % buttons.length].focus();
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onKeep]);

  return (
    <div className="modal-backdrop" onClick={(e) => e.target === e.currentTarget && onKeep()}>
      <div ref={dialog} className="modal" role="dialog" aria-modal="true" aria-labelledby="confirm-title">
        <h2 id="confirm-title">Replace your saved projection for {ticker}?</h2>
        <p>
          The assumptions you saved{savedOn && ` on ${savedOn}`} will be overwritten with the ones on this page. Filed
          figures are never stored.
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
