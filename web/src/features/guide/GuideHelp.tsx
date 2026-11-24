import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { guideEntry } from "./guideEntries";

type Props = {
  id: string;
  /** The label it follows, which the "?" never wraps away from. */
  children: ReactNode;
};

/**
 * The circled "?" after a calculator label, and the popover it discloses: the guide entry's gist.
 * Escape and a click anywhere outside it close it, so opening another closes this one.
 */
export function GuideHelp({ id, children }: Props) {
  const entry = guideEntry(id);
  const [open, setOpen] = useState(false);
  const popoverId = useId();
  const help = useRef<HTMLSpanElement>(null);
  const marker = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    // A click, not a pointerdown: activating another "?" from the keyboard clicks it too.
    const onClick = (event: MouseEvent) => {
      if (!help.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      if (help.current?.contains(document.activeElement)) marker.current?.focus();
      setOpen(false);
    };
    document.addEventListener("click", onClick);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("click", onClick);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const [head, lastWord] = splitBeforeLastWord(children);

  return (
    <>
      {head}
      <span ref={help} className="help">
        {lastWord}
        <button
          ref={marker}
          type="button"
          className="help-marker"
          aria-expanded={open}
          aria-controls={open ? popoverId : undefined}
          aria-label={`What is ${entry.name}?`}
          onClick={() => setOpen(!open)}
        >
          ?
        </button>
        {open && (
          <span id={popoverId} className="help-popover">
            <span>{entry.what}</span>
            {entry.anchor && <span className="help-anchor">Anchor on: {entry.anchor}</span>}
            {/* A new tab, so a projection being edited here is never lost to the guide. */}
            <Link to={`/guide#${entry.id}`} target="_blank" rel="noopener noreferrer">
              Where to find it, in the guide ↗
            </Link>
          </span>
        )}
      </span>
    </>
  );
}

/** Plain text gives up all but its last word to wrap freely; markup can't be split, so it stays whole. */
function splitBeforeLastWord(label: ReactNode): [ReactNode, ReactNode] {
  if (typeof label !== "string") return [null, label];
  const lastWordStart = label.lastIndexOf(" ") + 1;
  return [label.slice(0, lastWordStart), label.slice(lastWordStart)];
}
