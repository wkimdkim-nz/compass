import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import type { ProjectionOutputs } from "../../projection/calculator";
import { Disclaimer } from "../../app/Disclaimer";
import { fieldErrors } from "../auth/problemDetails";
import { ConfirmReplaceDialog } from "./ConfirmReplaceDialog";
import { formatOutputs } from "./figures";
import {
  useCreateProjectionMutation,
  useReplaceProjectionMutation,
  type BaseFactsResponse,
  type ExitMultipleKind,
  type SavedProjectionResponse,
  type SavedScenario,
  type ScenarioName,
  type SeededScenario,
} from "./projectionApi";
import {
  buildForm,
  preview,
  saveDraft,
  SCENARIO_NAMES,
  type BaseFactsForm,
  type ProjectionForm,
  type ScenarioField,
} from "./projectionForm";

type Props = {
  company: { ticker: string; companyName: string; cik: number | null };
  /** Fresh from the lookup; absent when it could not pre-fill. */
  baseFacts: BaseFactsResponse | undefined;
  /** The lookup's Bear / Base / Bull defaults; empty when it had none to give. */
  seeded: SeededScenario[];
  /** The user's saved projection for this company, and the day it was saved. */
  saved: { scenarios: SavedScenario[]; on: string } | undefined;
  /** The statement note printed above the base facts. */
  notice: ReactNode;
};

const TITLE: Record<ScenarioName, string> = { bear: "Bear", base: "Base", bull: "Bull" };

const FACT_ROWS: { field: keyof BaseFactsForm; label: ReactNode; aria: string; source: string; wide?: boolean; unit?: string }[] = [
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
  { field: "currentMargin", label: "Net margin", aria: "Current net margin, percent", source: "Net income ÷ revenue", unit: "%" },
];

const ASSUMPTION_ROWS: { field: ScenarioField; label: string; small: string; aria: string }[] = [
  { field: "horizon", label: "Horizon", small: "years", aria: "horizon, years" },
  { field: "growth", label: "Revenue growth", small: "% per year", aria: "revenue growth, percent per year" },
  { field: "margin", label: "Future net margin", small: "%", aria: "future net margin, percent" },
  { field: "multiple", label: "Exit multiple", small: "P/E on earnings, or P/S on revenue", aria: "exit multiple" },
  { field: "dilution", label: "Share dilution", small: "% per year; buybacks in parentheses", aria: "share dilution, percent per year" },
];

const KINDS: { kind: ExitMultipleKind; label: string }[] = [
  { kind: "pe", label: "P/E" },
  { kind: "ps", label: "P/S" },
];

/** How long the output rows stay tinted after an edit, so the chain from a guess to the total shows. */
const TINT_MS = 700;

type ServerOutputs = Partial<Record<ScenarioName, ProjectionOutputs>>;

const outputsByName = (response: SavedProjectionResponse): ServerOutputs =>
  Object.fromEntries(response.scenarios.map((s) => [s.name, s.outputs]));

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
  const [savedHere, setSavedHere] = useState(false);
  const [serverOutputs, setServerOutputs] = useState<ServerOutputs | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [problems, setProblems] = useState<string[]>([]);
  const [tinted, setTinted] = useState(false);

  const [create, creating] = useCreateProjectionMutation();
  const [replace, replacing] = useReplaceProjectionMutation();
  const busy = creating.isLoading || replacing.isLoading;
  const exists = saved !== undefined || savedHere;

  const results = useMemo(() => preview(form), [form]);

  useEffect(() => {
    if (!tinted) return;
    const timer = setTimeout(() => setTinted(false), TINT_MS);
    return () => clearTimeout(timer);
  }, [tinted, form]);

  const edit = (next: ProjectionForm) => {
    setForm(next);
    setServerOutputs(null);
    setProblems([]);
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

  const save = async () => {
    setConfirming(false);
    const draft = saveDraft(form, company);
    if (!draft.ready) {
      setProblems([
        draft.unfinished.length > 0
          ? `Finish or clear the ${draft.unfinished.map((name) => TITLE[name]).join(" and ")} column before saving: every figure in it, and the base facts, must give a result.`
          : "Fill in at least one scenario before saving.",
      ]);
      return;
    }

    const result = await (exists ? replace : create)(draft.request);
    if ("error" in result) {
      const error = result.error as { status?: unknown; data?: { detail?: string } };
      // A 401 has already sent the app back to sign in (see the store); there is nothing to say here.
      if (error.status === 401) return;
      const fields = Object.values(fieldErrors(result.error)).flat();
      setProblems(fields.length > 0 ? fields : [error.data?.detail ?? "The projection could not be saved just now. Try again shortly."]);
      return;
    }
    setServerOutputs(outputsByName(result.data));
    // What was saved is now what an edit is measured against.
    setOpened(form);
    setSavedHere(true);
    setRestated(false);
    setProblems([]);
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (exists) setConfirming(true);
    else void save();
  };

  const shown = (name: ScenarioName) => {
    const outputs = serverOutputs?.[name] ?? (results[name].status === "ok" ? results[name].outputs : undefined);
    return outputs && formatOutputs(outputs);
  };
  const columns = SCENARIO_NAMES.map((name) => ({ name, outputs: shown(name) }));
  const blank = "—";

  return (
    <form onSubmit={submit} noValidate>
      {saved && !restated && (
        <p className="saved">
          <span>
            Your assumptions, saved <b>{saved.on}</b>, over today's figures
          </span>
          <button type="button" className="link" onClick={restate}>
            Restate to defaults
          </button>
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
            {FACT_ROWS.map(({ field, label, aria, source, wide, unit }) => {
              const input = (
                <input
                  className={`fig${wide ? " wide" : ""}${form.facts[field] !== opened.facts[field] ? " edited" : ""}`}
                  value={form.facts[field]}
                  onChange={(e) => editFact(field, e.target.value)}
                  inputMode="decimal"
                  placeholder={blank}
                  autoComplete="off"
                  aria-label={aria}
                />
              );
              return (
                <tr key={field}>
                  <th scope="row" className="label">
                    {label} <small className="src">{source}</small>
                  </th>
                  <td className="src-col note">{source}</td>
                  <td className="num">
                    {unit ? (
                      <span className="with-unit">
                        {input}
                        <span className="unit">{unit}</span>
                      </span>
                    ) : (
                      input
                    )}
                  </td>
                </tr>
              );
            })}
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
                  {TITLE[name]}
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
                      placeholder={blank}
                      autoComplete="off"
                      aria-label={`${TITLE[name]} ${aria}`}
                    />
                    {field === "multiple" && (
                      <span className="basis" role="radiogroup" aria-label={`${TITLE[name]} exit multiple basis`}>
                        {KINDS.map(({ kind, label: kindLabel }) => (
                          <label key={kind}>
                            <input
                              type="radio"
                              name={`basis-${name}`}
                              checked={form.scenarios[name].kind === kind}
                              onChange={() => editScenario(name, { kind })}
                              aria-label={`${TITLE[name]} exit multiple on ${kindLabel}`}
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
            <tr className={`subtotal-rule total${tinted ? " changed" : ""}`}>
              <th scope="row" className="label">
                Return multiple
              </th>
              {columns.map(({ name, outputs }) => (
                <td key={name} className="num x">
                  {outputs ? (
                    <>
                      {outputs.x}
                      <sup>×</sup>
                    </>
                  ) : (
                    blank
                  )}
                </td>
              ))}
            </tr>
            <tr className="double-rule" aria-hidden="true">
              <td colSpan={4} />
            </tr>
            <tr className={tinted ? "changed" : undefined}>
              <th scope="row" className="label">
                CAGR <small>% per year</small>
              </th>
              {columns.map(({ name, outputs }) => (
                <td key={name} className="num">
                  {outputs?.cagr ?? blank}
                </td>
              ))}
            </tr>
            <tr className={tinted ? "changed" : undefined}>
              <th scope="row" className="label">
                Future share price <small>USD</small>
              </th>
              {columns.map(({ name, outputs }) => (
                <td key={name} className="num">
                  {outputs?.futurePrice ?? blank}
                </td>
              ))}
            </tr>
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
            Saved. The outputs above are the server's own calculation.
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
          onReplace={() => void save()}
        />
      )}
    </form>
  );
}
