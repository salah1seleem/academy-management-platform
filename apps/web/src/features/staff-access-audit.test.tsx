import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StaffAccessAudit } from "./staff-access-audit";

afterEach(cleanup);

it("shows the actual actor and affected account with the owner period", async () => {
  const calls: string[] = [];
  global.fetch = vi.fn(async input => {
    calls.push(String(input));
    return new Response(JSON.stringify({ count: 1, items: [{ id: "audit-1", membershipId: "staff-1", createdAtUtc: "2026-10-14T08:00:00Z", actorName: "المالك", staffName: "الإداري", change: "صلاحيات الوحدات أو نطاق الفروع" }] }), { headers: { "Content-Type": "application/json" } });
  }) as typeof fetch;
  render(<StaffAccessAudit initialFrom="2026-10-01" initialTo="2026-10-14" />);
  expect(await screen.findByText("الإداري")).toBeVisible();
  expect(screen.getByText("المالك")).toBeVisible();
  expect(calls[0]).toContain("from=2026-10-01&to=2026-10-14");
  fireEvent.click(screen.getByRole("button", { name: "مسح" }));
  await waitFor(() => expect(calls.some(x => x.includes("allDates=true"))).toBe(true));
});
