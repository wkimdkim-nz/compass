import { createSlice, type PayloadAction } from "@reduxjs/toolkit";

/** One confirmation, with an id of its own so that a dismissal meant for it cannot clear the next. */
export type Toast = { id: number; text: string };

/**
 * The app's one way to confirm an action (docs/design/phase1-look.md, Feedback). In the store, not
 * a component, so that a toast shown before a route change is still there after it. One at a time.
 */
export type ToastState = { current: Toast | null };

const initialState: ToastState = { current: null };

let nextId = 1;

const toastSlice = createSlice({
  name: "toast",
  initialState,
  reducers: {
    /** Shows a confirmation, replacing any current one. */
    showToast: {
      reducer: (state, action: PayloadAction<Toast>) => {
        state.current = action.payload;
      },
      prepare: (text: string) => ({ payload: { id: nextId++, text } }),
    },
    /** Clears the toast with this id, if it is still the current one. */
    dismissToast: (state, action: PayloadAction<number>) => {
      if (state.current?.id === action.payload) state.current = null;
    },
  },
});

export const { showToast, dismissToast } = toastSlice.actions;
export const toastReducer = toastSlice.reducer;
