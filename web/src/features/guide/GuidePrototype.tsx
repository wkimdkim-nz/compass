// PROTOTYPE — throwaway (branch prototype/phase2-ui). Two questions:
//   /prototype/calculator-help?variant=A|B|C — how a calculator label leads to the guide
//   /prototype/guide?variant=A|B|C           — what the public guide page looks like
import { useEffect, useState } from "react";
import { Link, useLocation } from "react-router";
import { PrototypeSwitcher, useVariant } from "../../app/PrototypeSwitcher";
import { byId, ENTRIES, type Entry } from "./guideEntriesPrototype";

const ROWS = ["horizon", "growth", "margin", "multiple", "dilution"];
const FACTS = ["price", "revenue", "shares", "net-margin"];
const STUB: Record<string, [string, string, string]> = {
  horizon: ["5", "5", "5"],
  growth: ["2.0", "6.0", "10.0"],
  margin: ["22.0", "25.0", "27.0"],
  multiple: ["20", "28", "34"],
  dilution: ["(2.5)", "(2.5)", "(2.5)"],
};
const FACT_STUB: Record<string, string> = { price: "228.00", revenue: "391,035", shares: "15,408", "net-margin": "24.0 %" };

const guideHref = (id: string, variant = "B") => `/prototype/guide?variant=${variant}#${id}`;

/* ---------- Calculator help variants ---------- */

function LabelA({ id }: { id: string }) {
  const e = byId(id);
  return (
    <Link to={guideHref(id)} style={{ color: "inherit", textDecorationStyle: "dotted", textDecorationColor: "var(--pencil)" }}>
      {e.name}
    </Link>
  );
}

function LabelB({ id }: { id: string }) {
  const e = byId(id);
  return (
    <>
      {e.name}{" "}
      <Link
        to={guideHref(id)}
        aria-label={`What is ${e.name}? Open the guide`}
        title={e.what}
        style={{
          display: "inline-grid",
          placeItems: "center",
          width: "1.15em",
          height: "1.15em",
          borderRadius: 999,
          border: "1px solid var(--pencil)",
          fontSize: "0.75em",
          textDecoration: "none",
          verticalAlign: "0.1em",
        }}
      >
        ?
      </Link>
    </>
  );
}

function ExplainerRow({ id, span }: { id: string; span: number }) {
  const e = byId(id);
  return (
    <tr>
      <td colSpan={span} style={{ padding: "0.5rem 0 0.75rem", height: "auto" }}>
        <div className="text-sm" style={{ borderLeft: "2px solid var(--pencil)", paddingLeft: "0.75rem" }}>
          <p>{e.what}</p>
          {e.anchor && (
            <p className="text-ink-2">
              <b className="font-medium text-ink">Anchor on:</b> {e.anchor}
            </p>
          )}
          <p>
            <Link to={guideHref(id)}>More in the guide, with where to find it →</Link>
          </p>
        </div>
      </td>
    </tr>
  );
}

function CalculatorHelp({ variant }: { variant: string }) {
  const [open, setOpen] = useState<string | null>("growth");
  const label = (id: string) => {
    if (variant === "A") return <LabelA id={id} />;
    if (variant === "B") return <LabelB id={id} />;
    return (
      <button
        type="button"
        className="link"
        aria-expanded={open === id}
        onClick={() => setOpen(open === id ? null : id)}
        style={{ color: "inherit", textAlign: "left", textDecoration: "underline dotted var(--pencil)" }}
      >
        {byId(id).name} <span className="text-pencil text-xs">{open === id ? "▴" : "▾"}</span>
      </button>
    );
  };

  return (
    <main>
      <h1>
        Apple Inc. <span className="tag">AAPL</span>
      </h1>
      <p className="provenance">
        <b>Figures to 28 Jun 2025.</b> Fundamentals: SEC EDGAR. Quote: Finnhub. (Prototype — stub figures.)
      </p>
      {variant === "A" && <p className="note">Every label is a link: click any input's name to open its entry in the guide.</p>}
      {variant === "B" && <p className="note">A “?” beside each label opens its entry in the guide; hover shows one line.</p>}
      {variant === "C" && <p className="note">Click a label to unfold a short explainer under the row; the guide holds the rest.</p>}

      <section>
        <div className="caption">
          <span>Base facts</span>
          <span className="units">
            USD millions, except per share
            {variant !== "A" && (
              <>
                {" · "}
                <Link to="/prototype/guide?variant=B#facts">About these figures</Link>
              </>
            )}
          </span>
        </div>
        <table className="statement-table">
          <tbody>
            {FACTS.map((id) => (
              <FactOrRow key={id} id={id} variant={variant} open={open} label={label(id)} />
            ))}
          </tbody>
        </table>
      </section>

      <section>
        <div className="caption">
          <span>Assumptions</span>
          <span className="units">Your guesses; the first draft comes from filed history</span>
        </div>
        <table className="statement-table">
          <thead>
            <tr>
              <th scope="col">Input</th>
              <th scope="col">Bear</th>
              <th scope="col">Base</th>
              <th scope="col">Bull</th>
            </tr>
          </thead>
          <tbody>
            {ROWS.map((id) => (
              <ScenarioRow key={id} id={id} variant={variant} open={open} label={label(id)} />
            ))}
            <tr className="total">
              <th scope="row" className="label">
                {variant === "C" ? label("x") : variant === "B" ? <LabelB id="x" /> : <LabelA id="x" />}
              </th>
              <td className="num x">0.7</td>
              <td className="num x">1.2</td>
              <td className="num x">1.9</td>
            </tr>
            {variant === "C" && open === "x" && <ExplainerRow id="x" span={4} />}
          </tbody>
        </table>
      </section>
      <p className="note mt-6">
        Then: <Link to="/prototype/guide">see the guide page variants →</Link>
      </p>
    </main>
  );
}

function FactOrRow({ id, variant, open, label }: { id: string; variant: string; open: string | null; label: React.ReactNode }) {
  return (
    <>
      <tr>
        <th scope="row" className="label">
          {label}
        </th>
        <td className="num">{FACT_STUB[id]}</td>
      </tr>
      {variant === "C" && open === id && <ExplainerRow id={id} span={2} />}
    </>
  );
}

function ScenarioRow({ id, variant, open, label }: { id: string; variant: string; open: string | null; label: React.ReactNode }) {
  return (
    <>
      <tr>
        <th scope="row" className="label">
          {label}
        </th>
        {STUB[id].map((v, i) => (
          <td key={i} className="num">
            <input className="fig" defaultValue={v} aria-label={`${byId(id).name} ${i}`} />
          </td>
        ))}
      </tr>
      {variant === "C" && open === id && <ExplainerRow id={id} span={4} />}
    </>
  );
}

export function CalculatorHelpPrototype() {
  const variant = useVariant(["A", "B", "C"]);
  return (
    <>
      <CalculatorHelp variant={variant} />
      <PrototypeSwitcher
        variants={[
          { key: "A", name: "Labels are links" },
          { key: "B", name: "? marker per label" },
          { key: "C", name: "Unfold explainer row" },
        ]}
      />
    </>
  );
}

/* ---------- Guide page variants ---------- */

function Where({ e }: { e: Entry }) {
  if (e.where.length === 0) return <span className="text-ink-2">—</span>;
  return (
    <>
      {e.where.map((w, i) => (
        <span key={w.label}>
          {i > 0 && ", "}
          <a href={w.href} target="_blank" rel="noopener noreferrer">
            {w.label}
          </a>
        </span>
      ))}
    </>
  );
}

const targetStyle = `
  .guide-entry:target { background: var(--pencil-tint); outline: 6px solid var(--pencil-tint); }
  .guide-entry { scroll-margin-top: 1rem; }
`;

/** A: one statement-style reference table, a row per input. */
function GuideA() {
  const group = (kind: Entry["kind"], title: string, units: string) => (
    <section id={kind === "fact" ? "facts" : undefined} className="guide-entry">
      <div className="caption">
        <span>{title}</span>
        <span className="units">{units}</span>
      </div>
      <table className="statement-table">
        <thead>
          <tr>
            <th scope="col" style={{ textAlign: "left" }}>
              Input
            </th>
            <th scope="col" style={{ textAlign: "left" }}>
              What it is, and how to choose it
            </th>
            <th scope="col" style={{ textAlign: "left" }}>
              Find it
            </th>
          </tr>
        </thead>
        <tbody>
          {ENTRIES.filter((e) => e.kind === kind).map((e) => (
            <tr key={e.id} id={e.id} className="guide-entry" style={{ verticalAlign: "top" }}>
              <th scope="row" className="label" style={{ paddingTop: 8, width: "22%" }}>
                <b className="font-medium">{e.name}</b>
              </th>
              <td style={{ paddingTop: 8, paddingBottom: 8, height: "auto" }} className="text-sm">
                {e.what}
                {e.anchor && <div className="text-ink-2 mt-1">Anchor on: {e.anchor}</div>}
                {e.sanity && <div className="text-ink-2 mt-1">Sanity: {e.sanity}</div>}
              </td>
              <td style={{ paddingTop: 8, paddingBottom: 8, height: "auto", width: "26%" }} className="text-sm">
                <Where e={e} />
                {e.filing && <div className="text-ink-2 mt-1">In a filing: {e.filing}</div>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
  return (
    <>
      <p className="cover one-line" style={{ paddingTop: 0 }}>
        Every figure on the calculator, what it means, and where you would look it up yourself.
      </p>
      {group("fact", "Base facts", "Facts: looked up, not guessed")}
      {group("assumption", "Assumptions", "Guesses: pre-filled from history, yours to change")}
      {group("output", "Outputs", "Computed from the two above")}
      <WorkedExample />
    </>
  );
}

/** B: long-form article with a sticky contents rail, one section per input. */
function GuideB() {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "minmax(0,11rem) minmax(0,1fr)", gap: "2rem" }} className="mt-6">
      <nav aria-label="Contents" style={{ position: "sticky", top: 16, alignSelf: "start" }} className="text-sm">
        <div className="text-ink-2 mb-2">Contents</div>
        {(["fact", "assumption", "output"] as const).map((k) => (
          <div key={k} className="mb-3">
            <div className="font-medium">{{ fact: "Base facts", assumption: "Assumptions", output: "Outputs" }[k]}</div>
            {ENTRIES.filter((e) => e.kind === k).map((e) => (
              <div key={e.id}>
                <a href={`#${e.id}`}>{e.name}</a>
              </div>
            ))}
          </div>
        ))}
        <a href="#example">Worked example</a>
      </nav>
      <div>
        <p className="text-lg" id="facts">
          Every figure on the calculator, what it means, and where you would look it up yourself.
        </p>
        {ENTRIES.map((e) => (
          <section key={e.id} id={e.id} className="guide-entry mt-8">
            <h2 className="text-lg font-semibold border-b border-rule pb-1">
              {e.name} <span className="tag text-sm">{{ fact: "fact", assumption: "your guess", output: "output" }[e.kind]}</span>
            </h2>
            <dl className="text-sm mt-2" style={{ display: "grid", gridTemplateColumns: "8rem 1fr", rowGap: 6 }}>
              <dt className="text-ink-2">What it is</dt>
              <dd>{e.what}</dd>
              {e.anchor && (
                <>
                  <dt className="text-ink-2">Anchor on</dt>
                  <dd>{e.anchor}</dd>
                </>
              )}
              {e.where.length > 0 && (
                <>
                  <dt className="text-ink-2">Find it online</dt>
                  <dd>
                    <Where e={e} />
                  </dd>
                </>
              )}
              {e.filing && (
                <>
                  <dt className="text-ink-2">In a 10-K / 10-Q</dt>
                  <dd>{e.filing}</dd>
                </>
              )}
              {e.sanity && (
                <>
                  <dt className="text-ink-2">Sanity check</dt>
                  <dd>{e.sanity}</dd>
                </>
              )}
              <dt className="text-ink-2">Apple specimen</dt>
              <dd className="num" style={{ textAlign: "left" }}>
                {e.specimen}
              </dd>
            </dl>
          </section>
        ))}
        <WorkedExample />
      </div>
    </div>
  );
}

/** C: the worked example is the page; each step introduces its inputs, details fold out. */
function GuideC() {
  const steps: { line: string; ids: string[]; result: string }[] = [
    { line: "Start from today's sales", ids: ["revenue"], result: "$391,035m" },
    { line: "Grow them for the horizon", ids: ["growth", "horizon"], result: "× 1.06⁵ = $523,293m" },
    { line: "Keep a share as profit", ids: ["margin", "net-margin"], result: "× 25% = $130,823m" },
    { line: "Price the profit", ids: ["multiple"], result: "× 28 = $3,663bn market cap" },
    { line: "Split it across the future share count", ids: ["shares", "dilution"], result: "÷ 13,576m = $270 / share" },
    { line: "Compare with today's price", ids: ["price", "x", "cagr"], result: "$270 ÷ $228 = 1.2 X, 3 % / yr" },
  ];
  return (
    <>
      <p className="text-lg mt-6">
        One projection, step by step, on Apple's Base scenario. Each step names the inputs it uses; open one to see what it
        means and where to find it.
      </p>
      <ol className="mt-6" id="facts">
        {steps.map((s, i) => (
          <li key={s.line} className="border-b border-hairline py-3">
            <div className="flex justify-between gap-4 items-baseline">
              <span className="font-medium">
                {i + 1}. {s.line}
              </span>
              <span className="num text-sm">{s.result}</span>
            </div>
            {s.ids.map((id) => {
              const e = byId(id);
              return (
                <details key={id} id={id} className="guide-entry text-sm mt-1 ml-5">
                  <summary className="text-pencil cursor-pointer">{e.name}</summary>
                  <div className="py-2">
                    <p>{e.what}</p>
                    {e.anchor && <p className="text-ink-2">Anchor on: {e.anchor}</p>}
                    {e.where.length > 0 && (
                      <p className="text-ink-2">
                        Find it: <Where e={e} />
                      </p>
                    )}
                    {e.filing && <p className="text-ink-2">In a filing: {e.filing}</p>}
                    {e.sanity && <p className="text-ink-2">Sanity: {e.sanity}</p>}
                  </div>
                </details>
              );
            })}
          </li>
        ))}
      </ol>
    </>
  );
}

function WorkedExample() {
  return (
    <section id="example" className="mt-10">
      <div className="caption">
        <span>Worked example</span>
        <span className="units">Apple, Base scenario — illustrative, not a prediction</span>
      </div>
      <table className="statement-table text-sm">
        <tbody>
          {[
            ["Future revenue", "391,035 × 1.06⁵", "523,293"],
            ["Future net income", "523,293 × 25%", "130,823"],
            ["Future market cap", "130,823 × 28 P/E", "3,663,051"],
            ["Future shares", "15,408 × 0.975⁵", "13,576"],
            ["Future price", "3,663,051 ÷ 13,576", "$270"],
            ["X", "270 ÷ 228", "1.18"],
            ["CAGR", "1.18^(1/5) − 1", "3 %"],
          ].map(([a, b, c]) => (
            <tr key={a}>
              <th scope="row" className="label">
                {a}
              </th>
              <td className="text-ink-2">{b}</td>
              <td className="num">{c}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

export function GuidePrototype() {
  const variant = useVariant(["A", "B", "C"]);
  const { hash } = useLocation();
  useEffect(() => {
    if (!hash) return;
    const el = document.getElementById(hash.slice(1));
    if (el instanceof HTMLDetailsElement) el.open = true;
    el?.scrollIntoView();
  }, [hash, variant]);
  return (
    <main>
      <style>{targetStyle}</style>
      <h1>
        Guide <span className="tag">Reading the calculator</span>
      </h1>
      {variant === "A" && <GuideA />}
      {variant === "B" && <GuideB />}
      {variant === "C" && <GuideC />}
      <PrototypeSwitcher
        variants={[
          { key: "A", name: "Reference table" },
          { key: "B", name: "Article + contents rail" },
          { key: "C", name: "Worked example first" },
        ]}
      />
    </main>
  );
}

/* ---------- Round 2: refining the "?" marker (variant B won) ---------- */

const markerBase: React.CSSProperties = { textDecoration: "none", lineHeight: 1 };

function Marker({ id, style }: { id: string; style: string }) {
  const e = byId(id);
  const [pop, setPop] = useState(false);
  const common = { href: guideHref(id, "A"), target: "_blank", rel: "noopener", "aria-label": `What is ${e.name}? Opens the guide in a new tab` };
  if (style === "B1")
    return (
      <a {...common} title={e.what} style={{ ...markerBase, display: "inline-grid", placeItems: "center", width: "1.15em", height: "1.15em", borderRadius: 999, border: "1px solid var(--pencil)", fontSize: "0.75em", verticalAlign: "0.1em", marginLeft: 4 }}>
        ?
      </a>
    );
  if (style === "B2")
    return (
      <a {...common} title={e.what} style={{ ...markerBase, fontSize: "0.7em", verticalAlign: "super", marginLeft: 2, fontWeight: 600 }}>
        ?
      </a>
    );
  if (style === "B3")
    return (
      <a {...common} title={e.what} style={{ ...markerBase, float: "right", fontSize: "0.8em", padding: "0 0.4rem", border: "1px solid var(--hairline)", borderRadius: 4, color: "var(--ink-2)" }}>
        ?
      </a>
    );
  // B4: click opens a small popover; the guide is one more click away.
  return (
    <span style={{ position: "relative", marginLeft: 4 }}>
      <button type="button" aria-expanded={pop} aria-label={`What is ${e.name}?`} onClick={() => setPop(!pop)} className="link" style={{ display: "inline-grid", placeItems: "center", width: "1.15em", height: "1.15em", borderRadius: 999, border: "1px solid var(--pencil)", fontSize: "0.75em", verticalAlign: "0.1em", textDecoration: "none" }}>
        ?
      </button>
      {pop && (
        <span role="dialog" aria-label={e.name} className="text-sm" style={{ position: "absolute", left: 0, top: "1.6em", zIndex: 10, width: "18rem", background: "var(--paper)", border: "1px solid var(--rule)", padding: "0.6rem 0.75rem", boxShadow: "0 6px 20px rgba(0,0,0,.18)", fontWeight: 400, whiteSpace: "normal" }}>
          <span style={{ display: "block" }}>{e.what}</span>
          {e.anchor && <span style={{ display: "block", marginTop: 4 }} className="text-ink-2">Anchor on: {e.anchor}</span>}
          <a {...common} style={{ display: "inline-block", marginTop: 6 }}>Where to find it, in the guide ↗</a>
        </span>
      )}
    </span>
  );
}

export function MarkerPrototype() {
  const style = useVariant(["B1", "B2", "B3", "B4"]);
  const row = (id: string, cells: string[]) => (
    <tr key={id}>
      <th scope="row" className="label">
        {byId(id).name}
        <Marker id={id} style={style} />
      </th>
      {cells.map((v, i) => (
        <td key={i} className="num">
          <input className="fig" defaultValue={v} aria-label={`${byId(id).name} ${i}`} />
        </td>
      ))}
    </tr>
  );
  return (
    <main>
      <h1>
        Apple Inc. <span className="tag">AAPL</span>
      </h1>
      <p className="provenance">
        <b>Figures to 28 Jun 2025.</b> (Prototype — stub figures.) Every guide link opens in a new tab, so unsaved edits stay.
      </p>
      <section>
        <div className="caption">
          <span>Base facts</span>
          <span className="units">USD millions, except per share</span>
        </div>
        <table className="statement-table">
          <tbody>{FACTS.map((id) => row(id, [FACT_STUB[id]]))}</tbody>
        </table>
      </section>
      <section>
        <div className="caption">
          <span>Assumptions</span>
          <span className="units">Your guesses; the first draft comes from filed history</span>
        </div>
        <table className="statement-table">
          <thead>
            <tr>
              <th scope="col">Input</th>
              <th scope="col">Bear</th>
              <th scope="col">Base</th>
              <th scope="col">Bull</th>
            </tr>
          </thead>
          <tbody>
            {ROWS.map((id) => row(id, STUB[id]))}
            {row("x", ["0.7", "1.2", "1.9"])}
            {row("cagr", ["(6) %", "4 %", "14 %"])}
          </tbody>
        </table>
      </section>
      <PrototypeSwitcher
        variants={[
          { key: "B1", name: "Circled ?, hover line" },
          { key: "B2", name: "Superscript ?, footnote-style" },
          { key: "B3", name: "? aligned at the label's edge" },
          { key: "B4", name: "Circled ?, click for a popover" },
        ]}
      />
    </main>
  );
}
