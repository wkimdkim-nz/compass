import { Link, useNavigate } from "react-router";
import { useLoginMutation, type Credentials } from "./authApi";
import { CredentialsForm } from "./CredentialsForm";
import { fieldErrors, isStatus } from "./problemDetails";

export function LoginPage() {
  const [login, { isLoading, error }] = useLoginMutation();
  const navigate = useNavigate();

  const submit = async (credentials: Credentials) => {
    const result = await login(credentials);
    if (!("error" in result)) navigate("/", { replace: true });
  };

  return (
    <main className="phase0-column">
      <CredentialsForm
        title="Sign in"
        submitLabel="Sign in"
        busy={isLoading}
        errors={fieldErrors(error)}
        formError={isStatus(error, 401) ? "Invalid email or password." : undefined}
        onSubmit={submit}
      />
      <p className="mt-4 text-sm">
        New here? <Link to="/register">Create an account</Link>
      </p>
    </main>
  );
}
