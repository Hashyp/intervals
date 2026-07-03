import { describe, expect, it } from "vitest";
import { sanitizeReturnUrl } from "./returnUrl";

describe("sanitizeReturnUrl", () => {
  it("accepts local relative paths", () => {
    expect(sanitizeReturnUrl("/")).toBe("/");
    expect(sanitizeReturnUrl("/?mode=mixed")).toBe("/?mode=mixed");
    expect(sanitizeReturnUrl("/login")).toBe("/login");
  });
  it("rejects absolute and protocol-relative urls", () => {
    expect(sanitizeReturnUrl("https://evil.com/")).toBe("/");
    expect(sanitizeReturnUrl("//evil.com/")).toBe("/");
  });
  it("rejects backslashes and non-leading-slash input", () => {
    expect(sanitizeReturnUrl("/\\evil")).toBe("/");
    expect(sanitizeReturnUrl("no-slash")).toBe("/");
  });
  it("defaults for empty", () => {
    expect(sanitizeReturnUrl("")).toBe("/");
    expect(sanitizeReturnUrl(null)).toBe("/");
  });
});
