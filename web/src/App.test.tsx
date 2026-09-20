import { describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { fakeApi } from "./test/fakeApi";
import { renderApp } from "./test/renderApp";

// A real wall-clock wait. The assertions after it are about what does NOT happen, and
// there is no quiescence to wait for — only time in which a runaway loop would show up.
const sleep = (ms = 300) => new Promise((resolve) => setTimeout(resolve, ms));

const signInHeading = () => screen.findByRole("heading", { name: /sign in/i });
const homeHeading = () => screen.findByRole("heading", { name: /your projections/i });

async function fillCredentials(email: string, password: string) {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText(/email/i), email);
  await user.type(screen.getByLabelText(/password/i), password);
  return user;
}

describe("a logged-out visitor", () => {
  it("hitting a protected route sees the logged-out view and the app settles", async () => {
    renderApp("/");

    expect(await signInHeading()).toBeInTheDocument();

    // The loop the seam prototype found made ~5000 of these in seconds.
    await sleep();
    expect(fakeApi.requestCount("GET /api/auth/me")).toBeLessThanOrEqual(2);
  });

  it("sees a loading state, not the logged-out view, while the session is being determined", async () => {
    fakeApi.seedUser();
    fakeApi.holdMeResponsesFor(200);
    renderApp("/");

    expect(await screen.findByRole("status")).toHaveTextContent(/checking your session/i);
    expect(screen.queryByRole("heading", { name: /sign in/i })).not.toBeInTheDocument();

    expect(await homeHeading()).toBeInTheDocument();
  });

  it("sees a form error when login answers 401, and the API state is not reset", async () => {
    fakeApi.seedUser({ email: "alice@example.com", password: "correct horse battery", signedIn: false });
    renderApp("/login");
    await signInHeading();
    const meRequestsBefore = fakeApi.requestCount("GET /api/auth/me");

    const user = await fillCredentials("alice@example.com", "wrong password");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/invalid email or password/i);
    expect(await signInHeading()).toBeInTheDocument();
    // A reset would empty the cache and make the session query run again.
    await sleep();
    expect(fakeApi.requestCount("GET /api/auth/me")).toBe(meRequestsBefore);
  });

  it("is signed in after logging in with valid credentials", async () => {
    fakeApi.seedUser({ email: "alice@example.com", password: "correct horse battery", signedIn: false });
    renderApp("/login");
    await signInHeading();

    const user = await fillCredentials("alice@example.com", "correct horse battery");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await homeHeading()).toBeInTheDocument();
    expect(screen.getByText(/signed in as alice@example.com/i)).toBeInTheDocument();
  });

  it("is signed in the moment registration succeeds, with no second login step", async () => {
    renderApp("/register");
    await screen.findByRole("heading", { name: /create an account/i });

    const user = await fillCredentials("new@example.com", "a long enough passphrase");
    await user.click(screen.getByRole("button", { name: /register/i }));

    expect(await homeHeading()).toBeInTheDocument();
    expect(screen.getByText(/signed in as new@example.com/i)).toBeInTheDocument();
    expect(fakeApi.requestCount("POST /api/auth/login")).toBe(0);
  });

  it("sees field-level errors when registration fails", async () => {
    fakeApi.seedUser({ email: "taken@example.com", signedIn: false });
    renderApp("/register");
    await screen.findByRole("heading", { name: /create an account/i });

    const user = await fillCredentials("taken@example.com", "short");
    await user.click(screen.getByRole("button", { name: /register/i }));

    const alerts = await screen.findAllByRole("alert");
    expect(alerts.map((a) => a.textContent).join("\n")).toMatch(/already taken/i);
    expect(alerts.map((a) => a.textContent).join("\n")).toMatch(/at least 8 characters/i);
    expect(screen.getByRole("heading", { name: /create an account/i })).toBeInTheDocument();
  });
});

describe("a signed-in user", () => {
  it("is returned to the logged-out view after logging out, with the cached session cleared", async () => {
    fakeApi.seedUser();
    renderApp("/");
    await homeHeading();

    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /sign out/i }));

    expect(await signInHeading()).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /your projections/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /sign out/i })).not.toBeInTheDocument();
    // No further authenticated render: nothing brings the signed-in view back.
    await sleep();
    expect(screen.queryByRole("heading", { name: /your projections/i })).not.toBeInTheDocument();
  });

  it("is returned to the logged-out view when a protected call answers 401", async () => {
    fakeApi.seedUser();
    renderApp("/");
    await homeHeading();

    // The session expires on the server; the SPA finds out from its next protected call, which
    // until the calculator lands is signing out: a 401 where there is no session left to end.
    fakeApi.expireSession();
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /sign out/i }));

    expect(await signInHeading()).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("heading", { name: /your projections/i })).not.toBeInTheDocument());
    await sleep();
    expect(fakeApi.requestCount("GET /api/auth/me")).toBeLessThanOrEqual(4);
  });
});
