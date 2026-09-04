import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { SerializedError } from "@reduxjs/toolkit";

/** The shape the API answers every error with (RFC 9457), with field errors under `errors`. */
export type ProblemDetails = {
  title?: string;
  status?: number;
  errors?: Record<string, string[]>;
};

/** Field-level messages from a failed request, keyed by form field, or an empty object. */
export function fieldErrors(error: FetchBaseQueryError | SerializedError | undefined): Record<string, string[]> {
  if (error && "status" in error && typeof error.data === "object" && error.data !== null) {
    return (error.data as ProblemDetails).errors ?? {};
  }
  return {};
}

export function isStatus(error: FetchBaseQueryError | SerializedError | undefined, status: number): boolean {
  return error !== undefined && "status" in error && error.status === status;
}
