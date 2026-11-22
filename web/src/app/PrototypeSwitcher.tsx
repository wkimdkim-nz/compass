// PROTOTYPE — throwaway (branch prototype/phase2-ui). Not for main.
import { useEffect } from "react";
import { useSearchParams } from "react-router";

export function useVariant(keys: string[]) {
  const [params] = useSearchParams();
  const v = params.get("variant") ?? keys[0];
  return keys.includes(v) ? v : keys[0];
}

/** Floating pill that cycles ?variant= with the arrows (buttons or ← →). Dev builds only. */
export function PrototypeSwitcher({ variants }: { variants: { key: string; name: string }[] }) {
  const [params, setParams] = useSearchParams();
  const keys = variants.map((v) => v.key);
  const current = useVariant(keys);
  const at = keys.indexOf(current);

  const go = (step: number) => {
    const next = keys[(at + step + keys.length) % keys.length];
    const p = new URLSearchParams(params);
    p.set("variant", next);
    setParams(p, { replace: true });
  };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const t = e.target as HTMLElement;
      if (t.closest("input, textarea, [contenteditable]")) return;
      if (e.key === "ArrowLeft") go(-1);
      if (e.key === "ArrowRight") go(1);
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  });

  if (import.meta.env.PROD) return null;
  return (
    <div
      style={{
        position: "fixed",
        bottom: 16,
        left: "50%",
        transform: "translateX(-50%)",
        background: "#111",
        color: "#fff",
        borderRadius: 999,
        padding: "6px 10px",
        display: "flex",
        gap: 12,
        alignItems: "center",
        boxShadow: "0 4px 16px rgba(0,0,0,.35)",
        fontFamily: "system-ui, sans-serif",
        fontSize: 13,
        zIndex: 1000,
      }}
    >
      <button type="button" onClick={() => go(-1)} style={{ color: "#fff", padding: "0 6px" }} aria-label="Previous variant">
        ←
      </button>
      <span>
        <b>{current}</b> ({variants[at].name})
      </span>
      <button type="button" onClick={() => go(1)} style={{ color: "#fff", padding: "0 6px" }} aria-label="Next variant">
        →
      </button>
    </div>
  );
}
