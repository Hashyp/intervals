import { useMemo } from "react";
import { currentReturnUrl } from "./returnUrl";

const ERROR_MESSAGES: Record<string, string> = {
  cancelled: "Sign-in was cancelled. You can try again.",
  provider_error:
    "The sign-in provider could not complete the request. Please retry.",
  unknown: "Something went wrong during sign-in. Please retry.",
};

export function LoginPage() {
  const returnUrl = useMemo(() => currentReturnUrl(), []);
  const authMessage = useMemo(() => {
    if (typeof window === "undefined") {
      return null;
    }
    const params = new URLSearchParams(window.location.search);
    const code = params.get("auth");
    return code && ERROR_MESSAGES[code] ? ERROR_MESSAGES[code] : null;
  }, []);

  return (
    <div className="auth-shell">
      <main className="auth-card" aria-labelledby="login-title">
        <h1 id="login-title">
          Inter<span className="auth-brand">vals</span>
        </h1>
        <p className="auth-subtitle">Sign in to start training your ear.</p>
        {authMessage ? (
          <p className="auth-message" role="alert">
            {authMessage}
          </p>
        ) : null}
        <form method="post" action="/auth/login/google" className="auth-form">
          <input type="hidden" name="returnUrl" value={returnUrl} />
          <label className="auth-remember">
            <input
              type="checkbox"
              name="rememberMe"
              value="true"
              defaultChecked={false}
            />
            <span>Remember me</span>
          </label>
          <button type="submit" className="auth-button auth-button--google">
            Continue with Google
          </button>
        </form>
        <form method="post" action="/auth/login/x" className="auth-form">
          <input type="hidden" name="returnUrl" value={returnUrl} />
          <input type="hidden" name="rememberMe" value="false" />
          <button type="submit" className="auth-button auth-button--x">
            Continue with X
          </button>
        </form>
        <nav className="auth-links" aria-label="Legal and support">
          <a href="#">Privacy</a>
          <a href="#">Terms</a>
          <a href="#">Support</a>
        </nav>
      </main>
    </div>
  );
}
