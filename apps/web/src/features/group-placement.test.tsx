import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { EnrollmentLevelReview, GroupPlacement } from "./group-placement";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const candidate = { id: "e1", playerId: "p1", player: "لاعب اصطناعي", playerCode: "DEMO-01", level: "مبتدئ", reasons: [] as string[] };
const candidates = { total: 1, offset: 0, pageSize: 100, currentPlayers: 0, capacity: 4, conflicts: [] as string[], players: [candidate] };
const preview = { fingerprint: "reviewed", effectiveDate: "2026-10-11", selected: 1, currentPlayers: 0, capacity: 4, conflicts: [] as string[], players: [candidate] };
function setup(overrides: { candidates?: typeof candidates; preview?: typeof preview; saveFails?: boolean } = {}) {
  const calls: { path: string; body: unknown; headers: HeadersInit | undefined }[] = [];
  global.fetch = vi.fn(async (input, init) => {
    const path = String(input); if (path.endsWith("csrf")) return Response.json({ token: "csrf" });
    if (init?.method && init.method !== "GET") { calls.push({ path, body: JSON.parse(String(init.body)), headers: init.headers }); if (path.endsWith("/preview")) return Response.json(overrides.preview ?? preview); if (overrides.saveFails && path.endsWith("/placement")) return Response.json({ title: "تغيرت البيانات؛ أعد المعاينة." }, { status: 409 }); return Response.json({ placed: 1 }); }
    if (path.includes("training-levels") || path.includes("placement-levels")) return Response.json([{ id: "level", sportId: "sport", arabicName: "مبتدئ" }, { id: "other-level", sportId: "other", arabicName: "رياضة أخرى" }]);
    if(path.endsWith('/placement-level'))return Response.json({version:7,trainingLevelId:null,history:{items:[],hasMore:false}});
    return Response.json(overrides.candidates ?? candidates);
  }) as typeof fetch; return calls;
}
it("requires an explicit preview before bulk save, uses a stable intent, and does not announce preview as saved", async () => {
  const calls = setup(); const onSaved = vi.fn(); const success = vi.fn(); window.addEventListener("academy-save-success", success);
  render(<GroupPlacement id="group" sportId="sport" onSaved={onSaved} />);
  const checkbox = await screen.findByRole("checkbox", { name: /لاعب اصطناعي/ }); expect(screen.queryByRole("button", { name: "تأكيد تسكين الدفعة" })).not.toBeInTheDocument();
  fireEvent.click(checkbox); expect(checkbox.closest("article")).toHaveClass("is-selected"); fireEvent.click(screen.getByRole("button", { name: "معاينة التسكين" })); await screen.findByRole("region", { name: "معاينة التسكين" }); expect(success).not.toHaveBeenCalled(); expect(onSaved).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "تأكيد تسكين الدفعة" })); await waitFor(() => expect(onSaved).toHaveBeenCalledOnce()); expect(calls[0].body).toEqual({ enrollmentIds: ["e1"] }); expect(calls[1].body).toEqual({ enrollmentIds: ["e1"], fingerprint: "reviewed" }); expect(new Headers(calls[1].headers).get("Idempotency-Key")).toBeTruthy(); expect(screen.getByText(/تم تسكين 1 لاعب/)).toBeInTheDocument(); window.removeEventListener("academy-save-success", success);
});
it("shows individual eligibility reasons and disables unavailable players", async () => {
  setup({ candidates: { ...candidates, players: [{ ...candidate, reasons: ["لا يوجد استحقاق تدريب سارٍ"] }] } }); render(<GroupPlacement id="group" sportId="sport" onSaved={vi.fn()} />); expect(await screen.findByText("لا يوجد استحقاق تدريب سارٍ")).toBeVisible(); expect(screen.getByRole("checkbox")).toBeDisabled(); expect(screen.getByRole("button", { name: "معاينة التسكين" })).toBeDisabled();
});
it("blocks a changed preview and clears preview when selection changes", async () => {
  setup({ preview: { ...preview, conflicts: ["المقاعد المحجوزة لا تكفي"] } }); render(<GroupPlacement id="group" sportId="sport" onSaved={vi.fn()} />); const checkbox = await screen.findByRole("checkbox"); fireEvent.click(checkbox); fireEvent.click(screen.getByRole("button", { name: "معاينة التسكين" })); await screen.findByRole("region", { name: "معاينة التسكين" }); expect(screen.getByRole("button", { name: "تأكيد تسكين الدفعة" })).toBeDisabled(); fireEvent.click(checkbox); expect(screen.queryByRole("region", { name: "معاينة التسكين" })).not.toBeInTheDocument();
});
it("shows failed persistence truthfully and preserves idempotency key on transport retry", async () => {
  const calls = setup({ saveFails: true }); const onSaved = vi.fn(); render(<GroupPlacement id="group" sportId="sport" onSaved={onSaved} />); fireEvent.click(await screen.findByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "معاينة التسكين" })); const save = await screen.findByRole("button", { name: "تأكيد تسكين الدفعة" }); fireEvent.click(save); await screen.findByRole("alert"); expect(onSaved).not.toHaveBeenCalled(); fireEvent.click(save); await waitFor(() => expect(calls).toHaveLength(3)); expect(new Headers(calls[1].headers).get("Idempotency-Key")).toBe(new Headers(calls[2].headers).get("Idempotency-Key"));
});
it("reviews the sport-specific level only after explicit confirmation", async () => {
  const calls = setup(); const onSaved = vi.fn(); render(<EnrollmentLevelReview enrollmentId="e1" sportId="sport" onSaved={onSaved} />); fireEvent.click(screen.getByText("مراجعة مستوى اللاعب")); await screen.findByRole("option", { name: "مبتدئ" }); expect(screen.queryByRole("option", { name: "رياضة أخرى" })).not.toBeInTheDocument(); fireEvent.change(screen.getByRole("combobox"), { target: { value: "level" } }); expect(calls).toHaveLength(0); fireEvent.submit(screen.getByRole("button", { name: "اعتماد المراجعة" }).closest("form")!); await waitFor(() => expect(onSaved).toHaveBeenCalledOnce()); expect(calls[0].path).toBe("/api/v1/people/enrollments/e1/placement-level"); expect(calls[0].body).toEqual({levelId:"level",version:7,note:null,requestId:expect.stringMatching(/^[a-f0-9-]{36}$/)});
});
it("keeps the suggested enrollment focused and selected without automatically previewing or saving", async () => {
  const calls = setup(); render(<GroupPlacement id="group" sportId="sport" onSaved={vi.fn()} focusEnrollmentId="e1" />);
  expect(await screen.findByRole("checkbox", { name: /لاعب اصطناعي/ })).toBeChecked();
  expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining("enrollmentId=e1")); expect(calls).toHaveLength(0);
  expect(screen.queryByRole("button", { name: "تأكيد تسكين الدفعة" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "عرض كل المنتظرين بدل اللاعب المحدد" }));
  await waitFor(() => expect(global.fetch).toHaveBeenLastCalledWith(expect.not.stringContaining("enrollmentId="))); expect(screen.getByRole("checkbox")).not.toBeChecked();
});
it("does not select a focused enrollment that no longer meets hard constraints", async () => {
  setup({ candidates: { ...candidates, players: [{ ...candidate, reasons: ["لا يوجد مقعد متاح حاليًا."] }] } }); render(<GroupPlacement id="group" sportId="sport" onSaved={vi.fn()} focusEnrollmentId="e1" />);
  const box = await screen.findByRole("checkbox"); expect(box).not.toBeChecked(); expect(box).toBeDisabled(); expect(screen.getByRole("button", { name: "معاينة التسكين" })).toBeDisabled();
});
