import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AuthProvider } from "./AuthProvider";
import { ProtectedApp } from "./ProtectedApp";

function renderWithAuth() {
  return render(
    <AuthProvider>
      <ProtectedApp>
        <div data-testid="training">Training</div>
      </ProtectedApp>
    </AuthProvider>,
  );
}

function mockSession(status: number, body: unknown) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => Response.json(body, { status })),
  );
}

describe("ProtectedApp", () => {
  beforeEach(() => {
    window.history.replaceState({}, "", "/");
  });
  afterEach(() => vi.unstubAllGlobals());

  it("shows the login page when anonymous", async () => {
    mockSession(401, null);
    renderWithAuth();
    expect(
      await screen.findByRole("button", { name: /google/i }),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("training")).not.toBeInTheDocument();
  });

  it("renders children when authenticated", async () => {
    mockSession(200, {
      user: {
        id: "u1",
        displayName: "Avery",
        email: "a@x.com",
        avatarUrl: null,
      },
      providers: [{ id: "google", label: "Google", linked: true }],
    });
    renderWithAuth();
    expect(await screen.findByTestId("training")).toBeInTheDocument();
    expect(screen.getByText(/signed in as/i)).toBeInTheDocument();
  });
});
