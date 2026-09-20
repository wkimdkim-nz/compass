import { Link, useNavigate } from "react-router";
import { useLoginMutation, type Credentials } from "./authApi";
import { CredentialsForm } from "./CredentialsForm";
import { useReturnTo } from "./useReturnTo";
import { fieldErrors, isStatus } from "../../app/problemDetails";

export function LoginPage() {
  const [login, { isLoading, error }] = useLoginMutation();
  const navigate = useNavigate();
  const returnTo = useReturnTo();

  const submit = async (credentials: Credentials) => {
    const result = await login(credentials);
    if (!("error" in result)) navigate(returnTo.path, { replace: true });
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
        New here? <Link to="/register" state={returnTo.state}>Create an account</Link>
      </p>
    </main>
  );
}
