import { describe, expect, it } from "vitest";
import { formatDateAr, formatMoneyAr, planTypeLabel, statusLabel } from "./formatters";

describe("Arabic display formatters", () => {
  it("renders EGP as a clean Arabic جنيه amount", () => {
    expect(formatMoneyAr(900, "EGP")).toContain("جنيه");
    expect(formatMoneyAr(900, "EGP")).not.toContain("EGP");
  });
  it("renders ISO dates for Arabic readers", () => expect(formatDateAr("2026-09-28")).not.toContain("2026-09-28"));
  it("maps plan enums", () => expect(planTypeLabel("Combined")).toBe("بالمدة والحصص"));
  it("maps workflow enums", () => expect(statusLabel("Confirmed")).toBe("مؤكد"));
  it("uses a safe label for unknown states", () => expect(statusLabel("Unexpected")).toBe("حالة غير معروفة"));
});
