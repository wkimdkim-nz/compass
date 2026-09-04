import { useState, type FormEvent } from "react";
import { useGetMeQuery } from "../auth/authApi";
import { useAddNoteMutation, useListNotesQuery } from "./notesApi";

/** ⚠️ THROWAWAY. The signed-in view: who you are, and a row of your own written and read back. */
export function NotesPage() {
  const { data: me } = useGetMeQuery();
  const notes = useListNotesQuery();
  const [addNote, { isLoading: adding }] = useAddNoteMutation();
  const [text, setText] = useState("");

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!text.trim()) return;
    const result = await addNote(text);
    if (!("error" in result)) setText("");
  };

  return (
    <main>
      <h1>Your notes</h1>
      <p>Signed in as {me?.email}</p>

      <form onSubmit={submit}>
        <label>
          New note
          <input name="text" value={text} onChange={(e) => setText(e.target.value)} />
        </label>
        <button type="submit" disabled={adding}>
          Add note
        </button>
      </form>

      {notes.isLoading && <p role="status">Loading notes…</p>}
      {notes.data && notes.data.length === 0 && <p>No notes yet.</p>}
      {notes.data && notes.data.length > 0 && (
        <ul aria-label="Notes">
          {notes.data.map((note) => (
            <li key={note.id}>{note.text}</li>
          ))}
        </ul>
      )}
    </main>
  );
}
