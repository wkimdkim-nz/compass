import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { Disclaimer } from "../../app/Disclaimer";
import { ConfirmReplaceDialog } from "./ConfirmReplaceDialog";
import { BLANK, formatOutputs } from "./figures";
import { OutputRows } from "./OutputRows";
import {
  type BaseFactsResponse,
  type Company,
  type ExitMultipleKind,
  type SavedScenario,
  type ScenarioName,
  type SeededScenario,
} from "./projectionApi";
import {
  buildForm,
  currentMarginText,
  preview,
  SCENARIO_NAMES,
  SCENARIO_TITLE,
  type BaseFactsForm,
  type ProjectionForm,
  type ScenarioField,
} from "./projectionForm";
import { useSaveProjection } from "./useSaveProjection";

type Props = {
  company: Company;
  /** Fresh from the lookup; absent when it could not pre-fill. */
  baseFacts: BaseFactsResponse | undefined;
  /** The lookup's Bear / Base / Bull defaults; empty when it had none to give. */
  seeded: SeededScenario[];
  /** The user's saved projection for this company, and the day it was saved. */
  saved: { scenarios: SavedScenario[]; on: string } | undefined;
  /** The statement note printed above the base facts. */
  notice: ReactNode;
};

const FACT_ROWS: { field: keyof BaseFactsForm; label: ReactNode; aria: string; source: string; wide?: boolean }[] = [
  { field: "price", label: "Share price", aria: "Share price", source: "Finnhub, per share" },
  {
    field: "revenue",
    label: (
      <>
        Revenue<span className="long">, trailing twelve months</span>
        <span className="short"> TTM</span>
      </>
    ),
    aria: "Revenue, trailing twelve months, millions",
    source: "SEC EDGAR, TTM",
    wide: true,
  },
  {
    field: "shares",
    label: (
      <>
        Shares <span className="long">outstanding, </span>diluted
      </>
    ),
    aria: "Shares outstanding, diluted, millions",
    source: "SEC EDGAR, weighted average",
    wide: true,
  },
];

const ASSUMPTION_ROWS: { field: ScenarioField; label: string; small: string; aria: string }[] = [
  { field: "horizon", label: "Horizon", small: "years", aria: "horizon, years" },
  { field: "growthRate", label: "Revenue growth", small: "% per year", aria: "revenue growth, percent per year" },
  { field: "futureNetMargin", label: "Future net margin", small: "%", aria: "future net margin, percent" },
  { field: "exitMultiple", label: "Exit multiple", small: "P/E on earnings, or P/S on revenue", aria: "exit multiple" },
  { field: "dilutionRate", label: "Share dilution", small: "% per year; buybacks in parentheses", aria: "share dilution, percent per year" },
];

const KINDS: { kind: ExitMultipleKind; label: string }[] = [
  { kind: "pe", label: "P/E" },
  { kind: "ps", label: "P/S" },
];

/** How long the output rows hold their tint after an edit, so the chain from a guess to the total shows; the fade after it is the stylesheet's. */
const TINT_MS = 700;

/**
 * The statement: base facts, the assumptions table with its output rows, and the save. Every
 * keystroke recomputes each column with the TypeScript calculator; a save shows the .NET
 * calculator's outputs in their place until the next edit.
 */
export function CalculatorForm({ company, baseFacts, seeded, saved, notice }: Props) {
  // What the page opened with: the figures an edit is measured against. Restating moves it to the defaults.
  const [opened, setOpened] = useState(() => buildForm(baseFacts, saved?.scenarios ?? seeded));
  const [form, setForm] = useState(opened);
  const [restated, setRestated] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [tinted, setTinted] = useState(false);

  const { save, noteEdit, exists, busy, serverOutputs, problems } = useSaveProjection({
    company,
    alreadySaved: saved !== undefined,
    // What was saved is now what an edit is measured against.
    onSaved: (sent) => {
      setOpened(sent);
      setRestated(false);
    },
  });

  const results = useMemo(() => preview(form), [form]);

  useEffect(() => {
    if (!tinted) return;
    const timer = setTimeout(() => setTinted(false), TINT_MS);
    return () => clearTimeout(timer);
  }, [tinted, form]);

  const edit = (next: ProjectionForm) => {
    noteEdit();
    setForm(next);
    setTinted(true);
  };
  const editFact = (field: keyof BaseFactsForm, text: string) => edit({ ...form, facts: { ...form.facts, [field]: text } });
  const editScenario = (name: ScenarioName, change: Partial<ProjectionForm["scenarios"][ScenarioName]>) =>
    edit({ ...form, scenarios: { ...form.scenarios, [name]: { ...form.scenarios[name], ...change } } });

  const restate = () => {
    const defaults = buildForm(baseFacts, seeded);
    // The base facts on the page stay: only the assumptions go back to what the filings seed.
    const next = { facts: form.facts, scenarios: defaults.scenarios };
    setOpened({ facts: opened.facts, scenarios: defaults.scenarios });
    edit(next);
    setRestated(true);
  };

  const confirmReplace = () => {
    setConfirming(false);
    void save(form);
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (exists) setConfirming(true);
    else void save(form);
  };

  const shown = (name: ScenarioName) => {
    const outputs = serverOutputs?.[name] ?? (results[name].status === "ok" ? results[name].outputs : undefined);
    return outputs && formatOutputs(outputs);
  };
  const currentMargin = currentMarginText(baseFacts);

  return (
    <form onSubmit={submit} noValidate>
      {saved && !restated && (
        <p className="saved">
          <span>
            Your assumptions, saved <b>{saved.on}</b>
            {/* Only when the lookup brought figures: a failed one leaves nothing for them to sit over. */}
            {baseFacts && ", over today's figures"}
          </span>
          {/* Offered only when there are defaults to restate to: a lookup that failed seeds none. */}
          {seeded.length > 0 && (
            <button type="button" className="link" onClick={restate}>
              Restate to defaults
            </button>
          )}
        </p>
      )}
      {notice && <div className="statement-note">{notice}</div>}

      <section aria-labelledby="facts">
        <div className="caption">
          <span id="facts">Base facts</span>
          <span className="units">USD millions, except per share</span>
        </div>
        <table className="statement-table" aria-labelledby="facts">
          <thead>
            <tr>
              <th scope="col">Figure</th>
              <th scope="col" className="src-col">
                Source
              </th>
              <th scope="col">Today</th>
            </tr>
          </thead>
          <tbody>
            {FACT_ROWS.map(({ field, label, aria, source, wide }) => (
              <tr key={field}>
                <th scope="row" className="label">
                  {label} <small className="src">{source}</small>
                </th>
                <td className="src-col note">{source}</td>
                <td className="num">
                  <input
                    className={`fig${wide ? " wide" : ""}${form.facts[field] !== opened.facts[field] ? " edited" : ""}`}
                    value={form.facts[field]}
                    onChange={(e) => editFact(field, e.target.value)}
                    inputMode="decimal"
                    placeholder={BLANK}
                    autoComplete="off"
                    aria-label={aria}
                  />
                </td>
              </tr>
            ))}
            {/* Printed, not typed: no formula takes it. It is there to set the future net margin against. */}
            <tr>
              <th scope="row" className="label">
                Net margin <small className="src">Net income ÷ revenue</small>
              </th>
              <td className="src-col note">Net income ÷ revenue</td>
              <td className="num">
                {currentMargin ? (
                  <>
                    {currentMargin} <span className="unit">%</span>
                  </>
                ) : (
                  BLANK
                )}
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <section aria-labelledby="assumptions">
        <div className="caption">
          <span id="assumptions">Assumptions</span>
          <span className="units">Your guesses; the first draft comes from filed history</span>
        </div>
        <table className="statement-table" aria-labelledby="assumptions">
          <thead>
            <tr>
              <th scope="col">Input</th>
              {SCENARIO_NAMES.map((name) => (
                <th key={name} scope="col">
                  {SCENARIO_TITLE[name]}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {ASSUMPTION_ROWS.map(({ field, label, small, aria }) => (
              <tr key={field}>
                <th scope="row" className="label">
                  {label} <small>{small}</small>
                </th>
                {SCENARIO_NAMES.map((name) => (
                  <td key={name} className="num">
                    <input
                      className={`fig${form.scenarios[name][field] !== opened.scenarios[name][field] ? " edited" : ""}`}
                      value={form.scenarios[name][field]}
                      onChange={(e) => editScenario(name, { [field]: e.target.value })}
                      inputMode="decimal"
                      placeholder={BLANK}
                      autoComplete="off"
                      aria-label={`${SCENARIO_TITLE[name]} ${aria}`}
                    />
                    {field === "exitMultiple" && (
                      <span className="basis" role="radiogroup" aria-label={`${SCENARIO_TITLE[name]} exit multiple basis`}>
                        {KINDS.map(({ kind, label: kindLabel }) => (
                          <label key={kind}>
                            <input
                              type="radio"
                              name={`basis-${name}`}
                              checked={form.scenarios[name].kind === kind}
                              onChange={() => editScenario(name, { kind })}
                              aria-label={`${SCENARIO_TITLE[name]} exit multiple on ${kindLabel}`}
                            />
                            <span>{kindLabel}</span>
                          </label>
                        ))}
                      </span>
                    )}
                  </td>
                ))}
              </tr>
            ))}
            <OutputRows columns={{ bear: shown("bear"), base: shown("base"), bull: shown("bull") }} tinted={tinted} />
          </tbody>
        </table>
        <Disclaimer as="footnote" />
      </section>

      {problems.length > 0 && (
        <ul role="alert" className="statement-note mt-6">
          {problems.map((problem) => (
            <li key={problem}>{problem}</li>
          ))}
        </ul>
      )}

      <div className="actions">
        <button className="btn" type="submit" disabled={busy}>
          {exists ? "Update projection" : "Save projection"}
        </button>
        {serverOutputs ? (
          <span role="status" className="note">
            The outputs above are the server's own calculation.
          </span>
        ) : (
          <span className="note">Saves your assumptions only; the filed figures are fetched fresh each time.</span>
        )}
      </div>

      {confirming && (
        <ConfirmReplaceDialog
          ticker={company.ticker}
          savedOn={saved?.on}
          busy={busy}
          onKeep={() => setConfirming(false)}
          onReplace={confirmReplace}
        />
      )}
    </form>
  );
}
