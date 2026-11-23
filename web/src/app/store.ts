import { configureStore, isRejectedWithValue, type Middleware } from "@reduxjs/toolkit";
import { api } from "./api";
import { toastReducer } from "./toast";

/**
 * Endpoints whose 401 is an answer, not a sign the session has gone:
 * - `login` answers 401 to wrong credentials by design; the form shows it.
 * - `getMe` IS the session. Its 401 is the logged-out state. Resetting on it would empty the
 *   cache, re-run the query, 401 again, and loop (~5000 requests were observed when it did).
 *
 * The rule: the endpoint that reports the session can never trigger resetting the session.
 */
const NEVER_RESET_ON_401 = new Set(["login", "getMe"]);

/** A 401 from any other call means the session is gone: forget everything and re-derive "logged out". */
const resetOnUnauthorized: Middleware = (store) => (next) => (action) => {
  if (isRejectedWithValue(action)) {
    const status = (action.payload as { status?: unknown } | undefined)?.status;
    const endpoint = (action.meta as { arg?: { endpointName?: string } }).arg?.endpointName;
    if (status === 401 && endpoint !== undefined && !NEVER_RESET_ON_401.has(endpoint)) {
      store.dispatch(api.util.resetApiState());
    }
  }
  return next(action);
};

/** There is no auth slice: `useGetMeQuery()` is the session. */
export function makeStore() {
  return configureStore({
    reducer: { [api.reducerPath]: api.reducer, toast: toastReducer },
    middleware: (getDefaultMiddleware) => getDefaultMiddleware().concat(api.middleware, resetOnUnauthorized),
  });
}

export type RootState = ReturnType<ReturnType<typeof makeStore>["getState"]>;
