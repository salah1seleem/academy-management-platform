import { describe, expect, it } from "vitest";
import { dashboardParentPath } from "./dashboard-routes";

describe("direct-entry return destinations", () => {
  it("skips nonexistent intermediate academy routes", () => {
    expect(dashboardParentPath("/dashboard/academy/groups")).toBe("/dashboard");
    expect(dashboardParentPath("/dashboard/academy/groups/new")).toBe("/dashboard/academy/groups");
  });
  it("returns editing screens to their existing record, even without browser history", () => {
    expect(dashboardParentPath("/dashboard/players/player-demo/edit")).toBe("/dashboard/players/player-demo");
    expect(dashboardParentPath("/dashboard/guardians/family-demo/edit")).toBe("/dashboard/guardians/family-demo");
  });
  it("returns financial details to the correct in-app parent", () => {
    expect(dashboardParentPath("/dashboard/payroll")).toBe("/dashboard/finance?tab=payroll");
    expect(dashboardParentPath("/dashboard/reports/financial")).toBe("/dashboard/finance?tab=collections");
    expect(dashboardParentPath("/dashboard/finance/receivables")).toBe("/dashboard/finance");
    expect(dashboardParentPath("/dashboard/finance/external-agreements/agreement-demo/receipts/receipt-demo")).toBe("/dashboard/finance/external-agreements/agreement-demo");
  });
  it("never emits an external or invented destination", () => {
    expect(dashboardParentPath("https://example.test/other")).toBe("/dashboard");
    expect(dashboardParentPath("/dashboard/nonexistent/record")).toBe("/dashboard");
    expect(dashboardParentPath("/dashboard")).toBeNull();
  });
});
