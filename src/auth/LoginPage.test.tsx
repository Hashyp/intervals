import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { refreshSession } = vi.hoisted(() => ({
  refreshSession: vi.fn(),
}));

vi.mock("./AuthProvider", () => ({
  useAuth: () => ({ refreshSession }),
}));

import { LoginPage } from "./LoginPage";

function mockAuthFetch(responses: Record<string, () => Response>) {
  const fn = vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const responder = responses[url];
    if (!responder) {
      return Response.json({ message: "unmocked " + url }, { status: 404 });
    }
    return responder();
  });
  vi.stubGlobal("fetch", fn);
  return fn;
}

describe("LoginPage", () => {
  beforeEach(() => {
    window.history.replaceState({}, "", "/");
    refreshSession.mockClear();
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("renders google and x provider forms posting to the auth endpoints", () => {
    render(<LoginPage />);
    const googleForm = document.querySelector(
      'form[action="/auth/login/google"]',
    );
    const microsoftForm = document.querySelector(
      'form[action="/auth/login/microsoft"]',
    );
    const xForm = document.querySelector('form[action="/auth/login/x"]');
    expect(googleForm).not.toBeNull();
    expect(microsoftForm).not.toBeNull();
    expect(xForm).not.toBeNull();
    expect(
      screen.getByRole("button", { name: /google/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /continue with microsoft/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /continue with x/i }),
    ).toBeInTheDocument();
  });

  it("includes a sanitized return url hidden input", () => {
    window.history.replaceState({}, "", "/?mode=mixed");
    render(<LoginPage />);
    const input = document.querySelector(
      'input[name="returnUrl"]',
    ) as HTMLInputElement;
    expect(input.value).toBe("/?mode=mixed");
  });

  it("shows a cancellation message when auth=cancelled", () => {
    window.history.replaceState({}, "", "/login?auth=cancelled");
    render(<LoginPage />);
    expect(screen.getByRole("alert")).toHaveTextContent(/cancelled/i);
  });

  it("includes a remember me checkbox", () => {
    render(<LoginPage />);
    const checkboxes = screen.getAllByLabelText(/remember me/i);
    expect(checkboxes.length).toBeGreaterThanOrEqual(1);
    expect(checkboxes[0]).toBeInTheDocument();
  });

  it("renders the password sign-in form", () => {
    render(<LoginPage />);
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^password$/i)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /sign in with email/i }),
    ).toBeInTheDocument();
  });

  it("reveals the registration form when Create account is clicked", () => {
    render(<LoginPage />);
    fireEvent.click(
      screen.getByRole("button", { name: /create account/i }),
    );
    expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /^create account$/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /back to sign in/i }),
    ).toBeInTheDocument();
  });

  it("submits the password form to /auth/login/password", async () => {
    const fetchMock = mockAuthFetch({
      "/auth/antiforgery-token": () => Response.json({ token: "tok" }),
      "/auth/login/password": () => Response.json({ ok: true }, { status: 200 }),
    });

    render(<LoginPage />);
    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: "user@example.com" },
    });
    fireEvent.change(screen.getByLabelText(/^password$/i), {
      target: { value: "hunter2" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /sign in with email/i }),
    );

    await waitFor(() => expect(refreshSession).toHaveBeenCalled());
    const call = fetchMock.mock.calls.find(
      (c) => c[0] === "/auth/login/password",
    );
    expect(call).toBeDefined();
    const init = call![1]!;
    const headers = init.headers as Record<string, string>;
    expect(headers["X-CSRF-TOKEN"]).toBe("tok");
    expect(headers["Content-Type"]).toBe("application/json");
    expect(JSON.parse(init.body as string)).toEqual({
      email: "user@example.com",
      password: "hunter2",
      rememberMe: false,
    });
  });

  it("shows invalid credentials message on 401", async () => {
    mockAuthFetch({
      "/auth/antiforgery-token": () => Response.json({ token: "tok" }),
      "/auth/login/password": () =>
        Response.json(
          { code: "invalid_credentials", message: "bad" },
          { status: 401 },
        ),
    });

    render(<LoginPage />);
    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: "user@example.com" },
    });
    fireEvent.change(screen.getByLabelText(/^password$/i), {
      target: { value: "hunter2" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /sign in with email/i }),
    );

    expect(
      await screen.findByRole("alert", {}, { timeout: 2000 }),
    ).toHaveTextContent(/incorrect email or password/i);
    expect(refreshSession).not.toHaveBeenCalled();
  });

  it("submits the registration form to /auth/register", async () => {
    const fetchMock = mockAuthFetch({
      "/auth/antiforgery-token": () => Response.json({ token: "tok" }),
      "/auth/register": () => Response.json({ ok: true }, { status: 200 }),
    });

    render(<LoginPage />);
    fireEvent.click(
      screen.getByRole("button", { name: /create account/i }),
    );
    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: "new@example.com" },
    });
    fireEvent.change(screen.getByLabelText(/^password$/i), {
      target: { value: "hunter22" },
    });
    fireEvent.change(screen.getByLabelText(/confirm password/i), {
      target: { value: "hunter22" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /^create account$/i }),
    );

    await waitFor(() => expect(refreshSession).toHaveBeenCalled());
    const call = fetchMock.mock.calls.find((c) => c[0] === "/auth/register");
    expect(call).toBeDefined();
    const init = call![1]!;
    const headers = init.headers as Record<string, string>;
    expect(headers["X-CSRF-TOKEN"]).toBe("tok");
    expect(JSON.parse(init.body as string)).toEqual({
      email: "new@example.com",
      password: "hunter22",
    });
  });

  it("blocks registration when passwords do not match", () => {
    const fetchMock = mockAuthFetch({
      "/auth/antiforgery-token": () => Response.json({ token: "tok" }),
      "/auth/register": () => Response.json({ ok: true }),
    });

    render(<LoginPage />);
    fireEvent.click(
      screen.getByRole("button", { name: /create account/i }),
    );
    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: "new@example.com" },
    });
    fireEvent.change(screen.getByLabelText(/^password$/i), {
      target: { value: "hunter22" },
    });
    fireEvent.change(screen.getByLabelText(/confirm password/i), {
      target: { value: "different" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /^create account$/i }),
    );

    expect(screen.getByRole("alert")).toHaveTextContent(/do not match/i);
    expect(
      fetchMock.mock.calls.find((c) => c[0] === "/auth/register"),
    ).toBeUndefined();
  });

  it("navigates to the captured return url after password login", async () => {
    mockAuthFetch({
      "/auth/antiforgery-token": () => Response.json({ token: "tok" }),
      "/auth/login/password": () => Response.json({ ok: true }, { status: 200 }),
    });
    window.history.replaceState({}, "", "/intervals/deep?mode=mixed");
    const replaceSpy = vi.spyOn(window.history, "replaceState");

    render(<LoginPage />);
    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: "user@example.com" },
    });
    fireEvent.change(screen.getByLabelText(/^password$/i), {
      target: { value: "hunter2" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /sign in with email/i }),
    );

    await waitFor(() => expect(refreshSession).toHaveBeenCalled());
    expect(replaceSpy).toHaveBeenCalledWith({}, "", "/intervals/deep?mode=mixed");
  });

  it("navigates to the captured return url after registration", async () => {
    mockAuthFetch({
      "/auth/antiforgery-token": () => Response.json({ token: "tok" }),
      "/auth/register": () => Response.json({ ok: true }, { status: 200 }),
    });
    window.history.replaceState({}, "", "/intervals/deep");
    const replaceSpy = vi.spyOn(window.history, "replaceState");

    render(<LoginPage />);
    fireEvent.click(
      screen.getByRole("button", { name: /create account/i }),
    );
    fireEvent.change(screen.getByLabelText(/email/i), {
      target: { value: "new@example.com" },
    });
    fireEvent.change(screen.getByLabelText(/^password$/i), {
      target: { value: "hunter22" },
    });
    fireEvent.change(screen.getByLabelText(/confirm password/i), {
      target: { value: "hunter22" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: /^create account$/i }),
    );

    await waitFor(() => expect(refreshSession).toHaveBeenCalled());
    expect(replaceSpy).toHaveBeenCalledWith({}, "", "/intervals/deep");
  });
});
