import type { ReactNode } from "react";

type AuthCardProps = {
  titleId: string;
  title?: ReactNode;
  titleBrand?: boolean;
  subtitle?: ReactNode;
  children?: ReactNode;
};

export function AuthCard({
  titleId,
  title,
  titleBrand,
  subtitle,
  children,
}: AuthCardProps) {
  return (
    <div className="auth-shell">
      <main className="auth-card" aria-labelledby={titleId}>
        <h1 id={titleId}>
          {titleBrand ? (
            <>
              Inter<span className="auth-brand">vals</span>
            </>
          ) : (
            title
          )}
        </h1>
        {subtitle ? <p className="auth-subtitle">{subtitle}</p> : null}
        {children}
      </main>
    </div>
  );
}
