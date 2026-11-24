import { Link } from "react-router";
import { calculatorPath, TICKER_BOX_PATH } from "../../app/paths";
import { printDate } from "./dates";
import { useListProjectionsQuery } from "./projectionApi";
import { SCENARIO_TITLE } from "./projectionForm";

export function ProjectionsPage() {
  const { data: projections, isLoading, isError, refetch } = useListProjectionsQuery();

  return (
    <main>
      <h1>My projections</h1>
      <p className="note">Opening one fetches today's filed figures; only your assumptions are stored.</p>
      {isLoading ? (
        <p role="status" className="note py-6">
          Loading your projections…
        </p>
      ) : isError || !projections ? (
        <div role="alert" className="statement-note">
          <p>
            Your projections could not be loaded just now.{" "}
            <button type="button" className="link" onClick={() => refetch()}>
              Try again
            </button>
          </p>
        </div>
      ) : projections.length === 0 ? (
        <p className="note py-6">
          No saved projections yet. <Link to={TICKER_BOX_PATH}>Type a ticker</Link> to start one.
        </p>
      ) : (
        <ol className="projection-list" aria-label="Saved projections">
          {projections.map((projection) => (
            <li key={projection.ticker}>
              <div className="projection-line">
                <Link to={calculatorPath(projection.ticker)} className="projection-name">
                  {projection.companyName} <span className="tag">{projection.ticker}</span>
                </Link>
                <span className="note whitespace-nowrap">Saved {printDate(projection.updatedAt, "local")}</span>
              </div>
              <div className="projection-line text-sm">
                <span className="text-ink-2">{projection.scenarios.map((name) => SCENARIO_TITLE[name]).join(", ")}</span>
                <Link to={calculatorPath(projection.ticker)} aria-label={`Open ${projection.ticker}`}>
                  Open
                </Link>
              </div>
            </li>
          ))}
        </ol>
      )}
    </main>
  );
}
