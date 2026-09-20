import { useLocation } from "react-router";

/**
 * Where signing in or registering goes next: the guarded address the visitor was sent here from
 * (`ProtectedRoute` puts it in the navigation state), or the landing page. `state` is for the
 * link between the two forms, so that switching form does not lose it.
 */
export function useReturnTo(): { path: string; state: { returnTo: string } | undefined } {
  const state = useLocation().state as { returnTo?: unknown } | null;
  // Only ever a path within the app: the state is ours, but a path is all it is trusted to be.
  const returnTo = typeof state?.returnTo === "string" && state.returnTo.startsWith("/") ? state.returnTo : undefined;
  return { path: returnTo ?? "/", state: returnTo ? { returnTo } : undefined };
}
