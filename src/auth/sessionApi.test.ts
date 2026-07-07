import { afterEach, describe, expect, it, vi } from "vitest";
import { AuthApiError, loginWithPassword, register } from "./sessionApi";

type FetchHandler = () => Response;

function stubFetch(handlers: Record<string, FetchHandler>) {
  const fn = vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const handler = handlers[url];
    if (!handler) {
      return Response.json({ message: "unmocked " + url }, { status: 404 });
    }
    return handler();
  });
  vi.stubGlobal("fetch", fn);
  return fn;
}

function ok(): () => Response {
  return () => Response.json({ ok: true }, { status: 200 });
}

function antiforgery(): () => Response {
  return () => Response.json({ token: "csrf-token" }, { status: 200 });
}

describe("register", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("posts email and password with the antiforgery header", async () => {
    const fetchMock = stubFetch({
      "/auth/antiforgery-token": antiforgery(),
      "/auth/register": ok(),
    });

    await register("user@example.com", "super-secret-1");

    const call = fetchMock.mock.calls.find((c) => c[0] === "/auth/register");
    expect(call).toBeDefined();
    const init = call![1]!;
    expect(init.method).toBe("POST");
    const headers = init.headers as Record<string, string>;
    expect(headers["X-CSRF-TOKEN"]).toBe("csrf-token");
    expect(headers["Content-Type"]).toBe("application/json");
    expect(JSON.parse(init.body as string)).toEqual({
      email: "user@example.com",
      password: "super-secret-1",
    });
  });

  it("throws AuthApiError carrying status and code on 409 email_taken", async () => {
    stubFetch({
      "/auth/antiforgery-token": antiforgery(),
      "/auth/register": () =>
        Response.json(
          { code: "email_taken", message: "exists", correlationId: "c1" },
          { status: 409 },
        ),
    });

    let caught: unknown;
    try {
      await register("a@b.com", "password1");
    } catch (error) {
      caught = error;
    }
    expect(caught).toBeInstanceOf(AuthApiError);
    const apiError = caught as AuthApiError;
    expect(apiError.status).toBe(409);
    expect(apiError.code).toBe("email_taken");
  });

  it("maps weak_password on 400", async () => {
    stubFetch({
      "/auth/antiforgery-token": antiforgery(),
      "/auth/register": () =>
        Response.json(
          { code: "weak_password", message: "too weak" },
          { status: 400 },
        ),
    });

    await expect(register("a@b.com", "1")).rejects.toMatchObject({
      status: 400,
      code: "weak_password",
    });
  });

  it("falls back to unknown code when body has none", async () => {
    stubFetch({
      "/auth/antiforgery-token": antiforgery(),
      "/auth/register": () => Response.json({}, { status: 400 }),
    });

    await expect(register("a@b.com", "pw")).rejects.toMatchObject({
      status: 400,
      code: "unknown",
    });
  });
});

describe("loginWithPassword", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("posts email, password and rememberMe with the antiforgery header", async () => {
    const fetchMock = stubFetch({
      "/auth/antiforgery-token": antiforgery(),
      "/auth/login/password": ok(),
    });

    await loginWithPassword("user@example.com", "secret", true);

    const call = fetchMock.mock.calls.find(
      (c) => c[0] === "/auth/login/password",
    );
    expect(call).toBeDefined();
    const init = call![1]!;
    expect(init.method).toBe("POST");
    const headers = init.headers as Record<string, string>;
    expect(headers["X-CSRF-TOKEN"]).toBe("csrf-token");
    expect(JSON.parse(init.body as string)).toEqual({
      email: "user@example.com",
      password: "secret",
      rememberMe: true,
    });
  });

  it("maps invalid_credentials on 401", async () => {
    stubFetch({
      "/auth/antiforgery-token": antiforgery(),
      "/auth/login/password": () =>
        Response.json(
          { code: "invalid_credentials", message: "bad" },
          { status: 401 },
        ),
    });

    await expect(loginWithPassword("a@b.com", "wrong", false)).rejects.toMatchObject(
      { status: 401, code: "invalid_credentials" },
    );
  });

  it("maps locked_out on 423", async () => {
    stubFetch({
      "/auth/antiforgery-token": antiforgery(),
      "/auth/login/password": () =>
        Response.json({ code: "locked_out", message: "locked" }, { status: 423 }),
    });

    await expect(loginWithPassword("a@b.com", "x", false)).rejects.toMatchObject({
      status: 423,
      code: "locked_out",
    });
  });
});
