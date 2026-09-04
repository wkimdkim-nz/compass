import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";

/**
 * The one chokepoint every API call goes through: one `createApi`, one base URL. Features add
 * their endpoints with `injectEndpoints`.
 *
 * Nothing is attached by hand. There is no `prepareHeaders` (there is no antiforgery token to
 * replay) and no `credentials: "include"` (the SPA and the API are one origin, so the browser
 * attaches the HttpOnly session cookie on its own).
 */
export const api = createApi({
  reducerPath: "api",
  baseQuery: fetchBaseQuery({ baseUrl: "/api" }),
  tagTypes: ["Note"],
  endpoints: () => ({}),
});
