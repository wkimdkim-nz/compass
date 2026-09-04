import { api } from "../../app/api";

export type Me = { id: string; email: string };
export type Credentials = { email: string; password: string };

export const authApi = api.injectEndpoints({
  endpoints: (build) => ({
    /**
     * The session. There is no other copy of "am I logged in".
     *
     * Deliberately untagged: nothing invalidates the session. Register and login seed it from
     * their own answer, and the two things that end it — logout, and a 401 from any other call
     * (see the store's middleware) — reset the whole store. A tag here would only be dead weight.
     */
    getMe: build.query<Me, void>({
      query: () => "/auth/me",
    }),

    register: build.mutation<Me, Credentials>({
      query: (body) => ({ url: "/auth/register", method: "POST", body }),
      onQueryStarted: seedSessionFromAnswer,
      invalidatesTags: ["Note"],
    }),

    login: build.mutation<Me, Credentials>({
      query: (body) => ({ url: "/auth/login", method: "POST", body }),
      onQueryStarted: seedSessionFromAnswer,
      invalidatesTags: ["Note"],
    }),

    /**
     * Logout answers 204, so the 401 handler never fires. Without this the cache keeps serving
     * the old session and the SPA renders a page with no session behind it.
     */
    logout: build.mutation<void, void>({
      query: () => ({ url: "/auth/logout", method: "POST" }),
      async onQueryStarted(_arg, { dispatch, queryFulfilled }) {
        try {
          await queryFulfilled;
        } catch {
          return; // Still signed in as far as the server is concerned; keep the session.
        }
        dispatch(api.util.resetApiState());
      },
    }),
  }),
});

/**
 * Register and login both sign the user in and answer with who they are. Seed the session
 * query from that answer rather than asking `/auth/me` a second time.
 */
async function seedSessionFromAnswer(
  _credentials: Credentials,
  { dispatch, queryFulfilled }: { dispatch: (action: unknown) => unknown; queryFulfilled: Promise<{ data: Me }> },
) {
  try {
    const { data } = await queryFulfilled;
    dispatch(authApi.util.upsertQueryData("getMe", undefined, data));
  } catch {
    // The failure is reported through the mutation's own result; nothing to do here.
  }
}

export const { useGetMeQuery, useRegisterMutation, useLoginMutation, useLogoutMutation } = authApi;
