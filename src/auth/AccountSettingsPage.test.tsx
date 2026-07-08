import {
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const {
  getAccount,
  changePassword,
  addPassword,
  unlinkProvider,
  getAntiforgeryToken,
} = vi.hoisted(() => ({
  getAccount: vi.fn(),
  changePassword: vi.fn(),
  addPassword: vi.fn(),
  unlinkProvider: vi.fn(),
  getAntiforgeryToken: vi.fn(),
}));

const refreshSession = vi.fn();

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
  getAccount,
  changePassword,
  addPassword,
  unlinkProvider,
  getAntiforgeryToken,
}));

vi.mock("./AuthProvider", () => ({
  useAuth: () => ({
    session: null,
    loading: false,
    authenticated: true,
    refreshSession,
    signOut: vi.fn(),
  }),
}));

import { AccountSettingsPage } from "./AccountSettingsPage";

const accountWithPassword = {
  userId: "u1",
  displayName: "Avery",
  email: "avery@example.com",
  emailVerified: true,
  hasPassword: true,
  providers: [
    { id: "google", label: "Google", linked: true, email: "avery@example.com", lastLoginUtc: null },
    { id: "microsoft", label: "Microsoft", linked: false, email: null, lastLoginUtc: null },
    { id: "x", label: "X", linked: false, email: null, lastLoginUtc: null },
    { id: "password", label: "Email", linked: true, email: "avery@example.com", lastLoginUtc: null },
  ],
};

const accountWithoutPassword = {
  userId: "u2",
  displayName: "Social Sam",
  email: "sam@example.com",
  emailVerified: false,
  hasPassword: false,
  providers: [
    { id: "google", label: "Google", linked: true, email: "sam@example.com", lastLoginUtc: null },
    { id: "microsoft", label: "Microsoft", linked: false, email: null, lastLoginUtc: null },
    { id: "x", label: "X", linked: false, email: null, lastLoginUtc: null },
    { id: "password", label: "Email", linked: false, email: null, lastLoginUtc: null },
  ],
};

describe("AccountSettingsPage", () => {
  beforeEach(() => {
    getAccount.mockReset();
    changePassword.mockReset();
    addPassword.mockReset();
    unlinkProvider.mockReset();
    getAntiforgeryToken.mockReset();
    refreshSession.mockReset();
    refreshSession.mockResolvedValue(undefined);
    getAntiforgeryToken.mockResolvedValue("test-csrf-token");
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === "string" ? input : input.toString();
        if (url.includes("/api/auth/providers")) {
          return Response.json({ providers: [] });
        }
        return Response.json({ token: "tok" });
      }),
    );
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("renders linked providers and link forms for unlinked providers", async () => {
    getAccount.mockResolvedValue(accountWithoutPassword);

    render(<AccountSettingsPage />);

    expect(await screen.findByText(/sign-in methods/i)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /unlink google/i }),
    ).toBeInTheDocument();
    expect(screen.getAllByText(/not linked/i).length).toBeGreaterThan(0);

    const microsoftLinkForm = document.querySelector(
      'form[action="/auth/providers/link/microsoft"]',
    ) as HTMLFormElement;
    expect(microsoftLinkForm).toBeTruthy();
    expect(microsoftLinkForm.querySelector('input[name="returnUrl"]')).toHaveValue(
      "/account-settings",
    );
    expect(
      microsoftLinkForm.querySelector('input[name="__RequestVerificationToken"]'),
    ).toHaveValue("test-csrf-token");

    const xLinkForm = document.querySelector(
      'form[action="/auth/providers/link/x"]',
    ) as HTMLFormElement;
    expect(xLinkForm).toBeTruthy();
    expect(
      xLinkForm.querySelector('input[name="__RequestVerificationToken"]'),
    ).toHaveValue("test-csrf-token");

    // Google is linked, so no link form should exist for it.
    expect(
      document.querySelector('form[action="/auth/providers/link/google"]'),
    ).toBeNull();
  });

  it("does not render a link button for an unavailable provider", async () => {
    getAccount.mockResolvedValue(accountWithoutPassword);
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = typeof input === "string" ? input : input.toString();
        if (url.includes("/api/auth/providers")) {
          return Response.json({
            providers: [
              { id: "google", available: true },
              { id: "microsoft", available: false },
              { id: "x", available: true },
            ],
          });
        }
        return Response.json({ token: "tok" });
      }),
    );

    render(<AccountSettingsPage />);

    expect(await screen.findByText(/sign-in methods/i)).toBeInTheDocument();

    // Microsoft is unavailable: no link form/button.
    await waitFor(() => {
      expect(
        document.querySelector('form[action="/auth/providers/link/microsoft"]'),
      ).toBeNull();
      expect(
        screen.queryByRole("button", { name: /link microsoft/i }),
      ).toBeNull();
    });

    // X is available: link form still present.
    const xLinkForm = document.querySelector(
      'form[action="/auth/providers/link/x"]',
    ) as HTMLFormElement;
    expect(xLinkForm).toBeTruthy();
  });

  it("shows an unlink button for linked providers", async () => {
    getAccount.mockResolvedValue(accountWithoutPassword);

    render(<AccountSettingsPage />);

    expect(
      await screen.findByRole("button", { name: /unlink google/i }),
    ).toBeInTheDocument();
  });

  it("submits change password with field values and shows success", async () => {
    getAccount.mockResolvedValue(accountWithPassword);
    changePassword.mockResolvedValue(undefined);

    render(<AccountSettingsPage />);

    await screen.findByText(/change password/i);

    fireEvent.change(screen.getByLabelText(/current password/i), {
      target: { value: "old-password-1" },
    });
    fireEvent.change(screen.getByLabelText(/^new password$/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.change(screen.getByLabelText(/confirm new password/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.click(screen.getByRole("button", { name: /update password/i }));

    await waitFor(() => expect(changePassword).toHaveBeenCalledTimes(1));
    expect(changePassword).toHaveBeenCalledWith("old-password-1", "brand-new-pw-7");
    expect(await screen.findByText(/password updated/i)).toBeInTheDocument();
    expect(refreshSession).toHaveBeenCalled();
  });

  it("submits add password with email and password values", async () => {
    getAccount.mockResolvedValue(accountWithoutPassword);
    addPassword.mockResolvedValue(undefined);

    render(<AccountSettingsPage />);

    await screen.findByText(/add a password/i);

    fireEvent.change(screen.getByLabelText(/^email$/i), {
      target: { value: "sam@example.com" },
    });
    fireEvent.change(screen.getByLabelText(/^password$/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.change(screen.getByLabelText(/confirm password/i), {
      target: { value: "brand-new-pw-7" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^add password$/i }));

    await waitFor(() => expect(addPassword).toHaveBeenCalledTimes(1));
    expect(addPassword).toHaveBeenCalledWith("sam@example.com", "brand-new-pw-7");
  });

  it("asks for confirmation and calls unlinkProvider", async () => {
    getAccount.mockResolvedValue(accountWithoutPassword);
    unlinkProvider.mockResolvedValue(undefined);
    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(true);

    render(<AccountSettingsPage />);

    const unlinkButton = await screen.findByRole("button", {
      name: /unlink google/i,
    });
    fireEvent.click(unlinkButton);

    await waitFor(() => expect(unlinkProvider).toHaveBeenCalledTimes(1));
    expect(unlinkProvider).toHaveBeenCalledWith("google");
    expect(confirmSpy).toHaveBeenCalled();
  });

  it("does not call unlinkProvider when confirmation is cancelled", async () => {
    getAccount.mockResolvedValue(accountWithoutPassword);
    vi.spyOn(window, "confirm").mockReturnValue(false);

    render(<AccountSettingsPage />);

    const unlinkButton = await screen.findByRole("button", {
      name: /unlink google/i,
    });
    fireEvent.click(unlinkButton);

    await waitFor(() => expect(unlinkProvider).not.toHaveBeenCalled());
  });
});
