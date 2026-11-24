import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderApp } from "../../test/renderApp";

const guideHeading = () => screen.findByRole("heading", { level: 1, name: /guide/i });

describe("the guide", () => {
  it("is reachable from the masthead by a signed-out visitor", async () => {
    renderApp("/");
    const user = userEvent.setup();

    await user.click(within(await screen.findByRole("banner")).getByRole("link", { name: /^guide$/i }));

    expect(await guideHeading()).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: /sign in/i })).not.toBeInTheDocument();
  });

  it("is linked from the landing page's description", async () => {
    renderApp("/");
    const user = userEvent.setup();

    await user.click(await screen.findByRole("link", { name: /what every figure means/i }));

    expect(await guideHeading()).toBeInTheDocument();
  });

  it("has an entry for every figure on the calculator, grouped, each at its stable address", async () => {
    renderApp("/guide");
    await guideHeading();
    const entries = (group: RegExp) =>
      within(screen.getByRole("table", { name: group }))
        .getAllByRole("row")
        .filter((row) => row.id)
        .map((row) => [row.id, within(row).getByRole("rowheader").textContent]);

    expect(entries(/base facts/i)).toEqual([
      ["share-price", "Share price"],
      ["revenue-ttm", "Revenue (TTM)"],
      ["diluted-shares", "Shares outstanding (diluted)"],
      ["net-margin", "Net margin"],
    ]);
    expect(entries(/assumptions/i)).toEqual([
      ["horizon", "Horizon"],
      ["revenue-growth", "Revenue growth"],
      ["future-net-margin", "Future net margin"],
      ["exit-multiple", "Exit multiple (P/E or P/S)"],
      ["share-dilution", "Share dilution"],
    ]);
    expect(entries(/outputs/i)).toEqual([
      ["future-price", "Future share price"],
      ["the-x", "The X (return multiple)"],
      ["cagr", "CAGR"],
    ]);
  });

  it("links outside sources at their home pages only, each in a new tab", async () => {
    renderApp("/guide");
    await guideHeading();
    const outside = screen.getAllByRole("link").filter((link) => !link.getAttribute("href")!.startsWith("/"));

    const homePages = new Set([
      "https://www.sec.gov/edgar/search/",
      "https://stockanalysis.com/",
      "https://www.macrotrends.net/",
      "https://finance.yahoo.com/",
    ]);
    expect(new Set(outside.map((link) => link.getAttribute("href")))).toEqual(homePages);
    for (const link of outside) {
      expect(link).toHaveAttribute("target", "_blank");
      expect(link).toHaveAttribute("rel", "noopener noreferrer");
    }
  });

  describe("arrived at by an entry's address", () => {
    // jsdom lays nothing out, so it has no scrollIntoView of its own.
    const scrolled = vi.fn();
    Element.prototype.scrollIntoView = function (this: Element) {
      scrolled(this);
    };
    afterEach(() => scrolled.mockClear());

    it("brings that entry into view and highlights it", async () => {
      renderApp("/guide#exit-multiple");
      await guideHeading();

      const entry = screen.getByRole("row", { name: /^exit multiple/i });
      expect(scrolled).toHaveBeenCalledWith(entry);
      expect(entry).toHaveAttribute("aria-current", "true");
      expect(screen.getByRole("row", { name: /^revenue growth/i })).not.toHaveAttribute("aria-current");
    });

    it("still renders when the address carries a malformed fragment", async () => {
      renderApp("/guide#%E0%A4%A");
      await guideHeading();

      expect(scrolled).not.toHaveBeenCalled();
    });
  });

  it("works the shared Apple example's Base scenario through the formula, step by step", async () => {
    renderApp("/guide");
    await guideHeading();
    const example = screen.getByRole("table", { name: /worked example/i });
    const steps = within(example)
      .getAllByRole("row")
      .slice(1)
      .map((row) => [within(row).getByRole("rowheader").textContent, within(row).getAllByRole("cell").at(-1)!.textContent]);

    // $391,035M revenue at 6% for 5 years is $523,293M; a 25% margin leaves $130,823M; at P/E 28
    // that is a $3,663,051M market cap, over 15,408M shares bought back 2.5% a year to 13,576M:
    // $269.82 a share, 269.82 ÷ 228 = 1.1834×, and 1.1834^(1/5) − 1 = 3.43% a year.
    expect(steps).toEqual([
      ["Future revenue", "523,293"],
      ["Future net income", "130,823"],
      ["Future market cap", "3,663,051"],
      ["Future shares", "13,576"],
      ["Future share price", "269.82"],
      ["The X", "1.2×"],
      ["CAGR", "3.4 %"],
    ]);
  });
});

describe("the shared Apple example", () => {
  it("gives the same Base X and CAGR on the landing specimen and in the guide's worked example", async () => {
    renderApp("/");
    const specimen = await screen.findByRole("table", { name: /specimen/i });
    const base = (name: RegExp) => within(within(specimen).getByRole("row", { name })).getAllByRole("cell")[1].textContent;
    const landing = { x: base(/return multiple/i), cagr: base(/cagr/i) };
    cleanup();

    renderApp("/guide");
    await guideHeading();
    const example = screen.getByRole("table", { name: /worked example/i });
    const result = (name: RegExp) => within(within(example).getByRole("row", { name })).getAllByRole("cell").at(-1)!.textContent;
    const guide = { x: result(/^the x/i), cagr: result(/^cagr/i) };

    // Worked by hand in the test above: 1.1834× and 3.43% a year.
    expect(landing).toEqual({ x: "1.2×", cagr: "3.4 %" });
    expect(guide).toEqual(landing);
  });
});
