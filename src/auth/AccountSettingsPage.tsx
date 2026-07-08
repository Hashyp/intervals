import { useEffect, useState, type FormEvent } from "react";
import { useAuth } from "./AuthProvider";
import {
  addPassword,
  AuthApiError,
  changePassword,
  getAccount,
  getAntiforgeryToken,
  unlinkProvider,
  type AccountDetail,
} from "./sessionApi";
import { AuthCard } from "./ui/AuthCard";
import { AuthField } from "./ui/AuthField";
import { AuthMessage } from "./ui/AuthMessage";
import { PasswordConfirmFields } from "./ui/PasswordConfirmFields";
import { SubmitButton } from "./ui/SubmitButton";

const CHANGE_ERROR_MESSAGES: Record<string, string> = {
  invalid_credentials: "Your current password is incorrect.",
  weak_password: "Please choose a stronger password (at least 8 characters).",
};

const ADD_ERROR_MESSAGES: Record<string, string> = {
  email_taken: "An account with that email already exists.",
  weak_password: "Please choose a stronger password (at least 8 characters).",
};

const UNLINK_ERROR_MESSAGES: Record<string, string> = {
  last_login_method:
    "You must keep at least one sign-in method. Add a password before unlinking this provider.",
};

const EXTERNAL_PROVIDERS = ["google", "microsoft", "x"] as const;

type Status = "idle" | "loading" | "ready" | "error";

export function AccountSettingsPage() {
  const { refreshSession } = useAuth();
  const [detail, setDetail] = useState<AccountDetail | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [status, setStatus] = useState<Status>("idle");

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [changeSubmitting, setChangeSubmitting] = useState(false);
  const [changeError, setChangeError] = useState<string | null>(null);
  const [changeSuccess, setChangeSuccess] = useState<string | null>(null);

  const [addEmail, setAddEmail] = useState("");
  const [addPasswordValue, setAddPasswordValue] = useState("");
  const [addConfirm, setAddConfirm] = useState("");
  const [addSubmitting, setAddSubmitting] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);
  const [addSuccess, setAddSuccess] = useState<string | null>(null);

  const [unlinkError, setUnlinkError] = useState<string | null>(null);
  const [unlinkBusy, setUnlinkBusy] = useState<string | null>(null);

  const [csrfToken, setCsrfToken] = useState<string | null>(null);
  const [csrfLoading, setCsrfLoading] = useState(true);
  const [providerAvailability, setProviderAvailability] = useState<
    Record<string, boolean>
  >({});

  async function loadDetail() {
    setStatus("loading");
    setLoadError(null);
    try {
      const data = await getAccount();
      setDetail(data);
      setStatus("ready");
    } catch (error) {
      setLoadError(
        error instanceof AuthApiError
          ? error.message
          : "Unable to load account details right now.",
      );
      setStatus("error");
    }
  }

  useEffect(() => {
    void loadDetail();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    let cancelled = false;
    setCsrfLoading(true);
    getAntiforgeryToken()
      .then((token) => {
        if (!cancelled) {
          setCsrfToken(token);
        }
      })
      .catch(() => {
        // Leave token null; link buttons stay disabled.
      })
      .finally(() => {
        if (!cancelled) {
          setCsrfLoading(false);
        }
      });
    fetch("/api/auth/providers", { credentials: "same-origin" })
      .then((response) =>
        response.ok ? response.json() : Promise.reject(new Error("providers")),
      )
      .then((data: { providers?: { id: string; available: boolean }[] }) => {
        if (cancelled || !data.providers) {
          return;
        }
        const map: Record<string, boolean> = {};
        for (const p of data.providers) {
          map[p.id] = p.available;
        }
        if (!cancelled) {
          setProviderAvailability(map);
        }
      })
      .catch(() => {
        // Fail open: an empty map treats every provider as available.
      });
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleChangeSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (changeSubmitting) {
      return;
    }
    setChangeError(null);
    setChangeSuccess(null);
    if (newPassword.length < 8) {
      setChangeError("Please choose a stronger password (at least 8 characters).");
      return;
    }
    if (newPassword !== confirmPassword) {
      setChangeError("Passwords do not match.");
      return;
    }
    setChangeSubmitting(true);
    try {
      await changePassword(currentPassword, newPassword);
      setChangeSuccess("Password updated.");
      setCurrentPassword("");
      setNewPassword("");
      setConfirmPassword("");
      await refreshSession();
    } catch (error) {
      const code = error instanceof AuthApiError ? error.code : "unknown";
      setChangeError(
        CHANGE_ERROR_MESSAGES[code] ?? "Unable to change password right now.",
      );
    } finally {
      setChangeSubmitting(false);
    }
  }

  async function handleAddSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (addSubmitting) {
      return;
    }
    setAddError(null);
    setAddSuccess(null);
    const trimmedEmail = addEmail.trim();
    if (!trimmedEmail) {
      setAddError("Please enter your email.");
      return;
    }
    if (addPasswordValue.length < 8) {
      setAddError("Please choose a stronger password (at least 8 characters).");
      return;
    }
    if (addPasswordValue !== addConfirm) {
      setAddError("Passwords do not match.");
      return;
    }
    setAddSubmitting(true);
    try {
      await addPassword(trimmedEmail, addPasswordValue);
      setAddSuccess(
        "Password added. Check your email to verify the address.",
      );
      setAddEmail("");
      setAddPasswordValue("");
      setAddConfirm("");
      await refreshSession();
      await loadDetail();
    } catch (error) {
      const code = error instanceof AuthApiError ? error.code : "unknown";
      setAddError(
        ADD_ERROR_MESSAGES[code] ?? "Unable to add password right now.",
      );
    } finally {
      setAddSubmitting(false);
    }
  }

  async function handleUnlink(providerId: string, label: string) {
    setUnlinkError(null);
    const confirmed = window.confirm(
      `Unlink ${label} from your account? You will not be able to sign in with it afterwards.`,
    );
    if (!confirmed) {
      return;
    }
    setUnlinkBusy(providerId);
    try {
      await unlinkProvider(providerId);
      await refreshSession();
      await loadDetail();
    } catch (error) {
      const code = error instanceof AuthApiError ? error.code : "unknown";
      setUnlinkError(
        UNLINK_ERROR_MESSAGES[code] ?? `Unable to unlink ${label} right now.`,
      );
    } finally {
      setUnlinkBusy(null);
    }
  }

  return (
    <AuthCard
      titleId="account-settings-title"
      title="Account settings"
      subtitle="Manage how you sign in to Intervals."
    >
      {loadError ? <AuthMessage>{loadError}</AuthMessage> : null}

      {status === "loading" || status === "idle" ? (
        <AuthMessage role="status">Loading account details…</AuthMessage>
      ) : null}

      {unlinkError ? <AuthMessage>{unlinkError}</AuthMessage> : null}

      {detail ? (
        <section aria-labelledby="sign-in-methods-title">
          <h2 id="sign-in-methods-title">Sign-in methods</h2>
          <ul className="auth-providers">
            {EXTERNAL_PROVIDERS.map((providerId) => {
              const provider = detail.providers.find(
                (p) => p.id === providerId,
              );
              if (!provider) {
                return null;
              }
              return (
                <li key={providerId} className="auth-providers__item">
                  <div>
                    <span className="auth-providers__label">
                      {provider.label}
                    </span>
                    <span className="auth-providers__status">
                      {provider.linked
                        ? provider.email
                          ? `Linked · ${provider.email}`
                          : "Linked"
                        : "Not linked"}
                    </span>
                  </div>
                  {provider.linked ? (
                    <button
                      type="button"
                      className="auth-button"
                      disabled={unlinkBusy === providerId}
                      onClick={() =>
                        void handleUnlink(providerId, provider.label)
                      }
                    >
                      {unlinkBusy === providerId
                        ? "Unlinking…"
                        : `Unlink ${provider.label}`}
                    </button>
                  ) : providerAvailability[providerId] !== false ? (
                    <form
                      method="post"
                      action={`/auth/providers/link/${providerId}`}
                      className="auth-form auth-form--inline"
                    >
                      <input
                        type="hidden"
                        name="returnUrl"
                        value="/account-settings"
                      />
                      <input
                        type="hidden"
                        name="__RequestVerificationToken"
                        value={csrfToken ?? ""}
                      />
                      <SubmitButton
                        disabled={csrfLoading || csrfToken === null}
                      >
                        Link {provider.label}
                      </SubmitButton>
                    </form>
                  ) : null}
                </li>
              );
            })}
            {(() => {
              const password = detail.providers.find(
                (p) => p.id === "password",
              );
              if (!password) {
                return null;
              }
              return (
                <li className="auth-providers__item">
                  <div>
                    <span className="auth-providers__label">
                      {password.label}
                    </span>
                    <span className="auth-providers__status">
                      {password.linked && password.email
                        ? `Linked · ${password.email}`
                        : password.linked
                          ? "Linked"
                          : "Not linked"}
                    </span>
                  </div>
                </li>
              );
            })()}
          </ul>
        </section>
      ) : null}

      {detail?.hasPassword ? (
        <section aria-labelledby="change-password-title">
          <h2 id="change-password-title">Change password</h2>
          {changeSuccess ? (
            <AuthMessage role="status">{changeSuccess}</AuthMessage>
          ) : null}
          <form className="auth-form" onSubmit={handleChangeSubmit}>
            <AuthField id="change-current" label="Current password">
              <input
                id="change-current"
                type="password"
                name="currentPassword"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(event) => setCurrentPassword(event.target.value)}
                required
              />
            </AuthField>
            <PasswordConfirmFields
              passwordId="change-new"
              passwordLabel="New password"
              passwordName="newPassword"
              passwordValue={newPassword}
              onPasswordChange={setNewPassword}
              confirmId="change-confirm"
              confirmLabel="Confirm new password"
              confirmValue={confirmPassword}
              onConfirmChange={setConfirmPassword}
            />
            {changeError ? <AuthMessage>{changeError}</AuthMessage> : null}
            <SubmitButton pending={changeSubmitting} pendingLabel="Updating…">
              Update password
            </SubmitButton>
          </form>
        </section>
      ) : null}

      {detail && !detail.hasPassword ? (
        <section aria-labelledby="add-password-title">
          <h2 id="add-password-title">Add a password</h2>
          {addSuccess ? (
            <AuthMessage role="status">{addSuccess}</AuthMessage>
          ) : null}
          <form className="auth-form" onSubmit={handleAddSubmit}>
            <AuthField id="add-email" label="Email">
              <input
                id="add-email"
                type="email"
                name="email"
                autoComplete="email"
                value={addEmail}
                onChange={(event) => setAddEmail(event.target.value)}
                required
              />
            </AuthField>
            <PasswordConfirmFields
              passwordId="add-password"
              passwordLabel="Password"
              passwordValue={addPasswordValue}
              onPasswordChange={setAddPasswordValue}
              confirmId="add-confirm"
              confirmLabel="Confirm password"
              confirmValue={addConfirm}
              onConfirmChange={setAddConfirm}
            />
            {addError ? <AuthMessage>{addError}</AuthMessage> : null}
            <SubmitButton pending={addSubmitting} pendingLabel="Adding…">
              Add password
            </SubmitButton>
          </form>
        </section>
      ) : null}

      <p className="auth-toggle">
        <a className="auth-toggle__button" href="/">
          Back to app
        </a>
      </p>
    </AuthCard>
  );
}
