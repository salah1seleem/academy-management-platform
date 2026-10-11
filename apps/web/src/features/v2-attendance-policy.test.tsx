import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerAttendance } from "./attendance";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
for (const allowed of [true, false, undefined]) {
  it(`uses the server attendance capability (${allowed}) rather than trusting a role label`, async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ canManageAttendance: allowed,
      session: { id: "s", sessionDate: "2026-10-11", startTime: "17:00", endTime: "18:00", status: "Scheduled", group: "تجريبية", sport: "سباحة", branch: "فرع اختبار" },
      players: [{ sportEnrollmentId: "e", playerId: "p", playerCode: "P-TEST", arabicName: "لاعب اختبار", attendanceStatus: "Present", sessionConsumed: false }],
    }) });
    render(<PlayerAttendance sessionId="s" />);
    await screen.findByText("لاعب اختبار");
    if (allowed) { expect(screen.getByRole("button", { name: "حفظ الحضور" })).toBeVisible(); expect(screen.getByRole("button", { name: "تحديد الحاضرين" })).toBeVisible(); }
    else { expect(screen.queryByRole("button", { name: "حفظ الحضور" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "غائب" })).not.toBeInTheDocument(); expect(screen.getByRole("status")).toHaveTextContent("يتولى الإداري"); }
  });
}
