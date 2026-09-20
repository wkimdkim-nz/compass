import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router";
import { OutputRows } from "../projection/OutputRows";

/** The specimen's outputs, Bear / Base / Bull, printed as the calculator prints its own. */
const SPECIMEN = [
  { x: "0.7", cagr: "(6) %" },
  { x: "1.2", cagr: "4 %" },
  { x: "1.9", cagr: "14 %" },
] as const;

/**
 * The statement's cover block: what Compass does, and the ticker box. It works signed out: the
 * calculator's route is guarded, and sign in carries the visitor on to it.
 */
export function LandingPage() {
  const [ticker, setTicker] = useState("");
  const navigate = useNavigate();

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const typed = ticker.trim().toUpperCase();
    if (typed) navigate(`/projections/${encodeURIComponent(typed)}`);
  };

  return (
    <main>
      <section className="cover" aria-labelledby="cover-title">
        <h1 id="cover-title">Compass</h1>
        <div className="cover-rule" role="presentation" />
        <p className="one-line">
          A stock projection you can argue with: pre-filled from the company's own SEC filings, edited by you, shown
          as a Bear / Base / Bull range.
        </p>
        <p className="note">
          Figures in millions, except per-share data. US companies filing 10-K and 10-Q reports with the SEC.
        </p>

        <form className="ticker-entry" onSubmit={submit}>
          <label htmlFor="ticker">Ticker</label>
          <div className="line">
            <input
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
              <td className="num">4</td>
              <td className="num">8</td>
              <td className="num">12</td>
            </tr>
            <tr>
              <th scope="row" className="label">
                Exit multiple <small>P/E</small>
              </th>
              <td className="num">22</td>
              <td className="num">28</td>
              <td className="num">34</td>
            </tr>
            <OutputRows columns={SPECIMEN} showFuturePrice={false} />
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
