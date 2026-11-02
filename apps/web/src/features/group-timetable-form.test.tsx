import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GroupTimetableForm } from "./group-timetable-form";
const push = vi.fn(); vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); push.mockClear(); });
const preview = { fingerprint: "fingerprint", proposedSessions: 8, cancelledSessions: 2, preservedSessions: 1, cancelledBookings: 2, expectedPlayers: 3, resource: "A", conflicts: [] };
const calls: { path: string; body: Record<string, unknown>; headers?: HeadersInit }[] = [];
function setup(conflict = false) {
  calls.length = 0;
  vi.stubGlobal("fetch", vi.fn(async (url: unknown, init?: RequestInit) => {
    const path = String(url); let value: unknown;
    if (path.endsWith("my-permissions")) value = ["facilities.read"];
    else if (path.endsWith("/facilities")) value = { spaces: [{ id: "s", name: "المسبح", sportId: "swim" }], resources: [{ id: "a", name: "A", branchId: "b", facilitySpaceId: "s", capacity: 8, approvedAtUtc: "2026-10-11", serviceKinds: ["Training"] }, { id: "foreign", name: "فرع آخر", branchId: "other", facilitySpaceId: "s", capacity: 8, approvedAtUtc: "2026-10-11", serviceKinds: ["Training"] }] };
    else if (path.endsWith("/csrf")) value = { token: "csrf" };
    else { calls.push({ path, body: JSON.parse(String(init?.body)), headers: init?.headers }); value = path.endsWith("preview") ? { ...preview, conflicts: conflict ? [{ date: "2026-10-24", message: "السعة لا تكفي" }] : [] } : { created: 8 }; }
    return new Response(JSON.stringify(value), { status: 200 });
  }));
  render(<GroupTimetableForm id="g" group={{ branchId: "b", sportId: "swim" }} players={3} coaches={[{ id: "c", name: "مدرب المجموعة" }]} schedules={[{ dayOfWeek: 6, validFrom: "2026-10-24", validTo: "2026-11-24", startTime: "17:00", endTime: "18:00" }]} />);
}
it("previews without a success toast, filters resources, sends chosen coach and stable intent, then navigates", async () => {
  setup(); const saved = vi.fn(); window.addEventListener("academy-save-success", saved);
  await screen.findByRole("option", { name: /المسبح · A/ }); expect(screen.queryByRole("option", { name: /فرع آخر/ })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("الحارة أو منطقة التدريب"), { target: { value: "a" } }); fireEvent.change(screen.getByLabelText("المدرب المسند"), { target: { value: "c" } });
  fireEvent.click(screen.getByRole("button", { name: "معاينة الجدول" })); await screen.findByRole("region", { name: "معاينة أثر الجدول" }); expect(saved).not.toHaveBeenCalled(); expect(push).not.toHaveBeenCalled();
  expect(calls[0].body.resource).toEqual({ resourceId: "a", coachMembershipId: "c", seats: 3, exclusive: false });
  fireEvent.click(screen.getByRole("button", { name: "حفظ وإنشاء الحصص" })); await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/academy/groups"));
  expect(calls[1].body.previewFingerprint).toBe("fingerprint"); expect((calls[1].headers as Record<string, string>)["Idempotency-Key"]).toHaveLength(36); window.removeEventListener("academy-save-success", saved);
});
it("conflicts block confirmation and changing any field invalidates the reviewed result", async () => {
  setup(true); await screen.findByRole("option", { name: /المسبح · A/ });
  fireEvent.click(screen.getByRole("button", { name: "معاينة الجدول" })); expect(await screen.findByRole("alert")).toHaveTextContent("السعة لا تكفي"); expect(screen.getByRole("button", { name: "حفظ وإنشاء الحصص" })).toBeDisabled();
  fireEvent.change(screen.getByLabelText("إلى تاريخ"), { target: { value: "2026-11-30" } }); expect(screen.queryByRole("region", { name: "معاينة أثر الجدول" })).not.toBeInTheDocument(); expect(screen.getByRole("button", { name: "معاينة الجدول" })).toBeEnabled(); expect(calls).toHaveLength(1);
});
