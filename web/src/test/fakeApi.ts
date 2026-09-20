import { HttpResponse, http, delay } from "msw";
import { setupServer } from "msw/node";
import { calculateProjection, type ProjectionOutputs } from "../projection/calculator";
import type { SaveProjectionRequest, SavedScenario, ScenarioName, TickerResponse } from "../features/projection/projectionApi";

/**
 * A fake of the Compass API for the rendered-app seam. It stands in for the network only: the
 * real router, store, hooks and 401 handling all run. "The browser attaches the cookie" is
 * modelled as a server-side `session` the SPA cannot see or touch — exactly like the real
 * HttpOnly cookie.
 */
type User = { id: string; email: string; password: string };

type SavedProjection = {
  userId: string;
  ticker: string;
  companyName: string;
  cik: number | null;
  createdAt: string;
  updatedAt: string;
  scenarios: SavedScenario[];
};

type State = {
  users: User[];
  session: User | null;
  requests: Record<string, number>;
  /** Milliseconds to hold every `/api/auth/me` response. */
  meDelay: number;
  projections: SavedProjection[];
  /** Every create and replace body, oldest first. */
  saves: SaveProjectionRequest[];
  fundamentalsDown: boolean;
  quoteDown: boolean;
  /** Outputs every save answers with, in place of computing them. */
  saveOutputs: ProjectionOutputs | null;
};

const freshState = (): State => ({
  users: [],
  session: null,
  requests: {},
  meDelay: 0,
  projections: [],
  saves: [],
  fundamentalsDown: false,
  quoteDown: false,
  saveOutputs: null,
});
let state = freshState();

const problem = (status: number, title: string, extra: object = {}) =>
  HttpResponse.json({ type: "about:blank", title, status, ...extra }, { status });

const unauthorized = () => problem(401, "Unauthorized");

const count = (request: Request) => {
  const key = `${request.method} ${new URL(request.url).pathname}`;
  state.requests[key] = (state.requests[key] ?? 0) + 1;
};

/** As the API's `Projection.NormaliseTicker`. */
const normalise = (ticker: string) => ticker.trim().toUpperCase().replace(/\./g, "-");

/**
 * The one company the fake's lookup knows, in round figures so a test can work the formula by
 * hand: $100, $100,000M revenue, 5,000M shares, a 25% margin. TSM is the unsupported filer and
 * every other ticker is unknown, as in the API's own stub.
 */
const aapl = (): TickerResponse => {
  const scenario = (name: ScenarioName, growthRate: number) => ({
    name,
    growthRate,
    futureNetMargin: 0.25,
    exitMultiple: { kind: "pe" as const, value: state.quoteDown ? null : 20 },
    dilutionRate: -0.01,
    horizon: 5,
  });
  return {
    ticker: "AAPL",
    companyName: "Apple Inc.",
    cik: 320193,
    baseFacts: {
      currentPrice: state.quoteDown ? null : 100,
      ttmRevenue: 100_000_000_000,
      dilutedShares: 5_000_000_000,
      currentNetMargin: 0.25,
      figuresTo: "2025-12-31",
    },
    history: [],
    scenarios: [scenario("bear", 0.04), scenario("base", 0.08), scenario("bull", 0.12)],
    missing: state.quoteDown ? ["currentPrice"] : [],
  };
};

type Lookup = { found: TickerResponse } | { status: number; reason: string };

const lookUp = (symbol: string): Lookup => {
  const ticker = normalise(symbol);
  if (ticker === "TSM") return { status: 422, reason: "TSM files a 20-F, which Compass cannot read. You can enter the figures yourself." };
  if (ticker !== "AAPL") return { status: 404, reason: "No company with that ticker files with the SEC." };
  if (state.fundamentalsDown) return { status: 503, reason: "The company's figures could not be fetched just now." };
  return { found: aapl() };
};

const findOwn = (ticker: string) =>
  state.projections.find((p) => p.userId === state.session?.id && p.ticker === normalise(ticker));

/** A cut-down `SaveProjection.Validate`: the required base facts, and whatever the calculator rejects. */
const validate = (body: SaveProjectionRequest): Record<string, string[]> => {
  const errors: Record<string, string[]> = {};
  if (body.baseFacts?.currentPrice == null) errors["baseFacts.currentPrice"] = ["The current price is required."];
  if (!body.scenarios?.length) errors.scenarios = ["A projection has one to three scenarios."];
  body.scenarios?.forEach((scenario, i) => {
    const result = calculateProjection(body.baseFacts ?? {}, scenario);
    if (result.status === "rejected") errors[`scenarios[${i}]`] = [`These assumptions were rejected: ${result.reason}.`];
  });
  return errors;
};

const savedResponse = (projection: SavedProjection, body: SaveProjectionRequest) => ({
  ...projection,
  userId: undefined,
  scenarios: projection.scenarios.map((scenario) => {
    const result = calculateProjection(body.baseFacts, scenario);
    return { ...scenario, outputs: state.saveOutputs ?? (result.status === "ok" ? result.outputs : undefined) };
  }),
});

const handlers = [
  http.get("/api/tickers/:symbol", ({ request, params }) => {
    count(request);
    if (!state.session) return unauthorized();
    const lookup = lookUp(String(params.symbol));
    if ("found" in lookup) return HttpResponse.json(lookup.found);
    return problem(lookup.status, "Lookup failed.", { detail: lookup.reason });
  }),

  http.get("/api/projections/:ticker", ({ request, params }) => {
    count(request);
    if (!state.session) return unauthorized();
    const projection = findOwn(String(params.ticker));
    if (!projection) return problem(404, "Not Found", { detail: "You have no saved projection for that ticker." });
    const lookup = lookUp(projection.ticker);
    return HttpResponse.json({
      ...projection,
      userId: undefined,
      baseFacts: "found" in lookup ? lookup.found.baseFacts : null,
      missing: "found" in lookup ? lookup.found.missing : [],
      baseFactsUnavailable: "found" in lookup ? null : lookup.reason,
    });
  }),

  http.post("/api/projections", async ({ request }) => {
    count(request);
    if (!state.session) return unauthorized();
    const body = (await request.json()) as SaveProjectionRequest;
    state.saves.push(body);
    const errors = validate(body);
    if (Object.keys(errors).length > 0) return problem(400, "One or more validation errors occurred.", { errors });
    if (findOwn(body.ticker)) {
      return problem(409, "Conflict", { detail: `You already have a projection for ${normalise(body.ticker)}. Replace it instead.` });
    }
    const now = new Date().toISOString();
    const projection: SavedProjection = {
      userId: state.session.id,
      ticker: normalise(body.ticker),
      companyName: body.companyName,
      cik: body.cik,
      createdAt: now,
      updatedAt: now,
      scenarios: body.scenarios,
    };
    state.projections.push(projection);
    return HttpResponse.json(savedResponse(projection, body), { status: 201 });
  }),

  http.put("/api/projections/:ticker", async ({ request, params }) => {
    count(request);
    if (!state.session) return unauthorized();
    const body = (await request.json()) as SaveProjectionRequest;
    state.saves.push(body);
    const projection = findOwn(String(params.ticker));
    if (!projection) return problem(404, "Not Found", { detail: "You have no saved projection for that ticker." });
    const errors = validate(body);
    if (Object.keys(errors).length > 0) return problem(400, "One or more validation errors occurred.", { errors });
    Object.assign(projection, {
      companyName: body.companyName,
      cik: body.cik,
      updatedAt: new Date().toISOString(),
      scenarios: body.scenarios,
    });
    return HttpResponse.json(savedResponse(projection, body));
  }),

  http.get("/api/auth/me", async ({ request }) => {
    count(request);
    if (state.meDelay) await delay(state.meDelay);
    if (!state.session) return unauthorized();
    return HttpResponse.json({ id: state.session.id, email: state.session.email });
  }),

  http.post("/api/auth/register", async ({ request }) => {
    count(request);
    const { email, password } = (await request.json()) as { email: string; password: string };
    const errors: Record<string, string[]> = {};
    if (password.length < 8) errors.password = ["Passwords must be at least 8 characters."];
    if (state.users.some((u) => u.email === email)) errors.email = [`Email '${email}' is already taken.`];
    if (Object.keys(errors).length > 0) return problem(400, "One or more validation errors occurred.", { errors });
    const user = { id: crypto.randomUUID(), email, password };
    state.users.push(user);
    state.session = user;
    return HttpResponse.json({ id: user.id, email: user.email }, { status: 201 });
  }),

  http.post("/api/auth/login", async ({ request }) => {
    count(request);
    const { email, password } = (await request.json()) as { email: string; password: string };
    const user = state.users.find((u) => u.email === email && u.password === password);
    if (!user) return problem(401, "Invalid credentials.");
    state.session = user;
    return HttpResponse.json({ id: user.id, email: user.email });
  }),

  http.post("/api/auth/logout", ({ request }) => {
    count(request);
    if (!state.session) return unauthorized();
    state.session = null;
    return new HttpResponse(null, { status: 204 });
  }),
];

export const fakeApi = {
  server: setupServer(...handlers),

  reset() {
    state = freshState();
  },

  /** Registers a user and, unless told otherwise, gives them a live session (an existing cookie). */
  seedUser({ email = "alice@example.com", password = "correct horse battery", signedIn = true } = {}) {
    const user = { id: crypto.randomUUID(), email, password };
    state.users.push(user);
    if (signedIn) state.session = user;
    return user;
  },

  /** The server forgets the session, as when the cookie expires. The SPA is not told. */
  expireSession() {
    state.session = null;
  },

  holdMeResponsesFor(ms: number) {
    state.meDelay = ms;
  },

  /** The fundamentals source is down: every known ticker's lookup is a 503. */
  failFundamentals(down = true) {
    state.fundamentalsDown = down;
  },

  /** The quote source is down: a lookup is still a 200, with no price and no exit multiple values. */
  failQuote() {
    state.quoteDown = true;
  },

  /** A projection the signed-in user saved earlier: AAPL's seeded scenarios with these changes to each. */
  seedProjection({ updatedAt = "2026-09-12T10:00:00Z", ...changes }: Partial<SavedScenario> & { updatedAt?: string } = {}) {
    if (!state.session) throw new Error("Seed a signed-in user first.");
    const { ticker, companyName, cik, scenarios } = aapl();
    state.projections.push({
      userId: state.session.id,
      ticker,
      companyName,
      cik,
      createdAt: updatedAt,
      updatedAt,
      scenarios: scenarios.map((s) => ({ ...s, exitMultiple: { kind: "pe", value: 20 }, ...changes })),
    });
  },

  /** Every save answers with these outputs, so a test can tell the server's figures from the preview's. */
  answerSavesWith(outputs: ProjectionOutputs) {
    state.saveOutputs = outputs;
  },

  /** The bodies of every create and replace so far, oldest first. */
  saves() {
    return state.saves;
  },

  /** How many times the SPA has called an endpoint, e.g. `"GET /api/auth/me"`. */
  requestCount(endpoint: string) {
    return state.requests[endpoint] ?? 0;
  },
};
