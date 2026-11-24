import { api } from "../../app/api";
import type { ProjectionOutputs } from "../../projection/calculator";

// The wire shapes of the ticker lookup and the projection endpoints (README, "The API"). Raw
// values and decimal rates throughout: millions and percents are `figures.ts`'s.

export type ScenarioName = "bear" | "base" | "bull";
export type ExitMultipleKind = "pe" | "ps";

/** The glossary's Company. The CIK is null when no lookup has named it. */
export type Company = { ticker: string; companyName: string; cik: number | null };

export type BaseFactsResponse = {
  /** Null when the quote source failed; `missing` then names it. */
  currentPrice: number | null;
  ttmRevenue: number;
  dilutedShares: number;
  currentNetMargin: number | null;
  /** The period end the TTM figures run to, as `YYYY-MM-DD`. */
  figuresTo: string;
};

export type SeededScenario = {
  name: ScenarioName;
  growthRate: number;
  futureNetMargin: number;
  /** The value is null when there was no price to work it out from. */
  exitMultiple: { kind: ExitMultipleKind; value: number | null };
  dilutionRate: number;
  horizon: number;
};

export type TickerResponse = {
  ticker: string;
  companyName: string;
  cik: number;
  baseFacts: BaseFactsResponse;
  /** Bear, Base, Bull; or none, when the filed history gave nothing sound to seed from. */
  scenarios: SeededScenario[];
  missing: string[];
};

export type SavedScenario = {
  name: ScenarioName;
  growthRate: number;
  futureNetMargin: number;
  exitMultiple: { kind: ExitMultipleKind; value: number };
  dilutionRate: number;
  horizon: number;
};

/** What a create and a replace answer with: each scenario carries the .NET calculator's outputs. */
export type SavedProjectionResponse = {
  ticker: string;
  companyName: string;
  cik: number | null;
  createdAt: string;
  updatedAt: string;
  scenarios: (SavedScenario & { outputs: ProjectionOutputs })[];
};

export type LoadedProjectionResponse = {
  ticker: string;
  companyName: string;
  cik: number | null;
  createdAt: string;
  updatedAt: string;
  scenarios: SavedScenario[];
};

/** One entry of the caller's list: no base facts and no outputs, since the list fetches neither. */
export type ProjectionSummary = {
  ticker: string;
  companyName: string;
  /** Those saved, Bear, Base, Bull in that order. */
  scenarios: ScenarioName[];
  updatedAt: string;
};

export type SaveProjectionRequest = Company & {
  baseFacts: { currentPrice: number | null; ttmRevenue: number | null; dilutedShares: number | null };
  scenarios: SavedScenario[];
};

export const projectionApi = api.injectEndpoints({
  endpoints: (build) => ({
    /** Untagged: nothing the user does changes what a company filed. */
    lookUpTicker: build.query<TickerResponse, string>({
      query: (ticker) => `/tickers/${encodeURIComponent(ticker)}`,
    }),

    listProjections: build.query<ProjectionSummary[], void>({
      query: () => "/projections",
      providesTags: ["Projection"],
    }),

    /** The saved projection, or null: this route's 404 means only "nothing saved", which is an answer. */
    getProjection: build.query<LoadedProjectionResponse | null, string>({
      query: (ticker) => ({
        url: `/projections/${encodeURIComponent(ticker)}`,
        validateStatus: (response) => response.ok || response.status === 404,
      }),
      transformResponse: (body: LoadedProjectionResponse, meta) => (meta?.response?.status === 404 ? null : body),
      providesTags: ["Projection"],
    }),

    createProjection: build.mutation<SavedProjectionResponse, SaveProjectionRequest>({
      query: (body) => ({ url: "/projections", method: "POST", body }),
      invalidatesTags: ["Projection"],
    }),

    /** A second verb, so that only a user who confirmed can overwrite (ADR 0004, amended). */
    replaceProjection: build.mutation<SavedProjectionResponse, SaveProjectionRequest>({
      query: (body) => ({ url: `/projections/${encodeURIComponent(body.ticker)}`, method: "PUT", body }),
      invalidatesTags: ["Projection"],
    }),
  }),
});

export const {
  useLookUpTickerQuery,
  useListProjectionsQuery,
  useGetProjectionQuery,
  useCreateProjectionMutation,
  useReplaceProjectionMutation,
} = projectionApi;
