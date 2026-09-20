import { Link, useNavigate } from "react-router";
import { useRegisterMutation, type Credentials } from "./authApi";
import { CredentialsForm } from "./CredentialsForm";
import { useReturnTo } from "./useReturnTo";
import { fieldErrors } from "./problemDetails";

export function RegisterPage() {
  const [register, { isLoading, error }] = useRegisterMutation();
  const navigate = useNavigate();
  const returnTo = useReturnTo();

  const submit = async (credentials: Credentials) => {
    const result = await register(credentials);
    if (!("error" in result)) navigate(returnTo.path, { replace: true });
  };

  return (
    <main className="phase0-column">
      <CredentialsForm
        title="Create an account"
        submitLabel="Register"
        busy={isLoading}
        errors={fieldErrors(error)}
        onSubmit={submit}
      />
      <p className="mt-4 text-sm">
        Already have an account? <Link to="/login" state={returnTo.state}>Sign in</Link>
      </p>
    </main>
  );
}
