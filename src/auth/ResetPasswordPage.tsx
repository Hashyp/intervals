import { useEffect, useMemo, useState, type FormEvent } from "react";
import { AuthApiError, resetPassword } from "./sessionApi";
import { AuthCard } from "./ui/AuthCard";
import { AuthMessage } from "./ui/AuthMessage";
import { PasswordConfirmFields } from "./ui/PasswordConfirmFields";
import { SubmitButton } from "./ui/SubmitButton";

const RESET_ERROR_MESSAGES: Record<string, string> = {
  weak_password: "Please choose a stronger password (at least 8 characters).",
};

function readTokenFromLocation(): string | null {
  if (typeof window === "undefined") {
    return null;
  }
  const params = new URLSearchParams(window.location.search);
  const token = params.get("token");
  return token && token.trim() ? token : null;
}

export function ResetPasswordPage() {
  const token = useMemo(() => readTokenFromLocation(), []);
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [succeeded, setSucceeded] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  useEffect(() => {
    if (typeof window === "undefined") {
      return;
    }
    const url = new URL(window.location.href);
    if (!url.searchParams.has("token")) {
      return;
    }
    url.searchParams.delete("token");
    window.history.replaceState({}, "", url.pathname + url.search + url.hash);
  }, []);

  if (!token) {
    return (
      <AuthCard titleId="reset-password-title" title="Set a new password">
        <AuthMessage>Invalid reset link.</AuthMessage>
        <p className="auth-toggle">
          <a className="auth-toggle__button" href="/forgot-password">
            Request a new reset link
          </a>
        </p>
      </AuthCard>
    );
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) {
      return;
    }
    setFormError(null);
    if (password.length < 8) {
      setFormError("Please choose a stronger password (at least 8 characters).");
      return;
    }
    if (password !== confirmPassword) {
      setFormError("Passwords do not match.");
      return;
    }
    setSubmitting(true);
    try {
      await resetPassword(token!, password);
      setSucceeded(true);
    } catch (error) {
      const code = error instanceof AuthApiError ? error.code : "unknown";
      setFormError(
        RESET_ERROR_MESSAGES[code] ??
          "Unable to reset the password right now. Please try again.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AuthCard titleId="reset-password-title" title="Set a new password">
      {succeeded ? (
        <>
          <AuthMessage role="status">
            Your password has been reset. You can now sign in.
          </AuthMessage>
          <p className="auth-toggle">
            <a className="auth-toggle__button" href="/login">
              Back to sign in
            </a>
          </p>
        </>
      ) : (
        <form className="auth-form" onSubmit={handleSubmit}>
          <PasswordConfirmFields
            passwordId="reset-password"
            passwordLabel="New password"
            passwordValue={password}
            onPasswordChange={setPassword}
            confirmId="reset-confirm"
            confirmLabel="Confirm password"
            confirmValue={confirmPassword}
            onConfirmChange={setConfirmPassword}
          />
          {formError ? <AuthMessage>{formError}</AuthMessage> : null}
          <SubmitButton pending={submitting} pendingLabel="Resetting…">
            Reset password
          </SubmitButton>
        </form>
      )}
    </AuthCard>
  );
}
