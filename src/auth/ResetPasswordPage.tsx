import { useEffect, useMemo, useState, type FormEvent } from "react";
import { AuthApiError, resetPassword } from "./sessionApi";

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
      <div className="auth-shell">
        <main className="auth-card" aria-labelledby="reset-password-title">
          <h1 id="reset-password-title">Set a new password</h1>
          <p className="auth-message" role="alert">
            Invalid reset link.
          </p>
          <p className="auth-toggle">
            <a className="auth-toggle__button" href="/forgot-password">
              Request a new reset link
            </a>
          </p>
        </main>
      </div>
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
    <div className="auth-shell">
      <main className="auth-card" aria-labelledby="reset-password-title">
        <h1 id="reset-password-title">Set a new password</h1>
        {succeeded ? (
          <>
            <p className="auth-message" role="status">
              Your password has been reset. You can now sign in.
            </p>
            <p className="auth-toggle">
              <a className="auth-toggle__button" href="/login">
                Back to sign in
              </a>
            </p>
          </>
        ) : (
          <form className="auth-form" onSubmit={handleSubmit}>
            <div className="auth-field">
              <label htmlFor="reset-password">New password</label>
              <input
                id="reset-password"
                type="password"
                name="password"
                autoComplete="new-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                required
              />
            </div>
            <div className="auth-field">
              <label htmlFor="reset-confirm">Confirm password</label>
              <input
                id="reset-confirm"
                type="password"
                name="confirmPassword"
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(event) => setConfirmPassword(event.target.value)}
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
              {submitting ? "Resetting…" : "Reset password"}
            </button>
          </form>
        )}
      </main>
    </div>
  );
}
