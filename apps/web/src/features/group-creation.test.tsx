import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GroupCreation } from "./group-creation";
const push = vi.hoisted(() => vi.fn());
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); push.mockClear(); });
const preview = { fingerprint: "reviewed", name: "مجموعة تجريبية", sessions: 4, players: 1, conflicts: [] as string[] };
function setup(conflicts: string[] = [], fail = false) {
  const calls: { path: string; body: Record<string, unknown>; headers?: HeadersInit }[] = [];
  global.fetch = vi.fn(async (input, init) => {
    const path = String(input);
    if (path.endsWith("csrf")) return Response.json({ token: "csrf" });
    if (init) { calls.push({ path, body: JSON.parse(String(init.body)), headers: init.headers }); if (path.endsWith("/preview")) return Response.json({ ...preview, conflicts }); if (path.endsWith("training-levels")) return Response.json({ id: "level" }); if (fail) throw new TypeError("تعذر تأكيد الطلب"); return Response.json({ id: "created" }); }
    if (path.endsWith("structure/options")) return Response.json({ branches: [{ id: "branch", arabicName: "فرع اصطناعي" }], sports: [{ id: "sport", arabicName: "سباحة" }], categories: [{ id: "age", arabicName: "تحت عشرة" }], coaches: [{ id: "coach", arabicName: "مدرب اصطناعي" }] });
    if (path.endsWith("training-levels")) return Response.json([{ id: "level", sportId: "sport", arabicName: "مبتدئ" }]);
    if (path.endsWith("my-permissions")) return Response.json([]);
    return Response.json({ total: 2, players: [{ id: "e1", name: "لاعب مطابق", code: "DEMO-01", trainingLevelId: "level", registrationFormat: "Group" }, { id: "e2", name: "لاعب مستوى مختلف", code: "DEMO-02", trainingLevelId: "other", registrationFormat: "Group" }] });
  }) as typeof fetch; return calls;
}
async function toPlayers() {
  render(<GroupCreation />); fireEvent.change(await screen.findByLabelText("اسم المجموعة"), { target: { value: preview.name } }); fireEvent.change(screen.getByLabelText("الفئة العمرية"), { target: { value: "age" } }); fireEvent.change(screen.getByLabelText("مستوى التدريب"), { target: { value: "level" } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة" })); fireEvent.change(screen.getByLabelText("المدرب"), { target: { value: "coach" } }); fireEvent.click(screen.getByLabelText("السبت")); fireEvent.click(screen.getByLabelText("الاثنين"));
  fireEvent.change(screen.getByLabelText("وقت البداية — الساعة"), { target: { value: "5" } }); fireEvent.click(screen.getByRole("button", { name: "متابعة" })); await screen.findByRole("checkbox", { name: "اختيار لاعب مطابق" });
}
it("keeps a four-step draft, submits the exact weekdays, and requires preview before atomic creation", async () => {
  const calls = setup(), success = vi.fn(); window.addEventListener("academy-save-success", success); await toPlayers(); expect(calls).toHaveLength(0); expect(screen.getByRole("checkbox", { name: "اختيار لاعب مستوى مختلف" })).toBeDisabled(); fireEvent.click(screen.getByRole("checkbox", { name: "اختيار لاعب مطابق" })); fireEvent.click(screen.getByRole("button", { name: "معاينة المجموعة كاملة" })); await screen.findByRole("region", { name: "معاينة إنشاء المجموعة" }); expect(success).not.toHaveBeenCalled(); expect(calls[0].body).toMatchObject({ enrollmentIds: ["e1"], timetable: { days: [6, 1], startTime: "17:00:00" } }); fireEvent.click(screen.getByRole("button", { name: "تأكيد إنشاء المجموعة" })); await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/academy/groups/created")); expect(calls[1].body.fingerprint).toBe("reviewed"); expect(new Headers(calls[1].headers).get("Idempotency-Key")).toBeTruthy(); window.removeEventListener("academy-save-success", success);
});
it("blocks all persistence when the preview has one conflict", async () => {
  const calls = setup(["المدرب لديه موعد متعارض"]); await toPlayers(); fireEvent.click(screen.getByRole("button", { name: "معاينة المجموعة كاملة" })); await screen.findByText("المدرب لديه موعد متعارض"); expect(screen.getByRole("button", { name: "تأكيد إنشاء المجموعة" })).toBeDisabled(); expect(calls).toHaveLength(1); expect(push).not.toHaveBeenCalled();
});
it("retains the intent key on a transport retry and does not report a false save", async () => {
  const calls = setup([], true); await toPlayers(); fireEvent.click(screen.getByRole("button", { name: "معاينة المجموعة كاملة" })); const save = await screen.findByRole("button", { name: "تأكيد إنشاء المجموعة" }); fireEvent.click(save); await screen.findByText("تعذر تأكيد الطلب"); expect(push).not.toHaveBeenCalled(); fireEvent.click(save); await waitFor(() => expect(calls).toHaveLength(3)); expect(new Headers(calls[1].headers).get("Idempotency-Key")).toBe(new Headers(calls[2].headers).get("Idempotency-Key"));
});
it("supports creating the first sport level in place without creating a group", async () => {
  const calls = setup(); render(<GroupCreation />); await screen.findByLabelText("اسم المجموعة"); fireEvent.click(screen.getByText("إضافة مستوى تدريب للرياضة")); fireEvent.change(screen.getByLabelText("اسم المستوى الجديد"), { target: { value: "مبتدئ" } }); fireEvent.click(screen.getByRole("button", { name: "حفظ المستوى" })); await waitFor(() => expect(screen.getByLabelText("مستوى التدريب")).toHaveValue("level")); expect(calls).toHaveLength(1); expect(calls[0].body).toEqual({ sportId: "sport", arabicName: "مبتدئ", rank: 1 }); expect(push).not.toHaveBeenCalled();
});
