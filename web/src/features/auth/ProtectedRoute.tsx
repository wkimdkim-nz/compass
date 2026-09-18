import { Navigate, Outlet } from "react-router";
import { useGetMeQuery } from "./authApi";

/**
 * Guards a route on the session query. Loading is never treated as logged out: while the
 * session is still being determined we show a loading state, not the logged-out view.
 */
export function ProtectedRoute() {
  const { isLoading, isSuccess } = useGetMeQuery();

  if (isLoading) {
    return <p role="status" className="mt-8 text-sm text-ink-2">
        Checking your session…
      </p>;
  }
  if (!isSuccess) {
    return <Navigate to="/login" replace />;
  }
  return <Outlet />;
}
