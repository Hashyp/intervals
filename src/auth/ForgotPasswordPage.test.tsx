import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { requestPasswordReset } = vi.hoisted(() => ({
  requestPasswordReset: vi.fn(),
}));

vi.mock("./sessionApi", () => ({
  AuthApiError: class AuthApiError extends Error {
    readonly status: number;
    readonly code: string;
    constructor(status: number, code: string, message?: string) {
      super(message ?? code);
      this.status = status;
      this.code = code;
    }
  },
  requestPasswordReset,
}));

import { ForgotPasswordPage } from "./ForgotPasswordPage";

function mockAuthFetch() {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => Response.json({ token: "tok" })),
  );
}

describe("ForgotPasswordPage", () => {
  beforeEach(() => {
    requestPasswordReset.mockReset();
    mockAuthFetch();
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("renders the email field and submit button", () => {
    render(<ForgotPasswordPage />);
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /send reset link/i }),
    ).toBeInTheDocument();
  });

  it("calls requestPasswordReset with the typed email on submit", async () => {
    requestPasswordReset.mockResolvedValue(undefined);

    render(<ForgotPasswordPage />);
    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: "user@example.com" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /send reset link/i }),
    );

    await waitFor(() => expect(requestPasswordReset).toHaveBeenCalledTimes(1));
    expect(requestPasswordReset).toHaveBeenCalledWith("user@example.com");
  });

  it("shows the generic success message after submission", async () => {
    requestPasswordReset.mockResolvedValue(undefined);

    render(<ForgotPasswordPage />);
    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: "User@Example.com " },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /send reset link/i }),
    );

    expect(
      await screen.findByText(/If an account exists for User@Example\.com/),
    ).toBeInTheDocument();
  });

  it("requires a non-empty email before submitting", () => {
    render(<ForgotPasswordPage />);
    const form = document.querySelector(".auth-form") as HTMLFormElement;
    fireEvent.submit(form);

    expect(requestPasswordReset).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent(/enter your email/i);
  });
});
