import { useState, type FormEvent } from "react";
import { AuthApiError, requestPasswordReset } from "./sessionApi";

const RESET_ERROR_MESSAGES: Record<string, string> = {
  invalid_request: "Please enter your email.",
};

export function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [submittedEmail, setSubmittedEmail] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) {
      return;
    }
    setFormError(null);
    const trimmed = email.trim();
    if (!trimmed) {
      setFormError("Please enter your email.");
      return;
    }
    setSubmitting(true);
    try {
      await requestPasswordReset(trimmed);
      setSubmittedEmail(trimmed);
    } catch (error) {
      const code = error instanceof AuthApiError ? error.code : "unknown";
      setFormError(
        RESET_ERROR_MESSAGES[code] ??
          "Unable to request a reset right now. Please try again.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="auth-shell">
      <main className="auth-card" aria-labelledby="forgot-password-title">
        <h1 id="forgot-password-title">Reset your password</h1>
        <p className="auth-subtitle">
          Enter your email and we&apos;ll send a reset link.
        </p>
        {submittedEmail ? (
          <p className="auth-message" role="status">
            If an account exists for {submittedEmail}, a reset link has been
            sent.
          </p>
        ) : (
          <form className="auth-form" onSubmit={handleSubmit}>
            <div className="auth-field">
              <label htmlFor="forgot-email">Email</label>
              <input
                id="forgot-email"
                type="email"
                name="email"
                autoComplete="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                required
              />
            </div>
            {formError ? (
              <p className="auth-message" role="alert">
                {formError}
              </p>
            ) : null}
            <button
              type="submit"
              className="auth-button"
              disabled={submitting}
            >
              {submitting ? "Sending…" : "Send reset link"}
            </button>
          </form>
        )}
        <p className="auth-toggle">
          <a className="auth-toggle__button" href="/login">
            Back to sign in
          </a>
        </p>
      </main>
    </div>
  );
}
