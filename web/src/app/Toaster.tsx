import { useEffect, useRef, useState } from "react";
import { useDispatch, useSelector } from "react-redux";
import type { RootState } from "./store";
import { dismissToast, type Toast } from "./toast";

/** How long a toast stays, counting only the time nothing holds it. */
const TOAST_MS = 5_000;

/**
 * The one toast area, mounted once in the page layout. The live region is always there, so that a
 * toast put into it is announced; the toast itself comes and goes.
 */
export function Toaster() {
  const current = useSelector((state: RootState) => state.toast.current);
  return (
    <div role="status" aria-label="Notifications" className="toast-region">
      {/* Keyed by id: a replacing toast starts its own 5 seconds, and nothing held over from the last. */}
      {current && <ToastNote key={current.id} toast={current} />}
    </div>
  );
}

function ToastNote({ toast }: { toast: Toast }) {
  const dispatch = useDispatch();
  const [hovered, setHovered] = useState(false);
  const [focused, setFocused] = useState(false);
  const remainingMs = useRef(TOAST_MS);

  useEffect(() => {
    if (hovered || focused) return;
    const startedAt = Date.now();
    const timer = setTimeout(() => dispatch(dismissToast(toast.id)), remainingMs.current);
    return () => {
      clearTimeout(timer);
      remainingMs.current -= Date.now() - startedAt;
    };
  }, [hovered, focused, toast.id, dispatch]);

  return (
    <div
      className="toast"
      // Pointer events, not mouse events: a tap sends a mouseenter with no mouseleave to follow, which
      // would hold the toast for good. A finger does not hover, so only a mouse or pen pauses it.
      onPointerEnter={(event) => {
        if (event.pointerType !== "touch") setHovered(true);
      }}
      onPointerLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) setFocused(false);
      }}
    >
      <p>{toast.text}</p>
      <button type="button" className="toast-dismiss" aria-label="Dismiss" onClick={() => dispatch(dismissToast(toast.id))}>
        ×
      </button>
    </div>
  );
}
