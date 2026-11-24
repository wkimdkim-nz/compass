import { describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { fakeApi } from "../../test/fakeApi";
import { renderApp } from "../../test/renderApp";

// A real wait: the absent cases assert what does not appear, and there is no later event to wait for.
const sleep = () => new Promise((resolve) => setTimeout(resolve, 100));

const yourProjections = () => screen.findByRole("table", { name: /your projections/i });
const rows = async () => within(await yourProjections()).getAllByRole("row").slice(1);

const COMPANIES = [
  { ticker: "NVDA", companyName: "NVIDIA Corp", updatedAt: "2026-11-21T09:00:00Z" },
  { ticker: "AAPL", companyName: "Apple Inc.", updatedAt: "2026-11-18T09:00:00Z" },
  { ticker: "COST", companyName: "Costco Wholesale Corp", updatedAt: "2026-11-02T09:00:00Z" },
  { ticker: "BRK-B", companyName: "Berkshire Hathaway Inc.", updatedAt: "2026-10-14T09:00:00Z" },
  { ticker: "SNOW", companyName: "Snowflake Inc.", updatedAt: "2026-09-30T09:00:00Z" },
  { ticker: "MSFT", companyName: "Microsoft Corp", updatedAt: "2026-09-12T09:00:00Z" },
  { ticker: "SHOP", companyName: "Shopify Inc.", updatedAt: "2026-09-02T09:00:00Z" },
];

const seed = (count: number) => {
  // Seeded oldest first, so the list's order has to come from the saved dates.
  for (const { updatedAt, ...company } of COMPANIES.slice(0, count).reverse()) {
    fakeApi.seedProjection({ updatedAt, company });
  }
};

describe("the landing page's Your projections", () => {
  it("shows the five newest, with All N linking to My projections when there are more", async () => {
    fakeApi.seedUser();
    seed(7);
    renderApp("/");

    const shown = await rows();

    expect(shown.map((row) => within(row).getByRole("link").textContent)).toEqual(["NVDA", "AAPL", "COST", "BRK-B", "SNOW"]);
    expect(shown[0]).toHaveTextContent("NVIDIA Corp");
    expect(shown[0]).toHaveTextContent("Bear, Base, Bull");
    expect(shown[0]).toHaveTextContent("21 Nov 2026");
    expect(screen.getByRole("link", { name: "All 7 →" })).toHaveAttribute("href", "/projections");
  });

  it("sits under the ticker box and above the specimen", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection();
    renderApp("/");

    const section = await yourProjections();
    const tickerBox = screen.getByRole("textbox", { name: /ticker/i });
    const specimen = screen.getByRole("table", { name: /specimen/i });

    expect(tickerBox.compareDocumentPosition(section) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(section.compareDocumentPosition(specimen) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("opens a projection's calculator from its ticker", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection({ growthRate: 0.2 });
    renderApp("/");
    const user = userEvent.setup();

    const [row] = await rows();
    await user.click(within(row).getByRole("link", { name: "AAPL" }));

    expect(await screen.findByRole("heading", { name: /apple inc\..*AAPL/i })).toBeInTheDocument();
    expect(await screen.findByRole("textbox", { name: /base revenue growth/i })).toHaveValue("20");
  });

  it("shows all of five or fewer, with no All N", async () => {
    fakeApi.seedUser();
    seed(5);
    renderApp("/");

    expect(await rows()).toHaveLength(5);
    expect(screen.queryByRole("link", { name: /^all \d+/i })).not.toBeInTheDocument();
  });

  it("is absent signed in with nothing saved, once the list has answered", async () => {
    fakeApi.seedUser();
    renderApp("/");

    await screen.findByRole("button", { name: /sign out/i });
    await waitFor(() => expect(fakeApi.requestCount("GET /api/projections")).toBe(1));
    await sleep();
    expect(screen.queryByText(/your projections/i)).not.toBeInTheDocument();
  });

  it("renders nothing in its place while the list loads", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection();
    fakeApi.holdListResponsesFor(300);
    renderApp("/");

    await screen.findByRole("button", { name: /sign out/i });
    await waitFor(() => expect(fakeApi.requestCount("GET /api/projections")).toBe(1));
    expect(screen.queryByText(/your projections/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/loading/i)).not.toBeInTheDocument();

    expect(await yourProjections()).toBeInTheDocument();
  });

  it("is absent, with no error of its own, when the list cannot be loaded", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection();
    fakeApi.failProjectionList();
    renderApp("/");

    await screen.findByRole("button", { name: /sign out/i });
    await waitFor(() => expect(fakeApi.requestCount("GET /api/projections")).toBeGreaterThan(0));
    await sleep();
    expect(screen.queryByText(/your projections/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("is absent signed out, which neither asks for the list nor changes the rest of the page", async () => {
    renderApp("/");

    expect(await screen.findByRole("link", { name: /sign in/i })).toBeInTheDocument();
    await sleep();
    expect(screen.queryByText(/your projections/i)).not.toBeInTheDocument();
    expect(fakeApi.requestCount("GET /api/projections")).toBe(0);
    expect(screen.getByRole("textbox", { name: /ticker/i })).toBeInTheDocument();
    expect(screen.getAllByRole("table")).toEqual([screen.getByRole("table", { name: /specimen/i })]);
  });
});
