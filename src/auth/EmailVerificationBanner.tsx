import { useState } from "react";
import { useAuth } from "./AuthProvider";
import { requestEmailVerification } from "./emailVerificationApi";

type EmailVerificationBannerProps = {
  onResendComplete?: () => void;
};

export function EmailVerificationBanner({
  onResendComplete,
}: EmailVerificationBannerProps) {
  const { session } = useAuth();
  const [sending, setSending] = useState(false);
  const [sent, setSent] = useState(false);
  const [error, setError] = useState(false);

  if (!session || session.user.emailVerified) {
    return null;
  }

  const handleResend = async () => {
    if (sending) {
      return;
    }
    setSending(true);
    setError(false);
    try {
      await requestEmailVerification();
      setSent(true);
      onResendComplete?.();
    } catch {
      setError(true);
    } finally {
      setSending(false);
    }
  };

  return (
    <span
      className="account-bar__verify"
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: "8px",
        marginRight: "auto",
        fontSize: "0.85rem",
      }}
    >
      <span>
        {sent
          ? "Verification email sent."
          : "Your email isn't verified yet."}
      </span>
      {!sent && (
        <button
          type="button"
          onClick={() => void handleResend()}
          disabled={sending}
          style={{
            background: "transparent",
            border: "1px solid var(--line-strong, #ccc)",
            color: "var(--ink, inherit)",
            borderRadius: "var(--r-sm, 4px)",
            padding: "4px 10px",
            cursor: sending ? "default" : "pointer",
            font: "inherit",
          }}
        >
          {sending ? "Sending..." : "Resend verification email"}
        </button>
      )}
      {error && (
        <span role="alert" style={{ color: "var(--wrong-deep, #b00020)" }}>
          Try again.
        </span>
      )}
    </span>
  );
}
