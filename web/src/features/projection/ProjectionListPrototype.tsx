// PROTOTYPE — throwaway (branch prototype/phase2-ui). Stub data, in-memory delete.
//   /prototype/landing?variant=A|B|C      — the signed-in landing page with your projections on it
//   /prototype/projections?variant=A|B    — the "My projections" page from the nav
import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { PrototypeSwitcher, useVariant } from "../../app/PrototypeSwitcher";

type Row = { ticker: string; company: string; scenarios: string[]; saved: string };

const STUB: Row[] = [
  { ticker: "NVDA", company: "NVIDIA Corp", scenarios: ["Bear", "Base", "Bull"], saved: "21 Nov 2026" },
  { ticker: "AAPL", company: "Apple Inc.", scenarios: ["Bear", "Base", "Bull"], saved: "18 Nov 2026" },
  { ticker: "COST", company: "Costco Wholesale Corp", scenarios: ["Base"], saved: "2 Nov 2026" },
  { ticker: "BRK-B", company: "Berkshire Hathaway Inc.", scenarios: ["Bear", "Base"], saved: "14 Oct 2026" },
  { ticker: "SNOW", company: "Snowflake Inc.", scenarios: ["Bear", "Base", "Bull"], saved: "30 Sep 2026" },
  { ticker: "MSFT", company: "Microsoft Corp", scenarios: ["Bear", "Base", "Bull"], saved: "12 Sep 2026" },
  { ticker: "SHOP", company: "Shopify Inc. (typed in)", scenarios: ["Base", "Bull"], saved: "2 Sep 2026" },
];

const open = (t: string) => `/projections/${t}`;

function useRows() {
  const [rows, setRows] = useState(STUB);
  const [empty, setEmpty] = useState(false);
  return { rows: empty ? [] : rows, remove: (t: string) => setRows((r) => r.filter((x) => x.ticker !== t)), empty, setEmpty };
}

function EmptyToggle({ empty, setEmpty }: { empty: boolean; setEmpty: (b: boolean) => void }) {
  return (
    <label className="note" style={{ display: "block", marginTop: 32 }}>
      <input type="checkbox" checked={empty} onChange={(e) => setEmpty(e.target.checked)} /> Prototype: show the empty state
    </label>
  );
}

function TickerBox({ compact }: { compact?: boolean }) {
  return (
    <form className="ticker-entry" onSubmit={(e) => e.preventDefault()} style={compact ? { marginTop: 16 } : undefined}>
      <label htmlFor="ticker">{compact ? "Project another ticker" : "Ticker"}</label>
      <div className="line">
        <input className="ticker" id="ticker" placeholder="e.g. AAPL" autoComplete="off" maxLength={10} />
        <button className="btn" type="submit">
          Project
        </button>
      </div>
    </form>
  );
}

function Cover() {
  return (
    <>
      <h1>Compass</h1>
      <div className="cover-rule" role="presentation" />
      <p className="one-line">
        A stock projection you can argue with: pre-filled from the company's own SEC filings, edited by you, shown as a
        Bear / Base / Bull range.
      </p>
    </>
  );
}

function SpecimenStub() {
  return (
    <section className="mt-8">
      <div className="caption">
        <span>Specimen</span>
        <span className="units">Illustrative figures for Apple Inc., not a prediction</span>
      </div>
      <p className="note py-6 text-center">(the existing specimen table, unchanged)</p>
    </section>
  );
}

function ListTable({ rows, limit, onDelete }: { rows: Row[]; limit?: number; onDelete?: (t: string) => void }) {
  const shown = limit ? rows.slice(0, limit) : rows;
  return (
    <table className="statement-table">
      <thead>
        <tr>
          <th scope="col" style={{ textAlign: "left" }}>
            Company
          </th>
          <th scope="col" className="src-col">
            Scenarios
          </th>
          <th scope="col">Saved</th>
          {onDelete && (
            <th scope="col">
              <span className="sr-only">Actions</span>
            </th>
          )}
        </tr>
      </thead>
      <tbody>
        {shown.map((r) => (
          <tr key={r.ticker}>
            <th scope="row" className="label">
              <Link to={open(r.ticker)} style={{ fontWeight: 600 }}>
                {r.ticker}
              </Link>
              <small>{r.company}</small>
            </th>
            <td className="src-col note num">{r.scenarios.join(" · ")}</td>
            <td className="num text-sm">{r.saved}</td>
            {onDelete && (
              <td className="num">
                <button type="button" className="link text-sm" onClick={() => onDelete(r.ticker)}>
                  Delete
                </button>
              </td>
            )}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function EmptyLine() {
  return (
    <p className="note py-6">
      No saved projections yet. Type a ticker above and press <b>Save projection</b> on the calculator to keep one here.
    </p>
  );
}

/* ---------- Landing ---------- */

function LandingA({ rows }: { rows: Row[] }) {
  return (
    <main>
      <section className="cover">
        <Cover />
        <TickerBox />
      </section>
      <section>
        <div className="caption">
          <span>Your projections</span>
          <span className="units">{rows.length > 5 && <Link to="/prototype/projections">All {rows.length} →</Link>}</span>
        </div>
        {rows.length ? <ListTable rows={rows} limit={5} /> : <EmptyLine />}
      </section>
      <SpecimenStub />
    </main>
  );
}

function LandingB({ rows }: { rows: Row[] }) {
  return (
    <main>
      <h1>Your projections</h1>
      <TickerBox compact />
      {rows.length ? (
        <>
          <ListTable rows={rows} />
          <p className="note mt-4">
            <Link to="/prototype/projections">Manage projections</Link> to delete one.
          </p>
        </>
      ) : (
        <>
          <EmptyLine />
          <SpecimenStub />
        </>
      )}
    </main>
  );
}

function LandingC({ rows }: { rows: Row[] }) {
  return (
    <main>
      <section className="cover">
        <Cover />
        <TickerBox />
        {rows.length > 0 && (
          <p className="text-sm" style={{ display: "flex", flexWrap: "wrap", gap: "0.25rem 1rem", alignItems: "baseline" }}>
            <span className="text-ink-2">Your projections:</span>
            {rows.slice(0, 6).map((r) => (
              <Link key={r.ticker} to={open(r.ticker)} title={`${r.company}, saved ${r.saved}`} style={{ fontWeight: 600, letterSpacing: "0.04em" }}>
                {r.ticker}
              </Link>
            ))}
            <Link to="/prototype/projections">{rows.length > 6 ? `all ${rows.length} →` : "manage →"}</Link>
          </p>
        )}
      </section>
      <SpecimenStub />
    </main>
  );
}

export function LandingListPrototype() {
  const variant = useVariant(["A", "B", "C"]);
  const { rows, empty, setEmpty } = useRows();
  return (
    <>
      {variant === "A" && <LandingA rows={rows} />}
      {variant === "B" && <LandingB rows={rows} />}
      {variant === "C" && <LandingC rows={rows} />}
      <EmptyToggle empty={empty} setEmpty={setEmpty} />
      <PrototypeSwitcher
        variants={[
          { key: "A", name: "Table under the ticker box" },
          { key: "B", name: "Projections lead the page" },
          { key: "C", name: "Ticker strip under the box" },
        ]}
      />
    </>
  );
}

/* ---------- /projections page ---------- */

function ConfirmDelete({ row, onKeep, onDelete }: { row: Row; onKeep: () => void; onDelete: () => void }) {
  return (
    <div className="modal-backdrop" onClick={(e) => e.target === e.currentTarget && onKeep()}>
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby="del-title">
        <h2 id="del-title">Delete your projection for {row.ticker}?</h2>
        <p>
          Your {row.scenarios.join(", ")} assumptions for {row.company}, saved {row.saved}, will be gone for good. There is
          no undo.
        </p>
        <div className="actions">
          <button autoFocus className="btn btn-quiet" type="button" onClick={onKeep}>
            Keep it
          </button>
          <button className="btn" type="button" onClick={onDelete}>
            Delete
          </button>
        </div>
      </div>
    </div>
  );
}

function PageB({ rows, ask }: { rows: Row[]; ask: (t: string) => void }) {
  return (
    <ol className="mt-4">
      {rows.map((r) => (
        <li key={r.ticker} className="border-b border-hairline py-4">
          <div className="flex items-baseline justify-between gap-4">
            <Link to={open(r.ticker)} className="text-lg font-semibold" style={{ textDecoration: "none", color: "var(--ink)" }}>
              {r.company} <span className="tag">{r.ticker}</span>
            </Link>
            <span className="text-sm text-ink-2 whitespace-nowrap">Saved {r.saved}</span>
          </div>
          <div className="flex items-baseline justify-between gap-4 mt-1 text-sm">
            <span className="text-ink-2">
              {r.scenarios.length} scenario{r.scenarios.length > 1 ? "s" : ""}: {r.scenarios.join(", ")}
            </span>
            <span className="flex gap-4">
              <Link to={open(r.ticker)}>Open</Link>
              <button type="button" className="link" onClick={() => ask(r.ticker)}>
                Delete
              </button>
            </span>
          </div>
        </li>
      ))}
    </ol>
  );
}

export function ProjectionsPagePrototype() {
  const variant = useVariant(["A", "B"]);
  const { rows, remove, empty, setEmpty } = useRows();
  const [asking, setAsking] = useState<string | null>(null);
  const [gone, setGone] = useState<string | null>(null);
  const row = rows.find((r) => r.ticker === asking);
  return (
    <main>
      <h1>Your projections</h1>
      <p className="note">
        {rows.length} saved, newest first. Opening one fetches today's filed figures; only your assumptions are stored.
      </p>
      {gone && (
        <p role="status" className="statement-note">
          Deleted your projection for {gone}.
        </p>
      )}
      {rows.length === 0 ? (
        <p className="note py-6">
          No saved projections yet. <Link to="/prototype/landing">Type a ticker</Link> to start one.
        </p>
      ) : variant === "A" ? (
        <ListTable rows={rows} onDelete={setAsking} />
      ) : (
        <PageB rows={rows} ask={setAsking} />
      )}
      {row && (
        <ConfirmDelete
          row={row}
          onKeep={() => setAsking(null)}
          onDelete={() => {
            remove(row.ticker);
            setGone(row.ticker);
            setAsking(null);
          }}
        />
      )}
      <EmptyToggle empty={empty} setEmpty={setEmpty} />
      <PrototypeSwitcher
        variants={[
          { key: "A", name: "Statement table" },
          { key: "B", name: "Ledger entries" },
        ]}
      />
    </main>
  );
}

/* ---------- Round 2: how "deleted" is announced (list layout B won) ---------- */

function Toast({ text, onDone }: { text: string; onDone: () => void }) {
  useEffect(() => {
    const t = setTimeout(onDone, 5000);
    return () => clearTimeout(t);
  }, [onDone]);
  return (
    <div role="status" style={{ position: "fixed", bottom: 72, right: 24, background: "var(--ink)", color: "var(--paper)", padding: "0.75rem 1rem", boxShadow: "0 6px 20px rgba(0,0,0,.25)", display: "flex", gap: 16, alignItems: "baseline", zIndex: 900 }} className="text-sm">
      {text}
      <button type="button" onClick={onDone} aria-label="Dismiss" style={{ color: "inherit" }}>
        ×
      </button>
    </div>
  );
}

export function DeleteMessagePrototype() {
  const style = useVariant(["M1", "M2", "M3", "M4"]);
  const [params] = useSearchParams();
  const { rows, remove } = useRows();
  const [asking, setAsking] = useState<string | null>(null);
  // A delete from the calculator lands here with ?deleted=TICKER.
  const [gone, setGone] = useState<string | null>(params.get("deleted"));
  const row = rows.find((r) => r.ticker === asking);
  const text = gone ? `Deleted your projection for ${gone}.` : "";
  const shown = rows.filter((r) => r.ticker !== params.get("deleted"));
  return (
    <main>
      {style === "M3" && gone && (
        <p role="status" className="text-sm" style={{ margin: "1rem 0 0", padding: "0.6rem 0.75rem", borderLeft: "3px solid var(--pencil)", background: "var(--pencil-tint)", display: "flex", justifyContent: "space-between" }}>
          {text}
          <button type="button" className="link" onClick={() => setGone(null)}>
            Dismiss
          </button>
        </p>
      )}
      <h1>Your projections</h1>
      <p className="note">{rows.length} saved, newest first.</p>
      {style === "M1" && gone && (
        <p role="status" className="statement-note">
          {text}
        </p>
      )}
      <ol className="mt-4">
        {style === "M4" && gone && (
          <li role="status" className="border-b border-hairline py-4 text-sm text-ink-2" style={{ fontStyle: "italic" }}>
            {gone} — deleted. Your assumptions for it are gone.
          </li>
        )}
        {shown.map((r) => (
          <li key={r.ticker} className="border-b border-hairline py-4">
            <div className="flex items-baseline justify-between gap-4">
              <Link to={open(r.ticker)} className="text-lg font-semibold" style={{ textDecoration: "none", color: "var(--ink)" }}>
                {r.company} <span className="tag">{r.ticker}</span>
              </Link>
              <span className="text-sm text-ink-2 whitespace-nowrap">Saved {r.saved}</span>
            </div>
            <div className="flex items-baseline justify-between gap-4 mt-1 text-sm">
              <span className="text-ink-2">{r.scenarios.join(", ")}</span>
              <span className="flex gap-4">
                <Link to={open(r.ticker)}>Open</Link>
                <button type="button" className="link" onClick={() => setAsking(r.ticker)}>
                  Delete
                </button>
              </span>
            </div>
          </li>
        ))}
      </ol>
      {style === "M2" && gone && <Toast text={text} onDone={() => setGone(null)} />}
      {row && (
        <ConfirmDelete
          row={row}
          onKeep={() => setAsking(null)}
          onDelete={() => {
            remove(row.ticker);
            setGone(row.ticker);
            setAsking(null);
          }}
        />
      )}
      <p className="note mt-6">
        Prototype: <Link to={`?variant=${style}&deleted=AAPL`} reloadDocument>simulate arriving from the calculator after deleting AAPL</Link>
      </p>
      <PrototypeSwitcher
        variants={[
          { key: "M1", name: "Inline note under the heading" },
          { key: "M2", name: "Toast, gone after 5 s" },
          { key: "M3", name: "Dismissable banner at the top" },
          { key: "M4", name: "Tombstone row in the list" },
        ]}
      />
    </main>
  );
}
