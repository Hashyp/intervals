import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { useAuthMock, requestEmailVerificationMock } = vi.hoisted(() => ({
  useAuthMock: vi.fn(),
  requestEmailVerificationMock: vi.fn(),
}));

vi.mock("./AuthProvider", () => ({
  useAuth: () => useAuthMock(),
}));

vi.mock("./emailVerificationApi", () => ({
  requestEmailVerification: () => requestEmailVerificationMock(),
}));

import { EmailVerificationBanner } from "./EmailVerificationBanner";

function sessionWith(overrides: Record<string, unknown> = {}) {
  return {
    user: {
      id: "u",
      displayName: "Avery",
      email: "avery@example.com",
      avatarUrl: null,
      emailVerified: false,
      ...overrides,
    },
    providers: [],
  };
}

describe("EmailVerificationBanner", () => {
  beforeEach(() => {
    requestEmailVerificationMock.mockReset();
    useAuthMock.mockReset();
  });
  afterEach(() => {
    vi.clearAllMocks();
  });

  it("renders the banner and resend button when email is not verified", () => {
    useAuthMock.mockReturnValue({ session: sessionWith(), loading: false });
    render(<EmailVerificationBanner />);
    expect(
      screen.getByText(/your email isn't verified yet/i),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /resend verification email/i }),
    ).toBeInTheDocument();
  });

  it("does not render when email is already verified", () => {
    useAuthMock.mockReturnValue({
      session: sessionWith({ emailVerified: true }),
      loading: false,
    });
    const { container } = render(<EmailVerificationBanner />);
    expect(container).toBeEmptyDOMElement();
  });

  it("does not render when there is no session", () => {
    useAuthMock.mockReturnValue({ session: null, loading: false });
    const { container } = render(<EmailVerificationBanner />);
    expect(container).toBeEmptyDOMElement();
  });

  it("calls requestEmailVerification and shows the sent state on click", async () => {
    requestEmailVerificationMock.mockResolvedValue(undefined);
    useAuthMock.mockReturnValue({ session: sessionWith(), loading: false });
    render(<EmailVerificationBanner />);

    const button = screen.getByRole("button", { name: /resend verification email/i });
    await button.click();

    expect(requestEmailVerificationMock).toHaveBeenCalledTimes(1);
    expect(
      await screen.findByText(/verification email sent/i),
    ).toBeInTheDocument();
  });

  it("shows an error affordance when resend fails", async () => {
    requestEmailVerificationMock.mockRejectedValue(new Error("boom"));
    useAuthMock.mockReturnValue({ session: sessionWith(), loading: false });
    render(<EmailVerificationBanner />);

    const button = screen.getByRole("button", { name: /resend verification email/i });
    await button.click();

    expect(requestEmailVerificationMock).toHaveBeenCalledTimes(1);
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /resend verification email/i }),
    ).toBeInTheDocument();
  });
});
