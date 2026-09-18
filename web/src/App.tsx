import { Link, Outlet, Route, Routes } from "react-router";
import { useGetMeQuery, useLogoutMutation } from "./features/auth/authApi";
import { LoginPage } from "./features/auth/LoginPage";
import { ProtectedRoute } from "./features/auth/ProtectedRoute";
import { RegisterPage } from "./features/auth/RegisterPage";
import { NotesPage } from "./features/notes/NotesPage";

function Layout() {
  const { data: me } = useGetMeQuery();
  const [logout] = useLogoutMutation();

  return (
    <div className="sheet">
      <header className="masthead">
        <Link to="/" className="brand">
          Compass
        </Link>
        {me && (
          <button type="button" className="btn btn-quiet" onClick={() => logout()}>
            Sign out
          </button>
        )}
      </header>
      <Outlet />
    </div>
  );
}

/** The route table. `main.tsx` wraps it in a browser router; the tests in a memory router. */
export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route element={<ProtectedRoute />}>
          <Route path="/" element={<NotesPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
