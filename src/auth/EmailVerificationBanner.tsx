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
    <span className="account-bar__verify">
      <span className="account-bar__verify-text">
        {sent
          ? "Verification email sent."
          : "Your email isn't verified yet."}
      </span>
      {!sent && (
        <button
          type="button"
          className="account-bar__verify-button"
          onClick={() => void handleResend()}
          disabled={sending}
        >
          {sending ? "Sending..." : "Resend verification email"}
        </button>
      )}
      {error && (
        <span className="account-bar__verify-error" role="alert">
          Try again.
        </span>
      )}
    </span>
  );
}
