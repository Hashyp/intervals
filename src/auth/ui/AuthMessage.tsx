import type { ReactNode } from "react";

type AuthMessageProps = {
  children: ReactNode;
  role?: "alert" | "status";
};

export function AuthMessage({ children, role = "alert" }: AuthMessageProps) {
  return (
    <p className="auth-message" role={role}>
      {children}
    </p>
  );
}
