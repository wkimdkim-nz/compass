import { Link, useNavigate } from "react-router";
import { useRegisterMutation, type Credentials } from "./authApi";
import { CredentialsForm } from "./CredentialsForm";
import { fieldErrors } from "./problemDetails";

export function RegisterPage() {
  const [register, { isLoading, error }] = useRegisterMutation();
  const navigate = useNavigate();

  const submit = async (credentials: Credentials) => {
    const result = await register(credentials);
    if (!("error" in result)) navigate("/", { replace: true });
  };

  return (
    <main>
      <CredentialsForm
        title="Create an account"
        submitLabel="Register"
        busy={isLoading}
        errors={fieldErrors(error)}
        onSubmit={submit}
      />
      <p>
        Already have an account? <Link to="/login">Sign in</Link>
      </p>
    </main>
  );
}
