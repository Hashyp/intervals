import { useEffect, type ReactNode } from "react";
import { useAuth } from "./AuthProvider";
import { LoginPage } from "./LoginPage";
import { AccountBar } from "./AccountBar";

export function ProtectedApp({ children }: { children: ReactNode }) {
  const { loading, authenticated } = useAuth();

  useEffect(() => {
    if (loading) {
      return;
    }
    const onLogin =
      window.location.pathname === "/login" ||
      window.location.pathname.startsWith("/intervals/login");
    if (!authenticated && !onLogin) {
      window.history.replaceState({}, "", "/login");
    } else if (authenticated && onLogin) {
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
    return <LoginPage />;
  }

  return (
    <>
      <AccountBar />
      {children}
    </>
  );
}
