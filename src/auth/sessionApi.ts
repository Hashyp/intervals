export type SessionUser = {
  id: string;
  displayName: string;
  email: string | null;
  avatarUrl: string | null;
};

export type ProviderId = "google" | "x";

export type ProviderStatus = {
  id: ProviderId;
  label: string;
  linked: boolean;
};

export type Session = {
  user: SessionUser;
  providers: ProviderStatus[];
};

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
