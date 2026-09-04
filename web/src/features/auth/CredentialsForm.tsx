import { useState, type FormEvent } from "react";

type Props = {
  title: string;
  submitLabel: string;
  busy: boolean;
  errors: Record<string, string[]>;
  /** A message that belongs to the form as a whole rather than a field. */
  formError?: string;
  onSubmit: (credentials: { email: string; password: string }) => void;
};

export function CredentialsForm({ title, submitLabel, busy, errors, formError, onSubmit }: Props) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit({ email, password });
  };

  return (
    <form onSubmit={submit} aria-labelledby="credentials-heading">
      <h1 id="credentials-heading">{title}</h1>
      {formError && <p role="alert">{formError}</p>}
      <label>
        Email
        <input
          type="email"
          name="email"
          autoComplete="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          aria-invalid={errors.email ? true : undefined}
        />
      </label>
      <FieldErrors messages={errors.email} />
      <label>
        Password
        <input
          type="password"
          name="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          aria-invalid={errors.password ? true : undefined}
        />
      </label>
      <FieldErrors messages={errors.password} />
      <button type="submit" disabled={busy}>
        {submitLabel}
      </button>
    </form>
  );
}

function FieldErrors({ messages }: { messages?: string[] }) {
  if (!messages?.length) return null;
  return (
    <ul role="alert">
      {messages.map((message) => (
        <li key={message}>{message}</li>
      ))}
    </ul>
  );
}
