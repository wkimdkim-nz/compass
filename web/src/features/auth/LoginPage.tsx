import { Link, useNavigate } from "react-router";
import { useLoginMutation } from "./authApi";
import { CredentialsForm } from "./CredentialsForm";
import { fieldErrors, isStatus } from "./problemDetails";

export function LoginPage() {
  const [login, { isLoading, error }] = useLoginMutation();
  const navigate = useNavigate();

  const submit = async (credentials: { email: string; password: string }) => {
    const result = await login(credentials);
    if (!("error" in result)) navigate("/", { replace: true });
  };

  return (
    <main>
      <CredentialsForm
        title="Sign in"
        submitLabel="Sign in"
        busy={isLoading}
        errors={fieldErrors(error)}
        formError={isStatus(error, 401) ? "Invalid email or password." : undefined}
        onSubmit={submit}
      />
      <p>
        New here? <Link to="/register">Create an account</Link>
      </p>
    </main>
  );
}
