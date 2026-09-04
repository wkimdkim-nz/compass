import { HttpResponse, http, delay } from "msw";
import { setupServer } from "msw/node";

/**
 * A fake of the Compass API for the rendered-app seam. It stands in for the network only: the
 * real router, store, hooks and 401 handling all run. "The browser attaches the cookie" is
 * modelled as a server-side `session` the SPA cannot see or touch — exactly like the real
 * HttpOnly cookie.
 */
type User = { id: string; email: string; password: string };
type Note = { id: string; userId: string; text: string; createdAt: string };

type State = {
  users: User[];
  session: User | null;
  notes: Note[];
  requests: Record<string, number>;
  /** Milliseconds to hold every `/api/auth/me` response. */
  meDelay: number;
};

const freshState = (): State => ({ users: [], session: null, notes: [], requests: {}, meDelay: 0 });
let state = freshState();

const problem = (status: number, title: string, extra: object = {}) =>
  HttpResponse.json({ type: "about:blank", title, status, ...extra }, { status });

const unauthorized = () => problem(401, "Unauthorized");

const count = (request: Request) => {
  const key = `${request.method} ${new URL(request.url).pathname}`;
  state.requests[key] = (state.requests[key] ?? 0) + 1;
};

const handlers = [
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

  http.get("/api/notes", ({ request }) => {
    count(request);
    if (!state.session) return unauthorized();
    const userId = state.session.id;
    return HttpResponse.json(
      state.notes.filter((n) => n.userId === userId).sort((a, b) => b.createdAt.localeCompare(a.createdAt)),
    );
  }),

  http.post("/api/notes", async ({ request }) => {
    count(request);
    if (!state.session) return unauthorized();
    const { text } = (await request.json()) as { text: string };
    const note = {
      id: crypto.randomUUID(),
      userId: state.session.id,
      text,
      createdAt: new Date(Date.now() + state.notes.length).toISOString(),
    };
    state.notes.push(note);
    return HttpResponse.json(note, { status: 201 });
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

  /** How many times the SPA has called an endpoint, e.g. `"GET /api/auth/me"`. */
  requestCount(endpoint: string) {
    return state.requests[endpoint] ?? 0;
  },
};
