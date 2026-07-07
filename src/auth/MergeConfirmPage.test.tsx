import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { getPendingMerge, confirmMerge, cancelMerge, refreshSession } =
  vi.hoisted(() => ({
    getPendingMerge: vi.fn(),
    confirmMerge: vi.fn(),
    cancelMerge: vi.fn(),
    refreshSession: vi.fn(),
  }));

vi.mock("./mergeApi", () => ({
  getPendingMerge,
  confirmMerge,
  cancelMerge,
}));

vi.mock("./AuthProvider", () => ({
  useAuth: () => ({ refreshSession }),
}));

import { MergeConfirmPage } from "./MergeConfirmPage";

const samplePending = {
  primaryUserId: "p-1",
  primaryDisplayName: "Primary User",
  primaryEmail: "primary@example.com",
  secondaryUserId: "s-1",
  secondaryDisplayName: "Secondary User",
  secondaryEmail: "secondary@example.com",
  provider: "google",
};

function waitForPending() {
  return screen.findByText(/merge secondary user into primary user/i);
}

describe("MergeConfirmPage", () => {
  beforeEach(() => {
    getPendingMerge.mockReset();
    confirmMerge.mockReset();
    cancelMerge.mockReset();
    refreshSession.mockReset();
    confirmMerge.mockResolvedValue(undefined);
    cancelMerge.mockResolvedValue(undefined);
    refreshSession.mockResolvedValue(undefined);
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("shows the empty state when there is no pending merge", async () => {
    getPendingMerge.mockResolvedValue(null);

    render(<MergeConfirmPage />);

    expect(await screen.findByText(/no pending account merge/i)).toBeInTheDocument();
    expect(confirmMerge).not.toHaveBeenCalled();
  });

  it("shows both identities when a pending merge is present", async () => {
    getPendingMerge.mockResolvedValue(samplePending);

    render(<MergeConfirmPage />);

    expect(await waitForPending()).toBeInTheDocument();
    expect(screen.getAllByText(/primary user/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/secondary user/i).length).toBeGreaterThan(0);
    expect(
      screen.getByRole("button", { name: /confirm merge/i }),
    ).toBeInTheDocument();
  });

  it("calls confirmMerge and refreshSession when confirm is clicked", async () => {
    getPendingMerge.mockResolvedValue(samplePending);

    render(<MergeConfirmPage />);
    await waitForPending();

    fireEvent.click(screen.getByRole("button", { name: /confirm merge/i }));

    await waitFor(() => expect(confirmMerge).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(refreshSession).toHaveBeenCalledTimes(1));
  });

  it("calls cancelMerge when cancel is clicked", async () => {
    getPendingMerge.mockResolvedValue(samplePending);

    render(<MergeConfirmPage />);
    await waitForPending();

    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));

    await waitFor(() => expect(cancelMerge).toHaveBeenCalledTimes(1));
  });
});
