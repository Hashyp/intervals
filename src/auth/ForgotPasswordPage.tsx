import { useState, type FormEvent } from "react";
import { AuthApiError, requestPasswordReset } from "./sessionApi";
import { AuthCard } from "./ui/AuthCard";
import { AuthField } from "./ui/AuthField";
import { AuthMessage } from "./ui/AuthMessage";
import { SubmitButton } from "./ui/SubmitButton";

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
    <AuthCard
      titleId="forgot-password-title"
      title="Reset your password"
      subtitle="Enter your email and we'll send a reset link."
    >
      {submittedEmail ? (
        <AuthMessage role="status">
          If an account exists for {submittedEmail}, a reset link has been sent.
        </AuthMessage>
      ) : (
        <form className="auth-form" onSubmit={handleSubmit}>
          <AuthField id="forgot-email" label="Email">
            <input
              id="forgot-email"
              type="email"
              name="email"
              autoComplete="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
          </AuthField>
          {formError ? <AuthMessage>{formError}</AuthMessage> : null}
          <SubmitButton pending={submitting} pendingLabel="Sending…">
            Send reset link
          </SubmitButton>
        </form>
      )}
      <p className="auth-toggle">
        <a className="auth-toggle__button" href="/login">
          Back to sign in
        </a>
      </p>
    </AuthCard>
  );
}
