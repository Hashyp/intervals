import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useAuth } from "./AuthProvider";
import {
  AuthApiError,
  getAntiforgeryToken,
  loginWithPassword,
  register,
} from "./sessionApi";
import { currentReturnUrl } from "./returnUrl";
import { AuthCard } from "./ui/AuthCard";
import { AuthField } from "./ui/AuthField";
import { AuthMessage } from "./ui/AuthMessage";
import { PasswordConfirmFields } from "./ui/PasswordConfirmFields";
import { SubmitButton } from "./ui/SubmitButton";

const ERROR_MESSAGES: Record<string, string> = {
  cancelled: "Sign-in was cancelled. You can try again.",
  provider_error:
    "The sign-in provider could not complete the request. Please retry.",
  unknown: "Something went wrong during sign-in. Please retry.",
  email_verified: "Your email has been verified. You can sign in now.",
  verification_failed:
    "We couldn't verify that link. It may have expired or already been used.",
  disabled: "This account has been disabled.",
};

const LOGIN_ERROR_MESSAGES: Record<string, string> = {
  invalid_credentials: "Incorrect email or password.",
  locked_out: "Too many attempts. Please try again in a few minutes.",
  disabled: "This account has been disabled.",
};

const REGISTER_ERROR_MESSAGES: Record<string, string> = {
  email_taken: "An account with that email already exists. Try signing in.",
  weak_password: "Please choose a stronger password (at least 8 characters).",
};

type Mode = "sign-in" | "create";

type SocialProvider = {
  id: "google" | "microsoft" | "x";
  label: string;
  buttonClass: string;
};

const SOCIAL_PROVIDERS: readonly SocialProvider[] = [
  { id: "google", label: "Google", buttonClass: "auth-button--google" },
  { id: "microsoft", label: "Microsoft", buttonClass: "auth-button--microsoft" },
  { id: "x", label: "X", buttonClass: "auth-button--x" },
];

const DEFAULT_PROVIDER_AVAILABILITY: Record<string, boolean> = {
  google: true,
  microsoft: true,
  x: true,
};

export function LoginPage() {
  const returnUrl = useMemo(() => currentReturnUrl(), []);
  const { refreshSession } = useAuth();

  const [mode, setMode] = useState<Mode>("sign-in");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [rememberMe, setRememberMe] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [csrfToken, setCsrfToken] = useState<string | null>(null);
  const [csrfLoading, setCsrfLoading] = useState(true);
  const [available, setAvailable] = useState<Record<string, boolean>>(
    DEFAULT_PROVIDER_AVAILABILITY,
  );

  const authMessage = useMemo(() => {
    if (typeof window === "undefined") {
      return null;
    }
    const params = new URLSearchParams(window.location.search);
    const code = params.get("auth");
    return code && ERROR_MESSAGES[code] ? ERROR_MESSAGES[code] : null;
  }, []);

  useEffect(() => {
    getAntiforgeryToken()
      .then((token) => setCsrfToken(token))
      .catch(() => setCsrfToken(null))
      .finally(() => setCsrfLoading(false));
    fetch("/api/auth/providers")
      .then((response) => (response.ok ? response.json() : null))
      .then(
        (data: {
          providers?: Array<{ id: string; available: boolean }>;
        } | null) => {
          if (data?.providers) {
            setAvailable(
              Object.fromEntries(
                data.providers.map((p) => [p.id, p.available] as const),
              ),
            );
          }
        },
      )
      .catch(() => {
        // Fail open: keep all providers available when availability is unknown.
      });
  }, []);

  const socialDisabled = csrfLoading || !csrfToken;

  function switchMode(next: Mode) {
    setMode(next);
    setFormError(null);
    setPassword("");
    setConfirmPassword("");
  }

  async function handleLoginSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) {
      return;
    }
    setFormError(null);
    setSubmitting(true);
    try {
      await loginWithPassword(email, password, rememberMe);
      window.history.replaceState({}, "", returnUrl);
      await refreshSession();
    } catch (error) {
      const code = error instanceof AuthApiError ? error.code : "unknown";
      setFormError(
        LOGIN_ERROR_MESSAGES[code] ?? "Sign-in failed. Please try again.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  async function handleRegisterSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) {
      return;
    }
    setFormError(null);
    if (!email.trim()) {
      setFormError("Please enter your email.");
      return;
    }
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
      await register(email, password);
      window.history.replaceState({}, "", returnUrl);
      await refreshSession();
    } catch (error) {
      const code = error instanceof AuthApiError ? error.code : "unknown";
      setFormError(
        REGISTER_ERROR_MESSAGES[code] ??
          "Could not create the account. Please try again.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AuthCard
      titleId="login-title"
      titleBrand
      subtitle="Sign in to start training your ear."
    >
      {authMessage ? <AuthMessage>{authMessage}</AuthMessage> : null}
      <label className="auth-remember">
        <input
          type="checkbox"
          checked={rememberMe}
          onChange={(event) => setRememberMe(event.target.checked)}
        />
        <span>Remember me</span>
      </label>
      {SOCIAL_PROVIDERS.map((provider) => {
        if (available[provider.id] === false) {
          return null;
        }
        return (
          <form
            key={provider.id}
            method="post"
            action={`/auth/login/${provider.id}`}
            className="auth-form"
          >
            <input type="hidden" name="returnUrl" value={returnUrl} />
            <input
              type="hidden"
              name="__RequestVerificationToken"
              value={csrfToken ?? ""}
            />
            <input
              type="hidden"
              name="rememberMe"
              value={String(rememberMe)}
            />
            <SubmitButton
              className={`auth-button ${provider.buttonClass}`}
              disabled={socialDisabled}
            >
              Continue with {provider.label}
            </SubmitButton>
          </form>
        );
      })}
      <div className="auth-divider" role="separator" aria-label="or" />

      {mode === "sign-in" ? (
        <form className="auth-form" onSubmit={handleLoginSubmit}>
          <AuthField id="login-email" label="Email">
            <input
              id="login-email"
              type="email"
              name="email"
              autoComplete="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
          </AuthField>
          <AuthField id="login-password" label="Password">
            <input
              id="login-password"
              type="password"
              name="password"
              autoComplete="current-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
          </AuthField>
          <p className="auth-toggle">
            <a className="auth-toggle__button" href="/forgot-password">
              Forgot password?
            </a>
          </p>
          {formError ? <AuthMessage>{formError}</AuthMessage> : null}
          <SubmitButton pending={submitting} pendingLabel="Signing in…">
            Sign in with email
          </SubmitButton>
        </form>
      ) : (
        <form className="auth-form" onSubmit={handleRegisterSubmit}>
          <AuthField id="register-email" label="Email">
            <input
              id="register-email"
              type="email"
              name="email"
              autoComplete="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
          </AuthField>
          <PasswordConfirmFields
            passwordId="register-password"
            passwordLabel="Password"
            passwordValue={password}
            onPasswordChange={setPassword}
            confirmId="register-confirm"
            confirmLabel="Confirm password"
            confirmValue={confirmPassword}
            onConfirmChange={setConfirmPassword}
          />
          {formError ? <AuthMessage>{formError}</AuthMessage> : null}
          <SubmitButton pending={submitting} pendingLabel="Creating account…">
            Create account
          </SubmitButton>
        </form>
      )}

      <p className="auth-toggle">
        {mode === "sign-in" ? (
          <button
            type="button"
            className="auth-toggle__button"
            onClick={() => switchMode("create")}
          >
            Create account
          </button>
        ) : (
          <button
            type="button"
            className="auth-toggle__button"
            onClick={() => switchMode("sign-in")}
          >
            Back to sign in
          </button>
        )}
      </p>

      <nav className="auth-links" aria-label="Legal and support">
        <a href="#">Privacy</a>
        <a href="#">Terms</a>
        <a href="#">Support</a>
      </nav>
    </AuthCard>
  );
}
