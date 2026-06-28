import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { RecentStrip } from "./RecentStrip";

describe("RecentStrip", () => {
  it("shows at most 18 attempts and marks the newest one", () => {
    const recent = Array.from({ length: 20 }, (_, index) => index % 2 === 0);

    render(<RecentStrip recent={recent} />);

    const strip = screen.getByLabelText("Recent attempts");
    const dots = Array.from(strip.querySelectorAll(".recent__dot"));
    const newestDots = Array.from(strip.querySelectorAll(".recent__dot--new"));

    expect(dots).toHaveLength(18);
    expect(dots[0]).toHaveClass("recent__dot--hit");
    expect(dots[17]).toHaveClass("recent__dot--miss");
    expect(newestDots).toHaveLength(1);
    expect(newestDots[0]).toBe(dots[17]);
  });
});
