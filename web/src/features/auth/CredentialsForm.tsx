import { useState, type FormEvent } from "react";
import type { Credentials } from "./authApi";

type Props = {
  title: string;
  submitLabel: string;
  busy: boolean;
  errors: Record<string, string[]>;
  /** A message that belongs to the form as a whole rather than a field. */
  formError?: string;
  onSubmit: (credentials: Credentials) => void;
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
      {formError && (
        <p role="alert" className="statement-note">
          {formError}
        </p>
      )}
      <FieldErrors messages={errors.general} />
      <label className="field">
        Email
        <input
          className="entry"
          type="email"
          name="email"
          autoComplete="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          aria-invalid={errors.email ? true : undefined}
        />
      </label>
      <FieldErrors messages={errors.email} />
      <label className="field">
        Password
        <input
          className="entry"
          type="password"
          name="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          aria-invalid={errors.password ? true : undefined}
        />
      </label>
      <FieldErrors messages={errors.password} />
      <button type="submit" className="btn" disabled={busy}>
        {submitLabel}
      </button>
    </form>
  );
}

function FieldErrors({ messages }: { messages?: string[] }) {
  if (!messages?.length) return null;
  return (
    <ul role="alert" className="statement-note">
      {messages.map((message) => (
        <li key={message}>{message}</li>
      ))}
    </ul>
  );
}
