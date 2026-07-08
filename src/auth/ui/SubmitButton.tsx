import type { ButtonHTMLAttributes, ReactNode } from "react";

type SubmitButtonProps = {
  children: ReactNode;
  pendingLabel?: ReactNode;
  pending?: boolean;
  className?: string;
} & Omit<ButtonHTMLAttributes<HTMLButtonElement>, "className">;

export function SubmitButton({
  children,
  pendingLabel,
  pending = false,
  className,
  disabled,
  ...rest
}: SubmitButtonProps) {
  return (
    <button
      type="submit"
      className={className ?? "auth-button"}
      disabled={pending || disabled}
      {...rest}
    >
      {pending && pendingLabel ? pendingLabel : children}
    </button>
  );
}
