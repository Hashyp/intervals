import "@testing-library/jest-dom/vitest";
import { afterEach, beforeEach, vi } from "vitest";

beforeEach(() => {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () =>
      Response.json({
        service: "Intervals API",
        message: "Minimal API connected to the Vite app",
        timestampUtc: "2026-07-02T00:00:00Z",
      }),
    ),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});
