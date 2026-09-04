import { api } from "../../app/api";

export type Me = { id: string; email: string };
export type Credentials = { email: string; password: string };

export const authApi = api.injectEndpoints({
  endpoints: (build) => ({
    /** The session. There is no other copy of "am I logged in". */
    getMe: build.query<Me, void>({
      query: () => "/auth/me",
      providesTags: ["Me"],
    }),

    register: build.mutation<Me, Credentials>({
      query: (body) => ({ url: "/auth/register", method: "POST", body }),
      // The API signs the new user in and tells us who they are: seed the session from that
      // answer rather than asking again.
      async onQueryStarted(_credentials, { dispatch, queryFulfilled }) {
        try {
          const { data } = await queryFulfilled;
          dispatch(authApi.util.upsertQueryData("getMe", undefined, data));
        } catch {
          // The failure is reported through the mutation's own result; nothing to do here.
        }
      },
      invalidatesTags: ["Note"],
    }),

    login: build.mutation<Me, Credentials>({
      query: (body) => ({ url: "/auth/login", method: "POST", body }),
      async onQueryStarted(_credentials, { dispatch, queryFulfilled }) {
        try {
          const { data } = await queryFulfilled;
          dispatch(authApi.util.upsertQueryData("getMe", undefined, data));
        } catch {
          // The failure is reported through the mutation's own result; nothing to do here.
        }
      },
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

export const { useGetMeQuery, useRegisterMutation, useLoginMutation, useLogoutMutation } = authApi;
