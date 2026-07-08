import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { resetPassword, AuthApiErrorMock } = vi.hoisted(() => ({
  resetPassword: vi.fn(),
  AuthApiErrorMock: class AuthApiError extends Error {
    readonly status: number;
    readonly code: string;
    constructor(status: number, code: string, message?: string) {
      super(message ?? code);
      this.status = status;
      this.code = code;
    }
  },
}));

vi.mock("./sessionApi", () => ({
  AuthApiError: AuthApiErrorMock,
  resetPassword,
}));

import { ResetPasswordPage } from "./ResetPasswordPage";
import { AuthApiError } from "./sessionApi";

function mockAuthFetch() {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => Response.json({ token: "tok" })),
  );
}

describe("ResetPasswordPage", () => {
  beforeEach(() => {
    resetPassword.mockReset();
    mockAuthFetch();
    window.history.replaceState({}, "", "/reset-password?token=valid-token");
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("renders an error when no token is present in the url", () => {
    window.history.replaceState({}, "", "/reset-password");
    render(<ResetPasswordPage />);
    expect(screen.getByRole("alert")).toHaveTextContent(/invalid reset link/i);
  });

  it("calls resetPassword with the token and password on submit", async () => {
    resetPassword.mockResolvedValue(undefined);

    render(<ResetPasswordPage />);
    fireEvent.change(screen.getByLabelText(/^new password$/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.change(screen.getByLabelText(/confirm password/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /reset password/i }),
    );

    await waitFor(() => expect(resetPassword).toHaveBeenCalledTimes(1));
    expect(resetPassword).toHaveBeenCalledWith("valid-token", "brand-new-pw-7");
  });

  it("scrubs the token from the url on mount but keeps it for submission", async () => {
    resetPassword.mockResolvedValue(undefined);
    window.history.replaceState({}, "", "/reset-password?token=abc");
    const replaceSpy = vi.spyOn(window.history, "replaceState");

    render(<ResetPasswordPage />);

    await waitFor(() => expect(replaceSpy).toHaveBeenCalled());
    expect(window.location.search).not.toContain("token");
    expect(window.location.href).not.toContain("token=");

    fireEvent.change(screen.getByLabelText(/^new password$/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.change(screen.getByLabelText(/confirm password/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /reset password/i }),
    );

    await waitFor(() =>
      expect(resetPassword).toHaveBeenCalledWith("abc", "brand-new-pw-7"),
    );
  });

  it("shows the success message when reset resolves", async () => {
    resetPassword.mockResolvedValue(undefined);

    render(<ResetPasswordPage />);
    fireEvent.change(screen.getByLabelText(/^new password$/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.change(screen.getByLabelText(/confirm password/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /reset password/i }),
    );

    expect(
      await screen.findByText(/your password has been reset/i),
    ).toBeInTheDocument();
  });

  it("shows the weak-password message on a 400 weak_password response", async () => {
    resetPassword.mockRejectedValue(
      new AuthApiError(400, "weak_password", "weak"),
    );

    render(<ResetPasswordPage />);
    fireEvent.change(screen.getByLabelText(/^new password$/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.change(screen.getByLabelText(/confirm password/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /reset password/i }),
    );

    expect(
      await screen.findByRole("alert", {}, { timeout: 2000 }),
    ).toHaveTextContent(/stronger password/i);
  });

  it("requires matching passwords before submitting", async () => {
    render(<ResetPasswordPage />);
    fireEvent.change(screen.getByLabelText(/^new password$/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.change(screen.getByLabelText(/confirm password/i), {
      target: { value: "different-pw-2" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /reset password/i }),
    );

    expect(screen.getByRole("alert")).toHaveTextContent(/do not match/i);
    expect(resetPassword).not.toHaveBeenCalled();
  });
});
