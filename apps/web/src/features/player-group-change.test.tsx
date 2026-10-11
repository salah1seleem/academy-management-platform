import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerGroupChange } from "./player-group-change";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const preview = { fingerprint: "fresh", effectiveDate: "2026-12-01", sourceGroup: "مجموعة أ", targetGroup: "مجموعة ب", removedSessions: [{ id: "s1", sessionDate: "2026-12-02", startTime: "10:00", endTime: "11:00" }], addedSessions: [{ id: "s2", sessionDate: "2026-12-02", startTime: "13:00", endTime: "14:00" }], conflicts: [] as string[], effect: "لا تحصيل أو حضور جديد" };
function setup(conflicts: string[] = [], failSave = false) {
  const calls: { path: string; body: unknown; headers?: HeadersInit }[] = [];
  global.fetch = vi.fn(async (input, init) => {
    const path = String(input); if (path.endsWith("csrf")) return Response.json({ token: "token" });
    if (init) { calls.push({ path, body: JSON.parse(String(init.body)), headers: init.headers }); if (path.endsWith("preview")) return Response.json({ ...preview, conflicts }); return failSave ? Response.json({ title: "تغيرت البيانات؛ أعد المعاينة." }, { status: 409 }) : Response.json({ id: "move" }); }
    return Response.json({ groups: [{ id: "a", arabicName: "مجموعة أ", sportId: "sport", branchId: "branch" }, { id: "b", arabicName: "مجموعة ب", sportId: "sport", branchId: "branch" }, { id: "private", arabicName: "مجموعة خاصة", sportId: "sport", branchId: "branch", registrationFormat: "Private" }, { id: "foreign", arabicName: "فرع آخر", sportId: "sport", branchId: "other" }] });
  }) as typeof fetch; return calls;
}
async function select() { await screen.findByRole("option", { name: "مجموعة ب", hidden: true }); fireEvent.click(screen.getByText("تعديل المجموعة")); fireEvent.change(screen.getByLabelText("تعديل المجموعة"), { target: { value: "b" } }); fireEvent.change(screen.getByLabelText("بدء النقل"), { target: { value: "2026-12-01" } }); }
it("requires an explicit dated preview and sends the fingerprint and idempotency key without announcing preview as saved", async () => {
  const calls = setup(), saved = vi.fn(), notice = vi.fn(); window.addEventListener("academy-save-success", notice);
  render(<PlayerGroupChange enrollmentId="enrollment" groupId="a" onSaved={saved} />); await select(); expect(screen.queryByText("مجموعة خاصة")).not.toBeInTheDocument(); expect(screen.queryByText("فرع آخر")).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "تأكيد نقل المجموعة" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "معاينة نقل المجموعة" })); await screen.findByRole("button", { name: "تأكيد نقل المجموعة" }); expect(notice).not.toHaveBeenCalled(); expect(calls[0].body).toEqual({ groupId: "b", effectiveDate: "2026-12-01" });
  fireEvent.click(screen.getByRole("button", { name: "تأكيد نقل المجموعة" })); await waitFor(() => expect(saved).toHaveBeenCalledOnce()); expect(calls[1].body).toEqual({ groupId: "b", effectiveDate: "2026-12-01", fingerprint: "fresh" }); expect(new Headers(calls[1].headers).get("Idempotency-Key")).toBeTruthy(); window.removeEventListener("academy-save-success", notice);
});
it("blocks conflicting transfers and invalidates the preview when the date changes", async () => {
  const calls = setup(["المجموعة مكتملة"]); render(<PlayerGroupChange enrollmentId="e" groupId="a" />); await select(); fireEvent.click(screen.getByRole("button", { name: "معاينة نقل المجموعة" })); expect(await screen.findByRole("button", { name: "تأكيد نقل المجموعة" })).toBeDisabled(); expect(screen.getByRole("alert")).toHaveTextContent("المجموعة مكتملة"); fireEvent.change(screen.getByLabelText("بدء النقل"), { target: { value: "2026-12-03" } }); expect(screen.queryByRole("button", { name: "تأكيد نقل المجموعة" })).not.toBeInTheDocument(); expect(calls).toHaveLength(1);
});
it("requires a new preview after stale data is rejected by the server", async () => {
  setup([], true); render(<PlayerGroupChange enrollmentId="e" groupId="a" />); await select(); fireEvent.click(screen.getByRole("button", { name: "معاينة نقل المجموعة" })); fireEvent.click(await screen.findByRole("button", { name: "تأكيد نقل المجموعة" })); await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("تغيرت البيانات")); expect(screen.queryByRole("button", { name: "تأكيد نقل المجموعة" })).not.toBeInTheDocument();
});
