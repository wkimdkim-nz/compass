import { Link, Outlet, Route, Routes } from "react-router";
import { useGetMeQuery, useLogoutMutation } from "./features/auth/authApi";
import { LoginPage } from "./features/auth/LoginPage";
import { ProtectedRoute } from "./features/auth/ProtectedRoute";
import { RegisterPage } from "./features/auth/RegisterPage";
import { LandingPage } from "./features/landing/LandingPage";
import { CalculatorPage } from "./features/projection/CalculatorPage";
import { Disclaimer } from "./app/Disclaimer";
// PROTOTYPE routes (branch prototype/phase2-ui)
import { CalculatorHelpPrototype, GuidePrototype, MarkerPrototype } from "./features/guide/GuidePrototype";
import { DeleteMessagePrototype, LandingListPrototype, ProjectionsPagePrototype } from "./features/projection/ProjectionListPrototype";

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
          <nav aria-label="Account">
            <Link to="/prototype/calculator-help">Calculator (proto)</Link>
            <Link to="/prototype/landing">Landing (proto)</Link>
            <Link to="/prototype/guide">Guide</Link>
            <Link to="/prototype/projections">My projections</Link>
            {me ? (
              <button type="button" className="link" onClick={() => logout()}>
                Sign out
              </button>
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
        <Route path="/prototype/calculator-help" element={<CalculatorHelpPrototype />} />
        <Route path="/prototype/guide" element={<GuidePrototype />} />
        <Route path="/prototype/landing" element={<LandingListPrototype />} />
        <Route path="/prototype/projections" element={<ProjectionsPagePrototype />} />
        <Route path="/prototype/marker" element={<MarkerPrototype />} />
        <Route path="/prototype/deleted" element={<DeleteMessagePrototype />} />
        <Route element={<ProtectedRoute />}>
          <Route path="/projections/:ticker" element={<CalculatorPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
