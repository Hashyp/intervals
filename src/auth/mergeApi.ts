import { getAntiforgeryToken } from "./sessionApi";

export type PendingMergeDetail = {
  primaryUserId: string;
  primaryDisplayName: string;
  primaryEmail: string | null;
  secondaryUserId: string;
  secondaryDisplayName: string;
  secondaryEmail: string | null;
  provider: string;
};

export async function getPendingMerge(): Promise<PendingMergeDetail | null> {
  const res = await fetch("/auth/providers/pending-merge", {
    credentials: "same-origin",
  });
  if (res.status === 404) {
    return null;
  }
  if (!res.ok) {
    throw new Error("pending_merge_failed");
  }
  return (await res.json()) as PendingMergeDetail;
}

export async function confirmMerge(): Promise<void> {
  const token = await getAntiforgeryToken();
  const res = await fetch("/auth/providers/merge", {
    method: "POST",
    credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": token },
  });
  if (!res.ok) {
    throw new Error("merge_failed");
  }
}

export async function cancelMerge(): Promise<void> {
  const token = await getAntiforgeryToken();
  await fetch("/auth/providers/merge/cancel", {
    method: "POST",
    credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": token },
  });
}
