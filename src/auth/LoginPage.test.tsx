import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { LoginPage } from "./LoginPage";

describe("LoginPage", () => {
  beforeEach(() => {
    window.history.replaceState({}, "", "/");
  });

  it("renders google and x provider forms posting to the auth endpoints", () => {
    render(<LoginPage />);
    const googleForm = document.querySelector(
      'form[action="/auth/login/google"]',
    );
    const xForm = document.querySelector('form[action="/auth/login/x"]');
    expect(googleForm).not.toBeNull();
    expect(xForm).not.toBeNull();
    expect(
      screen.getByRole("button", { name: /google/i }),
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
    expect(screen.getByLabelText(/remember me/i)).toBeInTheDocument();
  });
});
