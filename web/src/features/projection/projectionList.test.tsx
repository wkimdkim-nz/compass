import { describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { fakeApi } from "../../test/fakeApi";
import { renderApp } from "../../test/renderApp";

const heading = () => screen.findByRole("heading", { level: 1, name: /my projections/i });

const entries = async () => {
  await heading();
  return within(await screen.findByRole("list", { name: /saved projections/i })).getAllByRole("listitem");
};

describe("the My projections page", () => {
  it("is reached from the masthead when signed in and lists what the user saved", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection({ updatedAt: "2026-11-21T09:00:00Z" });
    renderApp("/");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("link", { name: "My projections" }));

    const [entry] = await entries();
    expect(within(entry).getByRole("link", { name: /apple inc\. AAPL/i })).toHaveAttribute("href", "/projections/AAPL");
    expect(entry).toHaveTextContent("Saved 21 Nov 2026");
    expect(entry).toHaveTextContent("Bear, Base, Bull");
    expect(within(entry).getByRole("link", { name: /open/i })).toHaveAttribute("href", "/projections/AAPL");
  });

  it("lists every projection newest save first, each opening its calculator from the name or Open", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection({ updatedAt: "2026-09-02T09:00:00Z" });
    fakeApi.seedProjection({
      updatedAt: "2026-11-02T09:00:00Z",
      company: { ticker: "COST", companyName: "Costco Wholesale Corp" },
      only: ["base"],
    });
    fakeApi.seedProjection({
      updatedAt: "2026-10-14T09:00:00Z",
      company: { ticker: "BRK-B", companyName: "Berkshire Hathaway Inc." },
      only: ["bear", "base"],
    });
    renderApp("/projections");

    const list = await entries();

    expect(list.map((entry) => within(entry).getAllByRole("link")[0].textContent)).toEqual([
      "Costco Wholesale Corp COST",
      "Berkshire Hathaway Inc. BRK-B",
      "Apple Inc. AAPL",
    ]);
    expect(list[0]).toHaveTextContent("Saved 2 Nov 2026");
    expect(list[0]).toHaveTextContent(/Base(?!,)/);
    expect(list[0]).not.toHaveTextContent(/Bear|Bull/);
    expect(list[1]).toHaveTextContent("Bear, Base");
    expect(screen.getByText(/opening one fetches today's filed figures; only your assumptions are stored/i)).toBeInTheDocument();
    for (const ticker of ["AAPL", "COST", "BRK-B"]) expect(fakeApi.requestCount(`GET /api/tickers/${ticker}`)).toBe(0);

    const user = userEvent.setup();
    await user.click(within(list[1]).getByRole("link", { name: /open/i }));
    expect(await screen.findByRole("heading", { name: /BRK-B/ })).toBeInTheDocument();
  });

  it("opens a projection on its calculator, with the saved assumptions, from the company name", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection({ growthRate: 0.2 });
    renderApp("/projections");
    const user = userEvent.setup();

    const [entry] = await entries();
    await user.click(within(entry).getByRole("link", { name: /apple inc\./i }));

    expect(await screen.findByRole("heading", { name: /apple inc\..*AAPL/i })).toBeInTheDocument();
    expect(await screen.findByRole("textbox", { name: /base revenue growth/i })).toHaveValue("20");
  });

  it("lists a projection typed in by hand like any other", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection({ company: { ticker: "SHOP", companyName: "Shopify Inc.", cik: null } });
    renderApp("/projections");

    const [entry] = await entries();

    expect(within(entry).getByRole("link", { name: /shopify inc\. SHOP/i })).toHaveAttribute("href", "/projections/SHOP");
  });

  it("says when there is nothing saved and points at the landing page's ticker box", async () => {
    fakeApi.seedUser();
    renderApp("/projections");
    const user = userEvent.setup();

    await heading();
    expect(await screen.findByText(/no saved projections yet\./i)).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: /saved projections/i })).not.toBeInTheDocument();

    await user.click(screen.getByRole("link", { name: /type a ticker/i }));

    expect(await screen.findByRole("textbox", { name: /ticker/i })).toHaveFocus();
  });

  it("says so in place, with a way to try again, when the list cannot be loaded", async () => {
    fakeApi.seedUser();
    fakeApi.seedProjection();
    fakeApi.failProjectionList();
    renderApp("/projections");

    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be loaded/i);
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
    expect(screen.queryByText(/no saved projections yet/i)).not.toBeInTheDocument();
  });

  it("sends a signed-out visitor through sign in and back to the list", async () => {
    fakeApi.seedUser({ email: "alice@example.com", password: "correct horse battery", signedIn: false });
    renderApp("/projections");
    const user = userEvent.setup();

    expect(screen.queryByRole("link", { name: "My projections" })).not.toBeInTheDocument();
    await screen.findByRole("heading", { name: /sign in/i });
    expect(screen.queryByRole("link", { name: "My projections" })).not.toBeInTheDocument();
    await user.type(screen.getByLabelText(/email/i), "alice@example.com");
    await user.type(screen.getByLabelText(/password/i), "correct horse battery");
    await user.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await heading()).toBeInTheDocument();
  });
});
