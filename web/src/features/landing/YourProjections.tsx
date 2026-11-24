import { Link } from "react-router";
import { calculatorPath } from "../../app/paths";
import { useGetMeQuery } from "../auth/authApi";
import { printDate } from "../projection/dates";
import { useListProjectionsQuery } from "../projection/projectionApi";
import { SCENARIO_TITLE } from "../projection/projectionForm";

const SHOWN = 5;

/**
 * The signed-in user's newest Projections. Nothing renders until there is a list with something
 * in it: signed out, empty, still loading or failed, the landing page reads as it does signed out
 * and does not jump. A list that cannot load says so on My projections, not here.
 */
export function YourProjections() {
  const { data: me } = useGetMeQuery();
  const { data: projections } = useListProjectionsQuery(undefined, { skip: !me });

  if (!me || !projections?.length) return null;

  return (
    <section aria-labelledby="your-projections">
      <div className="caption">
        <span id="your-projections">Your projections</span>
        {projections.length > SHOWN && (
          <span className="units">
            <Link to="/projections">All {projections.length} →</Link>
          </span>
        )}
      </div>
      <table className="statement-table" aria-labelledby="your-projections">
        <thead>
          <tr>
            <th scope="col">Company</th>
            <th scope="col">Scenarios</th>
            <th scope="col">Saved</th>
          </tr>
        </thead>
        <tbody>
          {projections.slice(0, SHOWN).map((projection) => (
            <tr key={projection.ticker}>
              <th scope="row" className="label">
                <Link to={calculatorPath(projection.ticker)} className="font-semibold">
                  {projection.ticker}
                </Link>
                <small className="wrap-anywhere">{projection.companyName}</small>
              </th>
              <td className="num text-sm text-ink-2">{projection.scenarios.map((name) => SCENARIO_TITLE[name]).join(", ")}</td>
              <td className="num text-sm">{printDate(projection.updatedAt, "local")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
