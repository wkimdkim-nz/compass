import { useEffect } from "react";
import { useLocation } from "react-router";
import { WorkedExample } from "./WorkedExample";
import { GUIDE_ENTRIES, type GuideEntry } from "./guideEntries";

const GROUPS: { group: GuideEntry["group"]; title: string; units: string }[] = [
  { group: "fact", title: "Base facts", units: "Looked up, not guessed" },
  { group: "assumption", title: "Assumptions", units: "Your guesses, pre-filled from filed history" },
  { group: "output", title: "Outputs", units: "Worked out from the two above" },
];

/** Every figure on the calculator, what it means, and where to look it up. Public. */
export function GuidePage() {
  const target = decodeURIComponent(useLocation().hash.slice(1));

  useEffect(() => {
    if (target) document.getElementById(target)?.scrollIntoView();
  }, [target]);

  return (
    <main>
      <h1>Guide</h1>
      <p className="note guide-intro">
        Every figure on the calculator: what it means, how to choose it, and where to look it up yourself. Compass
        pre-fills the base facts from the company's SEC filings and a market quote, and drafts the assumptions from its
        filed history. Change a figure when you expect the future to differ from the past.
      </p>

      {GROUPS.map(({ group, title, units }) => (
        <section key={group} aria-labelledby={`guide-${group}`}>
          <div className="caption">
            <span id={`guide-${group}`}>{title}</span>
            <span className="units">{units}</span>
          </div>
          <table className="statement-table guide-table" aria-labelledby={`guide-${group}`}>
            <thead>
              <tr>
                <th scope="col">Figure</th>
                <th scope="col">What it is, and how to choose it</th>
                <th scope="col">Find it</th>
              </tr>
            </thead>
            <tbody>
              {GUIDE_ENTRIES.filter((entry) => entry.group === group).map((entry) => (
                <EntryRow key={entry.id} entry={entry} targeted={entry.id === target} />
              ))}
            </tbody>
          </table>
        </section>
      ))}

      <WorkedExample />
    </main>
  );
}

function EntryRow({ entry, targeted }: { entry: GuideEntry; targeted: boolean }) {
  return (
    <tr id={entry.id} aria-current={targeted || undefined}>
      <th scope="row">{entry.name}</th>
      <td>
        <p>{entry.what}</p>
        <Labelled label="Anchor on" text={entry.anchor} />
        <Labelled label="Sanity check" text={entry.sanity} />
      </td>
      <td>
        {entry.sources.length > 0 && (
          <p>
            {entry.sources.map((source, i) => (
              <span key={source.href}>
                {i > 0 && ", "}
                <a href={source.href} target="_blank" rel="noopener noreferrer">
                  {source.label}
                </a>
              </span>
            ))}
          </p>
        )}
        <Labelled label="In a 10-K or 10-Q" text={entry.filing} />
      </td>
    </tr>
  );
}

function Labelled({ label, text }: { label: string; text?: string }) {
  if (!text) return null;
  return (
    <p>
      <b>{label}:</b> {text}
    </p>
  );
}
