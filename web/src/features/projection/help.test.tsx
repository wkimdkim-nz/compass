import { describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { fakeApi } from "../../test/fakeApi";
import { renderApp } from "../../test/renderApp";

const calculator = async () => {
  fakeApi.seedUser();
  renderApp("/projections/AAPL");
  await screen.findByRole("table", { name: /assumptions/i });
  return userEvent.setup();
};

const help = (name: string) => screen.getByRole("button", { name: `What is ${name}?` });

describe("the calculator's help", () => {
  it("puts a closed “?” beside every base fact, assumption and output label", async () => {
    await calculator();

    for (const name of [
      "Share price",
      "Revenue (TTM)",
      "Shares outstanding (diluted)",
      "Net margin",
      "Horizon",
      "Revenue growth",
      "Future net margin",
      "Exit multiple (P/E or P/S)",
      "Share dilution",
      "The X (return multiple)",
      "CAGR",
      "Future share price",
    ]) {
      expect(help(name)).toHaveAttribute("aria-expanded", "false");
    }
  });

  it("opens under the label with what the figure is and what to anchor the guess on", async () => {
    const user = await calculator();

    await user.click(help("Revenue growth"));

    expect(help("Revenue growth")).toHaveAttribute("aria-expanded", "true");
    const row = screen.getByRole("row", { name: /^revenue growth/i });
    expect(within(row).getByText("How fast revenue grows each year, on average, over the horizon.")).toBeInTheDocument();
    expect(within(row).getByText(/anchor on/i)).toHaveTextContent(/revenue CAGR over the last 3–5 years of filed history/);
  });

  it("leaves out the anchor line for a figure that has none", async () => {
    const user = await calculator();

    await user.click(help("Share price"));

    const row = screen.getByRole("row", { name: /^share price/i });
    expect(within(row).getByText(/what one share costs today/i)).toBeInTheDocument();
    expect(within(row).queryByText(/anchor on/i)).not.toBeInTheDocument();
  });

  it("links to the figure's guide entry in a new tab, so unsaved edits stay", async () => {
    const user = await calculator();

    await user.click(help("Exit multiple (P/E or P/S)"));

    const link = screen.getByRole("link", { name: /where to find it, in the guide/i });
    expect(link).toHaveAttribute("href", "/guide#exit-multiple");
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("closes on Escape, handing focus back to its “?”", async () => {
    const user = await calculator();

    await user.click(help("Horizon"));
    await user.tab();
    expect(screen.getByRole("link", { name: /where to find it/i })).toHaveFocus();
    await user.keyboard("{Escape}");

    expect(help("Horizon")).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByText(/how many whole years ahead/i)).not.toBeInTheDocument();
    expect(help("Horizon")).toHaveFocus();
  });

  it("closes on a click elsewhere on the page, but not on a click inside it", async () => {
    const user = await calculator();

    await user.click(help("CAGR"));
    await user.click(screen.getByText(/the x spread evenly over the horizon/i));
    expect(help("CAGR")).toHaveAttribute("aria-expanded", "true");

    await user.click(screen.getByRole("heading", { name: /apple inc/i }));
    expect(help("CAGR")).toHaveAttribute("aria-expanded", "false");
  });

  it("is the only one open: opening another closes the first", async () => {
    const user = await calculator();

    await user.click(help("Share dilution"));
    await user.click(help("Future net margin"));

    expect(help("Share dilution")).toHaveAttribute("aria-expanded", "false");
    expect(help("Future net margin")).toHaveAttribute("aria-expanded", "true");
    expect(screen.getAllByRole("link", { name: /where to find it/i })).toHaveLength(1);
  });

  it("works from the keyboard: Enter opens it, Enter on another moves to that one, Escape closes it", async () => {
    const user = await calculator();

    help("Net margin").focus();
    await user.keyboard("{Enter}");
    expect(help("Net margin")).toHaveAttribute("aria-expanded", "true");

    help("Horizon").focus();
    await user.keyboard("{Enter}");
    expect(help("Net margin")).toHaveAttribute("aria-expanded", "false");
    expect(help("Horizon")).toHaveAttribute("aria-expanded", "true");

    await user.keyboard("{Escape}");
    expect(help("Horizon")).toHaveAttribute("aria-expanded", "false");
    expect(help("Horizon")).toHaveFocus();
  });
});

describe("the landing specimen", () => {
  it("carries no “?”: it is a picture of the calculator, not the tool", async () => {
    renderApp("/");
    const specimen = await screen.findByRole("table", { name: /specimen/i });

    expect(within(specimen).queryByRole("button", { name: /^what is/i })).not.toBeInTheDocument();
  });
});
