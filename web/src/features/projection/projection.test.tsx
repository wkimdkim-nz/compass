import { describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { fakeApi } from "../../test/fakeApi";
import { renderApp } from "../../test/renderApp";

// The fake's AAPL is $100, $100,000M revenue, 5,000M shares, with Bear / Base / Bull growing 4%,
// 8% and 12% a year for 5 years to a 25% margin, a P/E of 20 and 1% a year of buybacks. Worked by
// hand from the glossary formula, Base is 100,000 × 1.08⁵ × 0.25 × 20 ÷ (5,000 × 0.99⁵) = $154.51:
// 1.5×, 9% a year. Bear is $127.94 (1.3×, 5%) and Bull $185.32 (1.9×, 13%).

const field = (name: RegExp | string) => screen.getByRole("textbox", { name }) as HTMLInputElement;

/** The three figures of an output row, Bear to Bull. */
const outputs = (row: RegExp) => {
  const cells = within(screen.getByRole("row", { name: row })).getAllByRole("cell");
  return cells.map((cell) => cell.textContent);
};

const calculatorFor = async (ticker: string) => {
  renderApp(`/projections/${ticker}`);
  await screen.findByRole("table", { name: /assumptions/i });
  return userEvent.setup();
};

/** The app-wide toast area, which is always there and empty until something confirms. */
const toasts = () => screen.getByRole("status", { name: /notifications/i });
/** The calculator's own note that the outputs are the server's calculation. */
const serverNote = () => screen.queryByText(/the outputs above are the server's own calculation/i);

const retype = async (user: ReturnType<typeof userEvent.setup>, input: HTMLElement, text: string) => {
  await user.clear(input);
  if (text) await user.type(input, text);
};

describe("the landing page", () => {
  it("takes a ticker to that company's calculator, pre-filled from the lookup", async () => {
    fakeApi.seedUser();
    renderApp("/");
    const user = userEvent.setup();

    await user.type(await screen.findByRole("textbox", { name: /ticker/i }), "aapl");
    await user.click(screen.getByRole("button", { name: /project/i }));

    expect(await screen.findByRole("heading", { name: /apple inc\..*AAPL/i })).toBeInTheDocument();
    expect(screen.getByText(/figures to/i)).toHaveTextContent("Figures to 31 Dec 2025");
    expect(field("Share price")).toHaveValue("100");
    expect(field(/^revenue/i)).toHaveValue("100,000");
    expect(field(/^shares/i)).toHaveValue("5,000");
    // Printed, not a field: no formula takes the current net margin.
    expect(screen.getByRole("row", { name: /^net margin/i })).toHaveTextContent("25 %");
    expect(screen.queryByRole("textbox", { name: /current net margin/i })).not.toBeInTheDocument();
    expect(field(/bear revenue growth/i)).toHaveValue("4");
    expect(field(/base revenue growth/i)).toHaveValue("8");
    expect(field(/bull revenue growth/i)).toHaveValue("12");
    expect(field(/base share dilution/i)).toHaveValue("(1)");
    expect(outputs(/return multiple/i)).toEqual(["1.3×", "1.5×", "1.9×"]);
    expect(outputs(/cagr/i)).toEqual(["5.1 %", "9.1 %", "13.1 %"]);
    expect(outputs(/future share price/i)).toEqual(["128", "155", "185"]);
  });

  it("sends a signed-out visitor through sign in and on to the projection", async () => {
    fakeApi.seedUser({ email: "alice@example.com", password: "correct horse battery", signedIn: false });
    renderApp("/");
    const user = userEvent.setup();

    await user.type(await screen.findByRole("textbox", { name: /ticker/i }), "AAPL");
    await user.click(screen.getByRole("button", { name: /project/i }));

    await screen.findByRole("heading", { name: /sign in/i });
    await user.type(screen.getByLabelText(/email/i), "alice@example.com");
    await user.type(screen.getByLabelText(/password/i), "correct horse battery");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByRole("heading", { name: /apple inc/i })).toBeInTheDocument();
  });

  it("carries the disclaimer in its footer", async () => {
    renderApp("/");
    await screen.findByRole("textbox", { name: /ticker/i });

    expect(within(screen.getByRole("contentinfo")).getByText(/not investment advice/i)).toBeInTheDocument();
  });
});

describe("the calculator", () => {
  it("opens directly at its own address", async () => {
    fakeApi.seedUser();
    await calculatorFor("aapl");

    expect(screen.getByRole("heading", { name: /apple inc\..*AAPL/i })).toBeInTheDocument();
    expect(outputs(/return multiple/i)).toEqual(["1.3×", "1.5×", "1.9×"]);
  });

  it("recomputes a column on the keystroke, and that column alone", async () => {
    fakeApi.seedUser();
    const user = await calculatorFor("AAPL");

    // Base at 10% a year: 100,000 × 1.1⁵ × 0.25 × 20 ÷ (5,000 × 0.99⁵) = $169.35.
    await retype(user, field(/base revenue growth/i), "10");

    expect(outputs(/return multiple/i)).toEqual(["1.3×", "1.7×", "1.9×"]);
    expect(outputs(/future share price/i)).toEqual(["128", "169", "185"]);
  });

  it("recomputes every column when a base fact changes", async () => {
    fakeApi.seedUser();
    const user = await calculatorFor("AAPL");

    await retype(user, field("Share price"), "200");

    expect(outputs(/return multiple/i)).toEqual(["0.6×", "0.8×", "0.9×"]);
  });

  it("values revenue instead of earnings when a column is switched to P/S", async () => {
    fakeApi.seedUser();
    const user = await calculatorFor("AAPL");

    // Base on a P/S of 5: 100,000 × 1.08⁵ × 5 ÷ (5,000 × 0.99⁵) = $154.51, the P/E figure, since 0.25 × 20 = 5.
    await user.click(screen.getByRole("radio", { name: /base.*p\/s/i }));
    await retype(user, field(/base exit multiple/i), "5");
    expect(outputs(/future share price/i)).toEqual(["128", "155", "185"]);

    // The margin plays no part on the P/S path.
    await retype(user, field(/base future net margin/i), "");
    expect(outputs(/future share price/i)).toEqual(["128", "155", "185"]);
  });

  it("blanks a column's outputs while one of its fields is empty, mistyped or out of range", async () => {
    fakeApi.seedUser();
    const user = await calculatorFor("AAPL");

    for (const typed of ["", "abc", "-150", "1e999"]) {
      await retype(user, field(/bull revenue growth/i), typed);
      expect(outputs(/return multiple/i)).toEqual(["1.3×", "1.5×", "—"]);
      expect(outputs(/cagr/i)).toEqual(["5.1 %", "9.1 %", "—"]);
      expect(outputs(/future share price/i)).toEqual(["128", "155", "—"]);
    }
    await retype(user, field(/^shares/i), "0");
    expect(outputs(/return multiple/i)).toEqual(["—", "—", "—"]);
    expect(document.body).not.toHaveTextContent(/NaN|Infinity/);
  });

  it("prints the disclaimer beside the outputs and in the footer", async () => {
    fakeApi.seedUser();
    await calculatorFor("AAPL");

    expect(screen.getByRole("note")).toHaveTextContent(/not a prediction and not investment advice/i);
    expect(within(screen.getByRole("contentinfo")).getByText(/not investment advice/i)).toBeInTheDocument();
  });
});

describe("saving", () => {
  it("creates a new company's projection in wire units and shows the server's outputs", async () => {
    fakeApi.seedUser();
    fakeApi.answerSavesWith({ x: 4.26, cagr: 0.336, futurePrice: 426 });
    const user = await calculatorFor("AAPL");
    await retype(user, field(/bear revenue growth/i), "2.5");
    await retype(user, field(/^revenue/i), "120,500");

    await user.click(screen.getByRole("button", { name: /save projection/i }));

    expect(await screen.findByRole("button", { name: /update projection/i })).toBeInTheDocument();
    expect(toasts()).toHaveTextContent("Saved your projection for AAPL.");
    expect(serverNote()).toBeInTheDocument();
    expect(outputs(/return multiple/i)).toEqual(["4.3×", "4.3×", "4.3×"]);
    expect(outputs(/cagr/i)).toEqual(["33.6 %", "33.6 %", "33.6 %"]);
    expect(fakeApi.requestCount("POST /api/projections")).toBe(1);
    expect(fakeApi.saves()[0]).toEqual({
      ticker: "AAPL",
      companyName: "Apple Inc.",
      cik: 320193,
      baseFacts: { currentPrice: 100, ttmRevenue: 120_500_000_000, dilutedShares: 5_000_000_000 },
      scenarios: [
        { name: "bear", growthRate: 0.025, futureNetMargin: 0.25, exitMultiple: { kind: "pe", value: 20 }, dilutionRate: -0.01, horizon: 5 },
        { name: "base", growthRate: 0.08, futureNetMargin: 0.25, exitMultiple: { kind: "pe", value: 20 }, dilutionRate: -0.01, horizon: 5 },
        { name: "bull", growthRate: 0.12, futureNetMargin: 0.25, exitMultiple: { kind: "pe", value: 20 }, dilutionRate: -0.01, horizon: 5 },
      ],
    });

    // The next keystroke is the preview's again.
    // On $120,500M of revenue: Bear at 2.5% is $143.36, Base $186.18, Bull $223.31.
    await retype(user, field(/base revenue growth/i), "8");
    expect(outputs(/return multiple/i)).toEqual(["1.4×", "1.9×", "2.2×"]);
  });

  it("keeps its toast when the user moves on to another page", async () => {
    fakeApi.seedUser();
    const user = await calculatorFor("AAPL");
    await user.click(screen.getByRole("button", { name: /save projection/i }));
    await waitFor(() => expect(toasts()).toHaveTextContent("Saved your projection for AAPL."));

    await user.click(screen.getByRole("link", { name: /compass/i }));

    expect(await screen.findByRole("textbox", { name: /ticker/i })).toBeInTheDocument();
    expect(toasts()).toHaveTextContent("Saved your projection for AAPL.");
  });

  it("keeps the preview of an edit made while the save was on its way", async () => {
    fakeApi.seedUser();
    fakeApi.holdSaveResponsesFor(150);
    const user = await calculatorFor("AAPL");

    await user.click(screen.getByRole("button", { name: /save projection/i }));
    await retype(user, field(/base revenue growth/i), "12");

    await waitFor(() => expect(screen.getByRole("button", { name: /update projection/i })).toBeEnabled());
    // Base now matches Bull; the save's answer, worked from 8%, is not put over it.
    expect(outputs(/return multiple/i)).toEqual(["1.3×", "1.9×", "1.9×"]);
    expect(serverNote()).not.toBeInTheDocument();
    // The save itself was taken, and says so.
    expect(toasts()).toHaveTextContent("Saved your projection for AAPL.");
  });

  it("replaces an existing projection only after the confirmation", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection();
    const user = await calculatorFor("AAPL");
    await retype(user, field(/base revenue growth/i), "10");

    await user.click(screen.getByRole("button", { name: /update projection/i }));
    const dialog = await screen.findByRole("dialog", { name: /replace your saved projection for AAPL/i });
    expect(within(dialog).getByRole("button", { name: /keep saved/i })).toHaveFocus();
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /update projection/i })).toHaveFocus();
    expect(fakeApi.requestCount("PUT /api/projections/AAPL")).toBe(0);

    await user.click(screen.getByRole("button", { name: /update projection/i }));
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: /replace/i }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(toasts()).toHaveTextContent("Updated your projection for AAPL."));
    expect(serverNote()).toBeInTheDocument();
    expect(fakeApi.requestCount("PUT /api/projections/AAPL")).toBe(1);
    expect(fakeApi.saves()[0].scenarios[1].growthRate).toBe(0.1);
  });

  it("says what is missing rather than saving a half-filled column", async () => {
    fakeApi.seedUser();
    const user = await calculatorFor("AAPL");
    await retype(user, field(/bull exit multiple/i), "");

    await user.click(screen.getByRole("button", { name: /save projection/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/bull/i);
    expect(fakeApi.requestCount("POST /api/projections")).toBe(0);
  });

  it("asks for the margin a P/S column can be previewed without, rather than saving a zero in its place", async () => {
    fakeApi.seedUser();
    const user = await calculatorFor("AAPL");
    await user.click(screen.getByRole("radio", { name: /base.*p\/s/i }));
    await retype(user, field(/base future net margin/i), "");

    await user.click(screen.getByRole("button", { name: /save projection/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/base column.*future net margin/i);
    expect(fakeApi.requestCount("POST /api/projections")).toBe(0);
  });

  it("saves the columns that are filled in when another is left empty altogether", async () => {
    fakeApi.seedUser();
    const user = await calculatorFor("XYZQ");
    await retype(user, field("Share price"), "10");
    await retype(user, field(/^revenue/i), "500");
    await retype(user, field(/^shares/i), "100");
    await retype(user, field(/base horizon/i), "5");
    await retype(user, field(/base revenue growth/i), "20");
    await retype(user, field(/base future net margin/i), "10");
    await retype(user, field(/base exit multiple/i), "25");
    await retype(user, field(/base share dilution/i), "2");

    await user.click(screen.getByRole("button", { name: /save projection/i }));

    expect(await screen.findByRole("button", { name: /update projection/i })).toBeInTheDocument();
    expect(fakeApi.saves()[0]).toMatchObject({
      ticker: "XYZQ",
      companyName: "XYZQ",
      cik: null,
      baseFacts: { currentPrice: 10, ttmRevenue: 500_000_000, dilutedShares: 100_000_000 },
    });
    expect(fakeApi.saves()[0].scenarios.map((s) => s.name)).toEqual(["base"]);
  });
});

describe("a saved projection", () => {
  it("loads its assumptions over fresh base facts, and says so", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection({ growthRate: 0.3, updatedAt: "2026-09-12T10:00:00Z" });
    await calculatorFor("AAPL");

    expect(screen.getByText(/your assumptions, saved/i)).toHaveTextContent(/saved 12 Sep 2026, over today's figures/i);
    expect(field(/bear revenue growth/i)).toHaveValue("30");
    expect(field("Share price")).toHaveValue("100");
    expect(screen.getByRole("button", { name: /update projection/i })).toBeInTheDocument();
  });

  it("restates to the seeded defaults without deleting the projection", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection({ growthRate: 0.3 });
    const user = await calculatorFor("AAPL");

    await user.click(screen.getByRole("button", { name: /restate to defaults/i }));

    expect(field(/bear revenue growth/i)).toHaveValue("4");
    expect(field(/bull revenue growth/i)).toHaveValue("12");
    expect(screen.queryByText(/your assumptions, saved/i)).not.toBeInTheDocument();
    // Still there to be replaced: nothing was deleted.
    expect(screen.getByRole("button", { name: /update projection/i })).toBeInTheDocument();
    expect(fakeApi.requestCount("PUT /api/projections/AAPL")).toBe(0);
  });
});

describe("a lookup that cannot pre-fill", () => {
  const typeACompany = async (user: ReturnType<typeof userEvent.setup>) => {
    await retype(user, field("Share price"), "10");
    await retype(user, field(/^revenue/i), "500");
    await retype(user, field(/^shares/i), "100");
    for (const name of ["bear", "base", "bull"]) {
      await retype(user, field(new RegExp(`${name} horizon`, "i")), "5");
      await retype(user, field(new RegExp(`${name} revenue growth`, "i")), "20");
      await retype(user, field(new RegExp(`${name} future net margin`, "i")), "10");
      await retype(user, field(new RegExp(`${name} exit multiple`, "i")), "25");
      await retype(user, field(new RegExp(`${name} share dilution`, "i")), "2");
    }
  };

  it("says an unknown ticker has no filings and leaves the form to be filled in and saved", async () => {
    fakeApi.seedUser();
    const user = await calculatorFor("XYZQ");

    expect(screen.getByText(/no sec filings found for XYZQ/i)).toBeInTheDocument();
    expect(field("Share price")).toHaveValue("");
    expect(outputs(/return multiple/i)).toEqual(["—", "—", "—"]);

    // 500 × 1.2⁵ × 0.1 × 25 ÷ (100 × 1.02⁵) = $28.17 on a $10 share.
    await typeACompany(user);
    expect(outputs(/return multiple/i)).toEqual(["2.8×", "2.8×", "2.8×"]);

    await user.click(screen.getByRole("button", { name: /save projection/i }));
    expect(await screen.findByRole("button", { name: /update projection/i })).toBeInTheDocument();
  });

  it("gives the plain reason for a filer it cannot read", async () => {
    fakeApi.seedUser();
    await calculatorFor("TSM");

    expect(screen.getByText(/TSM files a 20-F, which Compass cannot read/i)).toBeInTheDocument();
    expect(field("Share price")).toBeEnabled();
    expect(screen.getByRole("button", { name: /save projection/i })).toBeEnabled();
  });

  it("says the figures could not be reached, keeps saved assumptions, and fills in on trying again", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection({ growthRate: 0.3 });
    fakeApi.failFundamentals();
    const user = await calculatorFor("AAPL");

    expect(screen.getByText(/could not be reached just now/i)).toBeInTheDocument();
    // There are no figures of today's for the saved assumptions to sit over.
    expect(screen.getByText(/your assumptions, saved/i)).not.toHaveTextContent(/today's figures/i);
    expect(field("Share price")).toHaveValue("");
    expect(field(/base revenue growth/i)).toHaveValue("30");
    expect(screen.getByRole("button", { name: /update projection/i })).toBeEnabled();
    // No defaults came with the failed lookup, so there is nothing to restate to.
    expect(screen.queryByRole("button", { name: /restate to defaults/i })).not.toBeInTheDocument();

    fakeApi.failFundamentals(false);
    await user.click(screen.getByRole("button", { name: /try again/i }));

    await waitFor(() => expect(field("Share price")).toHaveValue("100"));
    expect(screen.queryByText(/could not be reached just now/i)).not.toBeInTheDocument();
    expect(field(/base revenue growth/i)).toHaveValue("30");
  });

  it("pre-fills everything but the price when the quote alone failed", async () => {
    fakeApi.seedUser();
    fakeApi.failQuote();
    const user = await calculatorFor("AAPL");

    expect(screen.getByText(/share price could not be fetched/i)).toBeInTheDocument();
    expect(field("Share price")).toHaveValue("");
    expect(field(/^revenue/i)).toHaveValue("100,000");
    expect(field(/base revenue growth/i)).toHaveValue("8");
    expect(field(/base exit multiple/i)).toHaveValue("");

    await retype(user, field("Share price"), "100");
    await retype(user, field(/base exit multiple/i), "20");
    expect(outputs(/return multiple/i)).toEqual(["—", "1.5×", "—"]);
  });

  it("shows what the server said of a save it refused", async () => {
    fakeApi.seedUser();
    const user = await calculatorFor("AAPL");
    // Saved from another tab since this page loaded, so the create is a 409.
    fakeApi.seedProjection();

    await user.click(screen.getByRole("button", { name: /save projection/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/you already have a projection for AAPL/i);
    expect(toasts()).toBeEmptyDOMElement();
    expect(serverNote()).not.toBeInTheDocument();
  });
});
