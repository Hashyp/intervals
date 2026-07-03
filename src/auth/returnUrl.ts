export function sanitizeReturnUrl(raw: string | null | undefined): string {
  if (!raw) {
    return "/";
  }
  const url = raw;
  if (url.includes("\\")) {
    return "/";
  }
  if (url.startsWith("//")) {
    return "/";
  }
  if (!url.startsWith("/")) {
    return "/";
  }
  try {
    const parsed = new URL(url, "http://intervals.local");
    if (parsed.host !== "intervals.local") {
      return "/";
    }
    const reconstructed = parsed.pathname + parsed.search + parsed.hash;
    return reconstructed || "/";
  } catch {
    return "/";
  }
}

export function currentReturnUrl(): string {
  if (typeof window === "undefined") {
    return "/";
  }
  return sanitizeReturnUrl(window.location.pathname + window.location.search);
}
