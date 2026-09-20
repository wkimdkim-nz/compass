import type { ReactNode } from "react";
import type { SerializedError } from "@reduxjs/toolkit";
import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import { useParams } from "react-router";
import { detail, isStatus } from "../../app/problemDetails";
import { CalculatorForm } from "./CalculatorForm";
import { useGetProjectionQuery, useLookUpTickerQuery, type TickerResponse } from "./projectionApi";

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** A date as a statement prints it, 28 Jun 2025. A period end is a calendar date, read in UTC; a moment is read in the user's own zone. */
const date = (value: string, zone: "utc" | "local") => {
  const d = new Date(value);
  return zone === "utc"
    ? `${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]} ${d.getUTCFullYear()}`
    : `${d.getDate()} ${MONTHS[d.getMonth()]} ${d.getFullYear()}`;
};

type NoticeProps = {
  ticker: string;
  /** The lookup's answer; absent when it failed. */
  found: TickerResponse | undefined;
  error: FetchBaseQueryError | SerializedError | undefined;
  hasSaved: boolean;
  retry: () => void;
};

/** The statement note above the base facts: why the lookup could not pre-fill, in plain words. */
function lookupNotice({ ticker, found, error, hasSaved, retry }: NoticeProps): ReactNode {
  const enterThem = <p>Enter the figures yourself; the projection works the same way.</p>;
  if (found) {
    return found.missing.includes("currentPrice") ? (
      <p>
        <b>The share price could not be fetched just now.</b> The exit multiples are worked out from it, so they
        are empty too; enter the price and a multiple to see the outputs.
      </p>
    ) : null;
  }
  if (isStatus(error, 404)) {
    return (
      <>
        <p>
          <b>No SEC filings found for {ticker}.</b> Compass covers US companies that file 10-K and 10-Q reports with
          the SEC.
        </p>
        {enterThem}
      </>
    );
  }
  if (isStatus(error, 422)) {
    // The lookup's own plain reason, which already says the figures can be typed in.
    const reason = detail(error);
    return reason ? (
      <p>
        <b>{reason}</b>
      </p>
    ) : (
      <>
        <p>
          <b>Compass cannot read {ticker}'s filings.</b> Only 10-K and 10-Q filers are pre-filled.
        </p>
        {enterThem}
      </>
    );
  }
  return (
    <>
      <p>
        <b>SEC EDGAR could not be reached just now.</b>{" "}
        {hasSaved ? "Your saved assumptions are loaded; the filed figures are not." : "Nothing is pre-filled."}
      </p>
      <p>
        Enter the figures yourself, or{" "}
        <button type="button" className="link" onClick={retry}>
          try again
        </button>
        .
      </p>
    </>
  );
}

/**
 * `/projections/{ticker}`: the ticker lookup and the saved projection, asked for side by side. The
 * lookup gives the fresh base facts and the seeded defaults; the projection, when there is one,
 * gives the assumptions that go over them. Whatever the lookup answers, the form is usable.
 */
export function CalculatorPage() {
  const { ticker: param = "" } = useParams();
  const typed = param.trim().toUpperCase();
  const lookup = useLookUpTickerQuery(typed);
  const projection = useGetProjectionQuery(typed);

  // `currentData`, not `data`: another ticker's figures must never stand in while this one's are fetched.
  const pending = (query: { isFetching: boolean; currentData?: unknown }) => query.isFetching && query.currentData === undefined;
  if (pending(lookup) || pending(projection)) {
    return (
      <p role="status" className="note mt-8">
        Fetching the figures for {typed}…
      </p>
    );
  }

  // Not "nothing saved", which is a null answer: offering Save here would only meet a 409.
  if (projection.isError) {
    return (
      <main>
        <h1>{typed}</h1>
        <div className="statement-note" role="alert">
          <p>
            <b>Your saved projection could not be checked just now.</b>{" "}
            <button type="button" className="link" onClick={() => projection.refetch()}>
              Try again
            </button>
          </p>
        </div>
      </main>
    );
  }

  const found = lookup.currentData;
  const saved = projection.currentData ?? undefined;
  const ticker = found?.ticker ?? saved?.ticker ?? typed;
  const companyName = found?.companyName ?? saved?.companyName;

  return (
    <main>
      <h1>
        {companyName ?? ticker} {companyName && <span className="tag">{ticker}</span>}
      </h1>
      <p className="provenance">
        {found ? (
          <>
            Figures to <b>{date(found.baseFacts.figuresTo, "utc")}</b> · Fundamentals <b>SEC EDGAR</b> · Quote{" "}
            <b>Finnhub</b>
          </>
        ) : (
          "No filed figures loaded"
        )}
      </p>
      <CalculatorForm
        // Mounted afresh when a lookup that failed comes good, so that the figures it brings fill the form.
        key={`${ticker}:${found ? "found" : "manual"}`}
        company={{ ticker, companyName: companyName ?? ticker, cik: found?.cik ?? saved?.cik ?? null }}
        baseFacts={found?.baseFacts}
        seeded={found?.scenarios ?? []}
        saved={saved && { scenarios: saved.scenarios, on: date(saved.updatedAt, "local") }}
        notice={lookupNotice({ ticker, found, error: lookup.error, hasSaved: saved !== undefined, retry: () => void lookup.refetch() })}
      />
    </main>
  );
}
