import { Link, Outlet, Route, Routes } from "react-router";
import { useGetMeQuery, useLogoutMutation } from "./features/auth/authApi";
import { LoginPage } from "./features/auth/LoginPage";
import { ProtectedRoute } from "./features/auth/ProtectedRoute";
import { RegisterPage } from "./features/auth/RegisterPage";
import { GuidePage } from "./features/guide/GuidePage";
import { LandingPage } from "./features/landing/LandingPage";
import { CalculatorPage } from "./features/projection/CalculatorPage";
import { ProjectionsPage } from "./features/projection/ProjectionsPage";
import { Disclaimer } from "./app/Disclaimer";
import { PROJECTIONS_PATH } from "./app/paths";
import { Toaster } from "./app/Toaster";

function Layout() {
  const { data: me } = useGetMeQuery();
  const [logout] = useLogoutMutation();

  return (
    <div className="page">
      <div className="sheet">
        <header className="masthead">
          <Link to="/" className="brand">
            Compass
          </Link>
          <nav aria-label="Site">
            <Link to="/guide">Guide</Link>
            {me ? (
              <>
                <Link to={PROJECTIONS_PATH}>My projections</Link>
                <button type="button" className="link" onClick={() => logout()}>
                  Sign out
                </button>
              </>
            ) : (
              <>
                <Link to="/login">Sign in</Link>
                <Link to="/register">Register</Link>
              </>
            )}
          </nav>
        </header>
        <Outlet />
      </div>
      <footer className="footer">
        <div className="sheet">
          <Disclaimer as="footer" />
          <p>Fundamentals: SEC EDGAR. Quotes: Finnhub.</p>
        </div>
      </footer>
      {/* In the layout, not a page: a toast shown before a route change is still there after it. */}
      <Toaster />
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
        <Route path="/" element={<LandingPage />} />
        <Route path="/guide" element={<GuidePage />} />
        <Route element={<ProtectedRoute />}>
          <Route path={PROJECTIONS_PATH} element={<ProjectionsPage />} />
          <Route path="/projections/:ticker" element={<CalculatorPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
