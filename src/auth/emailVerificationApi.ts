import { getAntiforgeryToken } from "./sessionApi";

export async function requestEmailVerification(): Promise<void> {
  const token = await getAntiforgeryToken();
  const response = await fetch("/auth/email-verification/request", {
    method: "POST",
    credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": token },
  });
  if (!response.ok) {
    throw new Error("request_email_verification_failed");
  }
}
