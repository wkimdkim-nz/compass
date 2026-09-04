import "@testing-library/jest-dom/vitest";
import { afterAll, afterEach, beforeAll } from "vitest";
import { cleanup } from "@testing-library/react";
import { fakeApi } from "./fakeApi";

// The app's base query is exactly `fetchBaseQuery({ baseUrl: "/api" })`, a relative URL that a
// browser resolves against the page. Node's `Request` refuses relative URLs, so resolve them
// against jsdom's location here, the way a browser would. Nothing in the app changes for tests.
const NativeRequest = globalThis.Request;
class BrowserLikeRequest extends NativeRequest {
  constructor(input: RequestInfo | URL, init?: RequestInit) {
    super(
      typeof input === "string" && input.startsWith("/")
        ? new URL(input, window.location.origin).href
        : input,
      init,
    );
  }
}
globalThis.Request = BrowserLikeRequest as typeof Request;

beforeAll(() => fakeApi.server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  cleanup();
  fakeApi.reset();
});
afterAll(() => fakeApi.server.close());
