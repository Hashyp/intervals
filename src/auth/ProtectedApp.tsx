import { useEffect, type ReactNode } from "react";
import { useAuth } from "./AuthProvider";
import { LoginPage } from "./LoginPage";
import { ForgotPasswordPage } from "./ForgotPasswordPage";
import { ResetPasswordPage } from "./ResetPasswordPage";
import { AccountSettingsPage } from "./AccountSettingsPage";
import { MergeConfirmPage } from "./MergeConfirmPage";
import { AccountBar } from "./AccountBar";

const ANON_PUBLIC_PATHS = ["/login", "/forgot-password", "/reset-password"];

function isAnonymousAllowedPath(pathname: string): boolean {
  if (ANON_PUBLIC_PATHS.includes(pathname)) {
    return true;
  }
  return pathname.startsWith("/intervals/login");
}

export function ProtectedApp({ children }: { children: ReactNode }) {
  const { loading, authenticated } = useAuth();

  useEffect(() => {
    if (loading) {
      return;
    }
    const onPublicAnonPath = isAnonymousAllowedPath(window.location.pathname);
    if (!authenticated && !onPublicAnonPath) {
      window.history.replaceState({}, "", "/login");
    } else if (authenticated && onPublicAnonPath) {
      window.history.replaceState({}, "", "/");
    }
  }, [loading, authenticated]);

  if (loading) {
    return (
      <div className="auth-loading" role="status" aria-live="polite">
        Loading…
      </div>
    );
  }

  if (!authenticated) {
    const pathname = window.location.pathname;
    if (pathname === "/forgot-password") {
      return <ForgotPasswordPage />;
    }
    if (pathname === "/reset-password") {
      return <ResetPasswordPage />;
    }
    return <LoginPage />;
  }

  const pathname = window.location.pathname;
  if (pathname === "/account-settings") {
    return (
      <>
        <AccountBar />
        <AccountSettingsPage />
      </>
    );
  }
  if (pathname === "/merge-confirm") {
    return (
      <>
        <AccountBar />
        <MergeConfirmPage />
      </>
    );
  }

  return (
    <>
      <AccountBar />
      {children}
    </>
  );
}
