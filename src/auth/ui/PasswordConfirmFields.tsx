import type { ChangeEvent, ReactNode } from "react";
import { AuthField } from "./AuthField";

type PasswordConfirmFieldsProps = {
  passwordId: string;
  passwordLabel: ReactNode;
  passwordName?: string;
  passwordValue: string;
  onPasswordChange: (value: string) => void;
  confirmId: string;
  confirmLabel: ReactNode;
  confirmName?: string;
  confirmValue: string;
  onConfirmChange: (value: string) => void;
};

export function PasswordConfirmFields({
  passwordId,
  passwordLabel,
  passwordName = "password",
  passwordValue,
  onPasswordChange,
  confirmId,
  confirmLabel,
  confirmName = "confirmPassword",
  confirmValue,
  onConfirmChange,
}: PasswordConfirmFieldsProps) {
  return (
    <>
      <AuthField id={passwordId} label={passwordLabel}>
        <input
          id={passwordId}
          type="password"
          name={passwordName}
          autoComplete="new-password"
          value={passwordValue}
          onChange={(event: ChangeEvent<HTMLInputElement>) =>
            onPasswordChange(event.target.value)
          }
          required
        />
      </AuthField>
      <AuthField id={confirmId} label={confirmLabel}>
        <input
          id={confirmId}
          type="password"
          name={confirmName}
          autoComplete="new-password"
          value={confirmValue}
          onChange={(event: ChangeEvent<HTMLInputElement>) =>
            onConfirmChange(event.target.value)
          }
          required
        />
      </AuthField>
    </>
  );
}
