import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerSessionChange } from "./player-session-change";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const source = { id: "s1", groupId: "g1", group: "المجموعة الأصلية", date: "2026-11-15", start: "10:00", end: "11:00" }, target = { ...source, id: "s2", groupId: "g2", group: "المجموعة البديلة", start: "13:00", end: "14:00" };
function setup(conflicts: string[] = [], fail = false) {
  const writes: { path: string; body: unknown; headers: Headers }[] = []; global.fetch = vi.fn(async (input, init) => {
    const path = String(input); if (path.endsWith("csrf")) return Response.json({ token: "csrf" });
    if (!init) return Response.json({ original: [source], alternative: path.includes("targetGroup=") ? [target] : [] });
    writes.push({ path, body: JSON.parse(String(init.body)), headers: new Headers(init.headers) });
    return path.endsWith("preview") ? Response.json({ fingerprint: "review", conflicts, effect: "لا تغيير في المال", changes: [{ source, target, expectedPlayers: 1, capacity: 2 }] }) : fail ? Response.json({ title: "تغير الموعد؛ أعد المعاينة." }, { status: 409 }) : Response.json({ moved: 1 });
  }) as typeof fetch; return writes;
}
async function select() {
  fireEvent.click(screen.getByText("نقل حصص مختارة")); await screen.findByRole("checkbox"); fireEvent.change(screen.getByLabelText("مجموعة الحصة البديلة"), { target: { value: "g2" } }); await waitFor(() => expect(screen.getByRole("checkbox")).toBeEnabled()); fireEvent.click(screen.getByRole("checkbox")); fireEvent.change(screen.getByLabelText("بديل الحصة s1"), { target: { value: "s2" } });
}
it("selects a particular session, requires preview then sends its pairs and reviewed fingerprint with a stable request key", async () => {
  const writes = setup(), saved = vi.fn(); render(<PlayerSessionChange enrollmentId="e" groups={[{ id: "g2", arabicName: "المجموعة البديلة" }]} onSaved={saved} />); await select(); fireEvent.click(screen.getByRole("button", { name: "معاينة نقل الحصص" })); await screen.findByRole("button", { name: "تأكيد نقل الحصص" }); expect(writes[0].body).toEqual({ pairs: [{ sourceSessionId: "s1", targetSessionId: "s2" }] }); fireEvent.click(screen.getByRole("button", { name: "تأكيد نقل الحصص" })); await waitFor(() => expect(saved).toHaveBeenCalledOnce()); expect(writes[1].body).toEqual({ pairs: [{ sourceSessionId: "s1", targetSessionId: "s2" }], fingerprint: "review" }); expect(writes[1].headers.get("Idempotency-Key")).toBeTruthy();
});
it("blocks a conflict and discards preview when selection changes", async () => {
  setup(["لا يوجد مقعد"]); render(<PlayerSessionChange enrollmentId="e" groups={[{ id: "g2", arabicName: "المجموعة البديلة" }]} />); await select(); fireEvent.click(screen.getByRole("button", { name: "معاينة نقل الحصص" })); expect(await screen.findByRole("button", { name: "تأكيد نقل الحصص" })).toBeDisabled(); fireEvent.click(screen.getByRole("checkbox")); expect(screen.queryByRole("button", { name: "تأكيد نقل الحصص" })).not.toBeInTheDocument();
});
it("requires a fresh review after the server rejects stale data", async () => {
  setup([], true); render(<PlayerSessionChange enrollmentId="e" groups={[{ id: "g2", arabicName: "المجموعة البديلة" }]} />); await select(); fireEvent.click(screen.getByRole("button", { name: "معاينة نقل الحصص" })); fireEvent.click(await screen.findByRole("button", { name: "تأكيد نقل الحصص" })); await screen.findByText("تغير الموعد؛ أعد المعاينة."); expect(screen.queryByRole("button", { name: "تأكيد نقل الحصص" })).not.toBeInTheDocument();
});
