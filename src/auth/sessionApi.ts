export type SessionUser = {
  id: string;
  displayName: string;
  email: string | null;
  avatarUrl: string | null;
  emailVerified: boolean;
};

export type ProviderId = "google" | "microsoft" | "x" | "password";

export type ProviderStatus = {
  id: ProviderId;
  label: string;
  linked: boolean;
};

export type Session = {
  user: SessionUser;
  providers: ProviderStatus[];
};

export class AuthApiError extends Error {
  readonly status: number;
  readonly code: string;
  constructor(status: number, code: string, message?: string) {
    super(message ?? code);
    this.name = "AuthApiError";
    this.status = status;
    this.code = code;
  }
}

type AuthErrorBody = {
  code?: string;
  message?: string;
  correlationId?: string;
};

async function readErrorBody(response: Response): Promise<AuthErrorBody> {
  try {
    return (await response.json()) as AuthErrorBody;
  } catch {
    return {};
  }
}

export async function getSession(): Promise<Session | null> {
  const response = await fetch("/api/session", { credentials: "same-origin" });
  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`Session request failed: ${response.status}`);
  }
  return (await response.json()) as Session;
}

export async function getAntiforgeryToken(): Promise<string> {
  const response = await fetch("/auth/antiforgery-token", {
    credentials: "same-origin",
  });
  if (!response.ok) {
    throw new Error("Unable to load antiforgery token");
  }
  const data = (await response.json()) as { token: string };
  return data.token;
}

export async function signOut(): Promise<void> {
  const token = await getAntiforgeryToken();
  await fetch("/auth/logout", {
    method: "POST",
    credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": token },
  });
}

async function postAuthForm(
  url: string,
  payload: Record<string, unknown>,
): Promise<void> {
  const token = await getAntiforgeryToken();
  const response = await fetch(url, {
    method: "POST",
    credentials: "same-origin",
    headers: {
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": token,
    },
    body: JSON.stringify(payload),
  });
  if (!response.ok) {
    const body = await readErrorBody(response);
    throw new AuthApiError(
      response.status,
      body.code ?? "unknown",
      body.message,
    );
  }
}

export async function register(
  email: string,
  password: string,
): Promise<void> {
  await postAuthForm("/auth/register", { email, password });
}

export async function loginWithPassword(
  email: string,
  password: string,
  rememberMe: boolean,
): Promise<void> {
  await postAuthForm("/auth/login/password", { email, password, rememberMe });
}

export async function requestPasswordReset(email: string): Promise<void> {
  await postAuthForm("/auth/password/forgot", { email });
}

export async function resetPassword(
  token: string,
  password: string,
): Promise<void> {
  await postAuthForm("/auth/password/reset", { token, password });
}

export type AccountProvider = {
  id: ProviderId;
  label: string;
  linked: boolean;
  email: string | null;
  lastLoginUtc: string | null;
};

export type AccountDetail = {
  userId: string;
  displayName: string;
  email: string | null;
  emailVerified: boolean;
  hasPassword: boolean;
  providers: AccountProvider[];
};

export async function getAccount(): Promise<AccountDetail> {
  const response = await fetch("/auth/account", { credentials: "same-origin" });
  if (!response.ok) {
    const body = await readErrorBody(response);
    throw new AuthApiError(
      response.status,
      body.code ?? "unknown",
      body.message ?? "Failed to load account",
    );
  }
  return (await response.json()) as AccountDetail;
}

export async function changePassword(
  currentPassword: string,
  newPassword: string,
): Promise<void> {
  await postAuthForm("/auth/account/password/change", {
    currentPassword,
    newPassword,
  });
}

export async function addPassword(
  email: string,
  newPassword: string,
): Promise<void> {
  await postAuthForm("/auth/account/password/add", { email, newPassword });
}

export async function unlinkProvider(provider: string): Promise<void> {
  const token = await getAntiforgeryToken();
  const response = await fetch(
    `/auth/account/providers/${encodeURIComponent(provider)}`,
    {
      method: "DELETE",
      credentials: "same-origin",
      headers: { "X-CSRF-TOKEN": token },
    },
  );
  if (!response.ok) {
    const body = await readErrorBody(response);
    throw new AuthApiError(
      response.status,
      body.code ?? "unknown",
      body.message,
    );
  }
}
