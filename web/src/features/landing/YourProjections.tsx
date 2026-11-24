import { Link } from "react-router";
import { calculatorPath, PROJECTIONS_PATH } from "../../app/paths";
import { useGetMeQuery } from "../auth/authApi";
import { printDate } from "../projection/dates";
import { useListProjectionsQuery } from "../projection/projectionApi";
import { scenarioTitles } from "../projection/projectionForm";

const NEWEST_SHOWN = 5;

/** Absent rather than loading or failed, so the landing page never jumps; My projections reports a failed list. */
export function YourProjections() {
  const { data: me } = useGetMeQuery();
  const { data: projections, isError } = useListProjectionsQuery(undefined, { skip: !me });

  if (!me || isError || !projections?.length) return null;

  return (
    <section aria-labelledby="your-projections">
      <div className="caption">
        <span id="your-projections">Your projections</span>
        {projections.length > NEWEST_SHOWN && (
          <span className="units">
            <Link to={PROJECTIONS_PATH}>All {projections.length} →</Link>
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
          {projections.slice(0, NEWEST_SHOWN).map((projection) => (
            <tr key={projection.ticker}>
              <th scope="row" className="label">
                <Link to={calculatorPath(projection.ticker)} className="font-semibold">
                  {projection.ticker}
                </Link>
                <small className="wrap-anywhere">{projection.companyName}</small>
              </th>
              <td className="text-right text-sm whitespace-nowrap text-ink-2">{scenarioTitles(projection.scenarios)}</td>
              <td className="num text-sm">{printDate(projection.updatedAt, "local")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
