import type { ReactNode } from "react";

type AuthFieldProps = {
  id: string;
  label: ReactNode;
  children: ReactNode;
};

export function AuthField({ id, label, children }: AuthFieldProps) {
  return (
    <div className="auth-field">
      <label htmlFor={id}>{label}</label>
      {children}
    </div>
  );
}
