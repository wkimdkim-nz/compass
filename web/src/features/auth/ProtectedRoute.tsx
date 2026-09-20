import { Navigate, Outlet, useLocation } from "react-router";
import { useGetMeQuery } from "./authApi";

/**
 * Guards a route on the session query. Loading is never treated as logged out: while the
 * session is still being determined we show a loading state, not the logged-out view. The
 * address asked for goes along to sign in, which returns to it (see `useReturnTo`).
 */
export function ProtectedRoute() {
  const { isLoading, isSuccess } = useGetMeQuery();
  const location = useLocation();

  if (isLoading) {
    return (
      <p role="status" className="note mt-8">
        Checking your session…
      </p>
    );
  }
  if (!isSuccess) {
    return <Navigate to="/login" replace state={{ returnTo: location.pathname }} />;
  }
  return <Outlet />;
}
