import { describe, expect, it } from "vitest";
import { formatDateAr, formatMoneyAr, formatStaffCheckout, planTypeLabel, statusLabel } from "./formatters";

describe("Arabic display formatters", () => {
  it("shows the actual overnight checkout date without inventing a date for an open record", () => {
    expect(formatStaffCheckout("04:00:00","2026-10-16","2026-10-15")).toContain(formatDateAr("2026-10-16"));
    expect(formatStaffCheckout("04:00:00","2026-10-16","2026-10-16")).toBe("4:00 صباحًا");
    expect(formatStaffCheckout(null,"2026-10-16","2026-10-15")).toBe("—");
  });
  it("renders EGP as a clean Arabic جنيه amount", () => {
    expect(formatMoneyAr(900, "EGP")).toContain("جنيه");
    expect(formatMoneyAr(900, "EGP")).not.toContain("EGP");
  });
  it("renders ISO dates for Arabic readers", () => expect(formatDateAr("2026-09-28")).not.toContain("2026-09-28"));
  it("maps plan enums", () => expect(planTypeLabel("Combined")).toBe("بالمدة والحصص"));
  it("maps workflow enums", () => expect(statusLabel("Confirmed")).toBe("مؤكد"));
  it("uses a safe label for unknown states", () => expect(statusLabel("Unexpected")).toBe("حالة غير معروفة"));
});
