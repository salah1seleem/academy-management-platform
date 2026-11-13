import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerSessionChange } from "./player-session-change";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const source = { id: "s1", group: "المجموعة الأصلية", date: "2026-11-15", start: "10:00", end: "11:00" };
const target = { ...source, id: "s2", group: "المجموعة البديلة", start: "13:00", end: "14:00" };
const pair = { sourceSessionId: "s1", targetSessionId: "s2" };
const move = { ...pair, batchId: "batch" };

function setup({ conflicts = [], status = 200, moves = () => [move], multiple = false }: {
  conflicts?: string[]; status?: number; moves?: () => typeof move[]; multiple?: boolean;
} = {}) {
  const writes: { path: string; body: unknown; headers: Headers }[] = [];
  const otherSource = { ...source, id: "s3", date: "2026-11-16" };
  const otherTarget = { ...target, id: "s4", date: "2026-11-16" };
  global.fetch = vi.fn(async (input, init) => {
    const path = String(input);
    if (path.endsWith("/csrf")) return Response.json({ token: "csrf" });
    if (path.endsWith("/players/p")) return Response.json({ enrollments: [{ id: "e", sessionMoves: moves() }] });
    if (path.includes("/options")) return Response.json({
      original: [source, ...(multiple ? [otherSource] : [])],
      alternative: path.includes("targetGroup=") ? [target, ...(multiple ? [otherTarget] : [])] : [],
    });
    writes.push({ path, body: JSON.parse(String(init?.body)), headers: new Headers(init?.headers) });
    if (path.endsWith("/preview")) return Response.json({
      fingerprint: "review", conflicts, changes: [
        { source, target, expectedPlayers: 1, capacity: 2 },
        ...(multiple ? [{ source: otherSource, target: otherTarget, expectedPlayers: 2, capacity: 3 }] : []),
      ],
    });
    return Response.json({ id: "batch", title: "تغير الموعد؛ أعد المعاينة." }, { status });
  }) as typeof fetch;
  return writes;
}

function mount(onSaved?: () => void) {
  return render(<PlayerSessionChange playerId="p" enrollmentId="e" groups={[{ id: "g2", arabicName: target.group }]} onSaved={onSaved} />);
}

async function select(multiple = false) {
  fireEvent.click(screen.getByText("نقل حصص مختارة", { exact: true }));
  fireEvent.change(await screen.findByLabelText("مجموعة الحصة البديلة"), { target: { value: "g2" } });
  fireEvent.click(await screen.findByRole("checkbox", { name: /15 نوفمبر/ }));
  fireEvent.change(screen.getByRole("combobox", { name: /بديل حصة 15 نوفمبر/ }), { target: { value: "s2" } });
  if (multiple) {
    fireEvent.click(screen.getByRole("checkbox", { name: /16 نوفمبر/ }));
    fireEvent.change(screen.getByRole("combobox", { name: /بديل حصة 16 نوفمبر/ }), { target: { value: "s4" } });
  }
}

async function review() {
  fireEvent.click(screen.getByRole("button", { name: "معاينة نقل الحصص" }));
  return screen.findByRole("button", { name: "تأكيد نقل الحصص" });
}

it("reviews the original and replacement, then reads the exact saved batch", async () => {
  const writes = setup(), saved = vi.fn();
  mount(saved);
  await select();
  const confirm = await review();
  expect(screen.getByText("الحصة الأصلية", { exact: true })).toBeVisible();
  expect(screen.getByText("الحصة البديلة", { exact: true })).toBeVisible();
  expect(writes[0].body).toEqual({ pairs: [pair] });
  fireEvent.click(confirm);
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(writes[1].body).toEqual({ pairs: [pair], fingerprint: "review" });
  expect(writes[1].headers.get("Idempotency-Key")).toBeTruthy();
  expect(screen.getByText("تم تسجيل نقل الحصص المختارة.")).toBeVisible();
});

it("blocks a conflict and discards the preview when selection changes", async () => {
  setup({ conflicts: ["لا يوجد مقعد"] });
  mount();
  await select();
  expect(await review()).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox"));
  expect(screen.queryByRole("button", { name: "تأكيد نقل الحصص" })).toBeNull();
});

it("checks a conflict with a read and requires a fresh preview if no batch exists", async () => {
  const writes = setup({ status: 409, moves: () => [] });
  mount();
  await select();
  fireEvent.click(await review());
  await screen.findByText("تغير الموعد؛ أعد المعاينة.");
  fireEvent.click(screen.getByRole("button", { name: "تحقّق من سجل النقل" }));
  expect(await screen.findByText(/لم يظهر نقل مطابق/)).toBeVisible();
  expect(screen.queryByRole("button", { name: "تأكيد نقل الحصص" })).toBeNull();
  expect(writes).toHaveLength(2);
});

it("does not combine rows from separate batches after an uncertain response", async () => {
  const writes = setup({
    status: 503, multiple: true,
    moves: () => [move, { batchId: "other", sourceSessionId: "s3", targetSessionId: "s4" }],
  });
  const saved = vi.fn();
  mount(saved);
  await select(true);
  fireEvent.click(await review());
  fireEvent.click(await screen.findByRole("button", { name: "تحقّق من سجل النقل" }));
  await screen.findByText(/لم يظهر نقل مطابق/);
  expect(saved).not.toHaveBeenCalled();
  expect(writes).toHaveLength(2);
});

it("recovers a committed batch after a lost response without sending again", async () => {
  const writes = setup({ status: 503 });
  const saved = vi.fn();
  mount(saved);
  await select();
  fireEvent.click(await review());
  fireEvent.click(await screen.findByRole("button", { name: "تحقّق من سجل النقل" }));
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(writes).toHaveLength(2);
});
