import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

const { signOut, session } = vi.hoisted(() => ({
  signOut: vi.fn(),
  session: {
    user: {
      id: "u",
      displayName: "Avery",
      email: null,
      avatarUrl: null,
    },
    providers: [],
  },
}));

vi.mock("./AuthProvider", () => ({
  useAuth: () => ({
    session,
    signOut,
    loading: false,
    authenticated: true,
    refreshSession: vi.fn(),
  }),
}));

import { AccountBar } from "./AccountBar";

describe("AccountBar", () => {
  afterEach(() => signOut.mockClear());

  it("renders the display name and a sign out control", () => {
    render(<AccountBar />);
    expect(screen.getByText(/avery/i)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /sign out/i }),
    ).toBeInTheDocument();
  });

  it("sign out button triggers signOut", () => {
    render(<AccountBar />);
    screen.getByRole("button", { name: /sign out/i }).click();
    expect(signOut).toHaveBeenCalled();
  });
});
