import { api } from "../../app/api";

/** ⚠️ THROWAWAY, with the API's Note. The placeholder row that proves a user-scoped round-trip. */
export type Note = { id: string; userId: string; text: string; createdAt: string };

export const notesApi = api.injectEndpoints({
  endpoints: (build) => ({
    listNotes: build.query<Note[], void>({
      query: () => "/notes",
      providesTags: ["Note"],
    }),
    addNote: build.mutation<Note, string>({
      query: (text) => ({ url: "/notes", method: "POST", body: { text } }),
      // Tag invalidation refreshes the list; no hand-wired refetch.
      invalidatesTags: ["Note"],
    }),
  }),
});

export const { useListNotesQuery, useAddNoteMutation } = notesApi;
