import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useLocation, useNavigate } from "react-router";
import { calculatorPath, TICKER_BOX_HASH } from "../../app/paths";
import { APPLE_EXAMPLE } from "../../projection/appleExample";
import { calculateProjection } from "../../projection/calculator";
import { formatOutputs, toUi, type PrintedOutputs } from "../projection/figures";
import { OutputRows } from "../projection/OutputRows";
import type { ScenarioName } from "../projection/projectionApi";
import { SCENARIO_NAMES } from "../projection/projectionForm";

const SPECIMEN_OUTPUTS: Partial<Record<ScenarioName, PrintedOutputs>> = {};
for (const name of SCENARIO_NAMES) {
  const result = calculateProjection(APPLE_EXAMPLE.facts, APPLE_EXAMPLE.scenarios[name]);
  if (result.status === "ok") SPECIMEN_OUTPUTS[name] = formatOutputs(result.outputs);
}

/**
 * The statement's cover block: what Compass does, and the ticker box. It works signed out: the
 * calculator's route is guarded, and sign in carries the visitor on to it.
 */
export function LandingPage() {
  const [ticker, setTicker] = useState("");
  const navigate = useNavigate();
  const tickerBox = useRef<HTMLInputElement>(null);
  const { hash } = useLocation();

  // A link to the ticker box comes from within the app, where the browser does not act on the fragment itself.
  useEffect(() => {
    if (hash === TICKER_BOX_HASH) tickerBox.current?.focus();
  }, [hash]);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const typed = ticker.trim().toUpperCase();
    if (typed) navigate(calculatorPath(typed));
  };

  return (
    <main>
      <section className="cover" aria-labelledby="cover-title">
        <h1 id="cover-title">Compass</h1>
        <div className="cover-rule" role="presentation" />
        <p className="one-line">
          A stock projection you can argue with: pre-filled from the company's own SEC filings, edited by you, shown
          as a Bear / Base / Bull range. The guide explains <Link to="/guide">what every figure means</Link>.
        </p>
        <p className="note">
          Figures in millions, except per-share data. US companies filing 10-K and 10-Q reports with the SEC.
        </p>

        <form className="ticker-entry" onSubmit={submit}>
          <label htmlFor="ticker">Ticker</label>
          <div className="line">
            <input
              ref={tickerBox}
              className="ticker"
              id="ticker"
              name="ticker"
              placeholder="e.g. AAPL"
              autoComplete="off"
              autoCapitalize="characters"
              spellCheck={false}
              maxLength={10}
              required
              value={ticker}
              onChange={(e) => setTicker(e.target.value)}
            />
            <button className="btn" type="submit">
              Project
            </button>
          </div>
        </form>
        <p className="note">Signed out? You will sign in first and land on your projection afterwards.</p>
      </section>

      <section aria-labelledby="specimen">
        <div className="caption">
          <span id="specimen">Specimen</span>
          <span className="units">Illustrative figures for Apple Inc., not a prediction</span>
        </div>
        <table className="statement-table" aria-labelledby="specimen">
          <thead>
            <tr>
              <td />
              <th scope="col">Bear</th>
              <th scope="col">Base</th>
              <th scope="col">Bull</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <th scope="row" className="label">
                Revenue growth <small>% per year</small>
              </th>
              {SCENARIO_NAMES.map((name) => (
                <td key={name} className="num">
                  {toUi(APPLE_EXAMPLE.scenarios[name].growthRate, "percent")}
                </td>
              ))}
            </tr>
            <tr>
              <th scope="row" className="label">
                Exit multiple <small>P/E</small>
              </th>
              {SCENARIO_NAMES.map((name) => (
                <td key={name} className="num">
                  {toUi(APPLE_EXAMPLE.scenarios[name].exitMultiple.value, "multiple")}
                </td>
              ))}
            </tr>
            <OutputRows columns={SPECIMEN_OUTPUTS} showFuturePrice={false} />
          </tbody>
        </table>
        <p className="footnote">
          Every figure above is a guess you can change. Compass fills the first draft from filed history and shows what
          the guesses imply.
        </p>
      </section>
    </main>
  );
}
