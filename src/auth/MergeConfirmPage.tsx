import { useEffect, useState } from "react";
import { useAuth } from "./AuthProvider";
import {
  cancelMerge,
  confirmMerge,
  getPendingMerge,
  type PendingMergeDetail,
} from "./mergeApi";
import { AuthCard } from "./ui/AuthCard";
import { AuthMessage } from "./ui/AuthMessage";

function labelProvider(provider: string): string {
  switch (provider) {
    case "google":
      return "Google";
    case "microsoft":
      return "Microsoft";
    case "x":
      return "X";
    default:
      return provider;
  }
}

export function MergeConfirmPage() {
  const { refreshSession } = useAuth();
  const [loading, setLoading] = useState(true);
  const [pending, setPending] = useState<PendingMergeDetail | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    getPendingMerge()
      .then((detail) => {
        if (!cancelled) {
          setPending(detail);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setError("Unable to load the pending account merge.");
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleConfirm() {
    if (submitting) {
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await confirmMerge();
      await refreshSession();
      window.location.href = "/account-settings";
    } catch {
      setError("Unable to complete the merge right now. Please try again.");
      setSubmitting(false);
    }
  }

  async function handleCancel() {
    if (submitting) {
      return;
    }
    setSubmitting(true);
    try {
      await cancelMerge();
    } catch {
      // Best-effort: navigate away regardless.
    }
    window.location.href = "/account-settings";
  }

  if (loading) {
    return (
      <AuthCard titleId="merge-confirm-title" title="Confirm account merge">
        <AuthMessage role="status">Loading…</AuthMessage>
      </AuthCard>
    );
  }

  if (!pending) {
    return (
      <AuthCard titleId="merge-confirm-title" title="Confirm account merge">
        <AuthMessage role="status">No pending account merge.</AuthMessage>
        <p className="auth-toggle">
          <a className="auth-toggle__button" href="/account-settings">
            Back to account settings
          </a>
        </p>
      </AuthCard>
    );
  }

  const providerLabel = labelProvider(pending.provider);

  return (
    <AuthCard titleId="merge-confirm-title" title="Confirm account merge">
      <p className="auth-subtitle">
        Merge {pending.secondaryDisplayName} into {pending.primaryDisplayName}?
        The {providerLabel} login and any credentials will move to{" "}
        {pending.primaryDisplayName}.
      </p>
      <ul className="auth-merge-accounts">
        <li>
          <strong>Primary:</strong> {pending.primaryDisplayName}
          {pending.primaryEmail ? ` (${pending.primaryEmail})` : ""}
        </li>
        <li>
          <strong>Secondary:</strong> {pending.secondaryDisplayName}
          {pending.secondaryEmail ? ` (${pending.secondaryEmail})` : ""}
        </li>
      </ul>
      {error ? <AuthMessage>{error}</AuthMessage> : null}
      <div className="auth-form-actions">
        <button
          type="button"
          className="auth-button"
          onClick={handleConfirm}
          disabled={submitting}
        >
          {submitting ? "Merging…" : "Confirm merge"}
        </button>
        <button
          type="button"
          className="auth-toggle__button"
          onClick={handleCancel}
          disabled={submitting}
        >
          Cancel
        </button>
      </div>
    </AuthCard>
  );
}
