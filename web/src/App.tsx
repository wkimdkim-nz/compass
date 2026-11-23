import { Link, Outlet, Route, Routes } from "react-router";
import { useGetMeQuery, useLogoutMutation } from "./features/auth/authApi";
import { LoginPage } from "./features/auth/LoginPage";
import { ProtectedRoute } from "./features/auth/ProtectedRoute";
import { RegisterPage } from "./features/auth/RegisterPage";
import { LandingPage } from "./features/landing/LandingPage";
import { CalculatorPage } from "./features/projection/CalculatorPage";
import { Disclaimer } from "./app/Disclaimer";
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
          <nav aria-label="Account">
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
        <Route element={<ProtectedRoute />}>
          <Route path="/projections/:ticker" element={<CalculatorPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
