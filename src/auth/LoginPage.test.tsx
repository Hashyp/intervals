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

  it("renders exactly one shared remember-me checkbox", () => {
    render(<LoginPage />);
    const checkboxes = screen.getAllByRole("checkbox", {
      name: /remember me/i,
    });
    expect(checkboxes).toHaveLength(1);
    expect(checkboxes[0]).toBeInTheDocument();
  });

  it("includes an antiforgery token and rememberMe field in each provider form", async () => {
    mockAuthFetch({
      "/auth/antiforgery-token": () => Response.json({ token: "csrf-abc" }),
      "/api/auth/providers": () =>
        Response.json({
          providers: [
            { id: "google", available: true },
            { id: "microsoft", available: true },
            { id: "x", available: true },
          ],
        }),
    });

    render(<LoginPage />);

    await waitFor(() => {
      const tokens = document.querySelectorAll(
        'input[name="__RequestVerificationToken"]',
      );
      expect(tokens).toHaveLength(3);
      tokens.forEach((node) => {
        expect((node as HTMLInputElement).value).toBe("csrf-abc");
      });
    });

    for (const id of ["google", "microsoft", "x"]) {
      const remember = document.querySelector(
        `form[action="/auth/login/${id}"] input[name="rememberMe"]`,
      ) as HTMLInputElement;
      expect(remember).not.toBeNull();
      expect(remember.value).toBe("false");
    }
  });

  it("reflects the shared remember-me state across provider hidden fields", async () => {
    mockAuthFetch({
      "/auth/antiforgery-token": () => Response.json({ token: "csrf-abc" }),
      "/api/auth/providers": () =>
        Response.json({
          providers: [
            { id: "google", available: true },
            { id: "microsoft", available: true },
            { id: "x", available: true },
          ],
        }),
    });

    render(<LoginPage />);
    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: /continue with google/i }),
      ).not.toBeDisabled(),
    );

    const googleRemember = document.querySelector(
      'form[action="/auth/login/google"] input[name="rememberMe"]',
    ) as HTMLInputElement;
    const xRemember = document.querySelector(
      'form[action="/auth/login/x"] input[name="rememberMe"]',
    ) as HTMLInputElement;
    expect(googleRemember.value).toBe("false");
    expect(xRemember.value).toBe("false");

    fireEvent.click(screen.getByRole("checkbox", { name: /remember me/i }));

    expect(googleRemember.value).toBe("true");
    expect(xRemember.value).toBe("true");
  });

  it("hides providers reported as unavailable", async () => {
    mockAuthFetch({
      "/auth/antiforgery-token": () => Response.json({ token: "csrf-abc" }),
      "/api/auth/providers": () =>
        Response.json({
          providers: [
            { id: "google", available: true },
            { id: "microsoft", available: true },
            { id: "x", available: false },
          ],
        }),
    });

    render(<LoginPage />);

    await waitFor(() => {
      expect(
        document.querySelector('form[action="/auth/login/x"]'),
      ).toBeNull();
    });
    expect(
      document.querySelector('form[action="/auth/login/google"]'),
    ).not.toBeNull();
    expect(
      document.querySelector('form[action="/auth/login/microsoft"]'),
    ).not.toBeNull();
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
