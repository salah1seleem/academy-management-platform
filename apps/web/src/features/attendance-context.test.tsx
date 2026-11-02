import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AttendanceHistory, PlayerAttendance } from "./attendance";

afterEach(cleanup);

describe("contextual player attendance", () => {
  it("shows only the selected player in a session roster", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({
      session: { id: "session-1", sessionDate: "2026-09-28", startTime: "18:00:00", endTime: "19:00:00", status: "Scheduled", group: "براعم أ", sport: "كرة القدم", branch: "مدينة نصر" },
      players: [
        { sportEnrollmentId: "enrollment-1", playerId: "player-1", playerCode: "P-1", arabicName: "آدم خالد", attendanceStatus: "NotRecorded", sessionConsumed: false },
        { sportEnrollmentId: "enrollment-2", playerId: "player-2", playerCode: "P-2", arabicName: "عمر حسن", attendanceStatus: "NotRecorded", sessionConsumed: false },
      ],
    }) });
    render(<PlayerAttendance sessionId="session-1" playerId="player-1" />);
    expect(await screen.findByText("آدم خالد")).toBeVisible();
    expect(screen.queryByText("عمر حسن")).not.toBeInTheDocument();
  });

  it("sends the selected player id to the history endpoint", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [] });
    render(<AttendanceHistory playerId="player-1" />);
    await screen.findByText("لا توجد سجلات حضور");
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([input]) => String(input).includes("playerId=player-1"))).toBe(true));
  });
});
