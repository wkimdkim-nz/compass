import { render } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router";
import { App } from "../App";
import { makeStore } from "../app/store";

/** Renders the real app — real routes, real store — at a path. Every call gets a fresh store. */
export function renderApp(path = "/") {
  return render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </Provider>,
  );
}
