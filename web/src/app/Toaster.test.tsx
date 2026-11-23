import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { makeStore } from "./store";
import { showToast } from "./toast";
import { Toaster } from "./Toaster";

// The toast area alone, over a real store: any feature confirms by dispatching `showToast`.
const renderToaster = () => {
  const store = makeStore();
  render(
    <Provider store={store}>
      <Toaster />
    </Provider>,
  );
  const show = (text: string) => act(() => void store.dispatch(showToast(text)));
  return { show };
};

const region = () => screen.getByRole("status", { name: /notifications/i });
const wait = (ms: number) => act(() => vi.advanceTimersByTime(ms));
const dismissButton = () => screen.getByRole("button", { name: /dismiss/i });

// Fake timers freeze the real timeout Testing Library's async helpers wait on, so the pointer and
// focus are moved with synchronous events here rather than user-event.

describe("the toast", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("is announced as a status, and × dismisses it", () => {
    const { show } = renderToaster();
    expect(region()).toBeEmptyDOMElement();

    show("Saved your projection for AAPL.");
    expect(region()).toHaveTextContent("Saved your projection for AAPL.");

    fireEvent.click(dismissButton());
    expect(region()).toBeEmptyDOMElement();
  });

  it("goes on its own after 5 seconds", () => {
    const { show } = renderToaster();
    show("Saved your projection for AAPL.");

    wait(4_900);
    expect(region()).toHaveTextContent(/saved/i);
    wait(100);
    expect(region()).toBeEmptyDOMElement();
  });

  it("pauses while the pointer is over it, and resumes the time it had left when the pointer leaves", () => {
    const { show } = renderToaster();
    show("Saved your projection for AAPL.");
    wait(3_000);

    fireEvent.pointerEnter(screen.getByText(/saved your projection/i), { pointerType: "mouse" });
    wait(10_000);
    expect(region()).toHaveTextContent(/saved/i);

    fireEvent.pointerLeave(screen.getByText(/saved your projection/i), { pointerType: "mouse" });
    wait(1_900);
    expect(region()).toHaveTextContent(/saved/i);
    wait(100);
    expect(region()).toBeEmptyDOMElement();
  });

  it("is not held by a tap, which has no pointer to leave it again", () => {
    const { show } = renderToaster();
    show("Saved your projection for AAPL.");
    wait(3_000);

    // What a touch screen sends for a tap on the text: the pointer enters, and the mouse events the
    // browser makes up after it include an enter but never a leave.
    fireEvent.pointerEnter(screen.getByText(/saved your projection/i), { pointerType: "touch" });
    fireEvent.mouseEnter(screen.getByText(/saved your projection/i));
    wait(2_000);
    expect(region()).toBeEmptyDOMElement();
  });

  it("stays while it holds focus", () => {
    const { show } = renderToaster();
    show("Saved your projection for AAPL.");

    act(() => dismissButton().focus());
    wait(10_000);
    expect(region()).toHaveTextContent(/saved/i);

    act(() => dismissButton().blur());
    wait(5_000);
    expect(region()).toBeEmptyDOMElement();
  });

  it("is replaced by a second toast, one at a time, which gets its own 5 seconds", () => {
    const { show } = renderToaster();
    show("Saved your projection for AAPL.");
    wait(3_000);

    show("Updated your projection for AAPL.");
    expect(screen.getAllByRole("button", { name: /dismiss/i })).toHaveLength(1);
    expect(region()).toHaveTextContent(/^Updated your projection for AAPL\./);
    expect(region()).not.toHaveTextContent(/saved/i);

    // The first toast's timer does not take the second one with it.
    wait(2_000);
    expect(region()).toHaveTextContent(/updated/i);
    wait(3_000);
    expect(region()).toBeEmptyDOMElement();
  });

  it("auto-dismisses a toast shown after one closed while focused", () => {
    const { show } = renderToaster();
    show("Saved your projection for AAPL.");
    act(() => dismissButton().focus());
    fireEvent.click(dismissButton());
    expect(region()).toBeEmptyDOMElement();

    show("Updated your projection for AAPL.");
    wait(5_000);
    expect(region()).toBeEmptyDOMElement();
  });
});
