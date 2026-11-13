import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerGroupChange } from "./player-group-change";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const date = "2026-12-01";
const preview = {
  fingerprint: "fresh", effectiveDate: date, sourceGroup: "مجموعة أ", targetGroup: "مجموعة ب",
  sourceGroupId: "a", targetGroupId: "b", conflicts: [],
  removedSessions: [{ id: "s1", sessionDate: "2026-12-02", startTime: "10:00", endTime: "11:00" }],
  addedSessions: [{ id: "s2", sessionDate: "2026-12-02", startTime: "13:00", endTime: "14:00" }],
};
const transfer = { id: "move", sourceGroupId: "a", targetGroupId: "b", effectiveDate: date };

function setup({
  conflicts = [],
  saveStatus = 200,
  records = () => [transfer],
}: {
  conflicts?: string[];
  saveStatus?: number;
  records?: () => typeof transfer[];
} = {}) {
  const calls: { path: string; body: unknown; headers?: HeadersInit }[] = [];
  global.fetch = vi.fn(async (input, init) => {
    const path = String(input);
    if (path.endsWith("/csrf")) return Response.json({ token: "token" });
    if (path.endsWith("/calendar")) return Response.json({ today: "2026-11-08" });
    if (path.endsWith("/group-transfers")) return Response.json(records());
    if (init?.method === "POST") {
      calls.push({ path, body: JSON.parse(String(init.body)), headers: init.headers });
      if (path.endsWith("/preview")) return Response.json({ ...preview, conflicts });
      return Response.json({ id: "move", title: "تغيرت البيانات؛ أعد المعاينة." }, { status: saveStatus });
    }
    return Response.json({ groups: [
      { id: "a", arabicName: "مجموعة أ", sportId: "sport", branchId: "branch" },
      { id: "b", arabicName: "مجموعة ب", sportId: "sport", branchId: "branch" },
      { id: "private", arabicName: "مجموعة خاصة", sportId: "sport", branchId: "branch", registrationFormat: "Private" },
      { id: "foreign", arabicName: "فرع آخر", sportId: "sport", branchId: "other" },
    ] });
  }) as typeof fetch;
  return calls;
}

async function select() {
  fireEvent.click(screen.getByText("تعديل المجموعة", { exact: true }));
  await screen.findByRole("option", { name: "مجموعة ب" });
  const start = screen.getByLabelText("بدء النقل");
  expect(start).toHaveValue("2026-11-08");
  expect(start).toHaveAttribute("min", "2026-11-08");
  expect(start).toHaveAttribute("max", "2027-11-08");
  fireEvent.change(screen.getByLabelText("تعديل المجموعة"), { target: { value: "b" } });
  fireEvent.change(screen.getByLabelText("بدء النقل"), { target: { value: date } });
}

async function review() {
  fireEvent.click(screen.getByRole("button", { name: "معاينة نقل المجموعة" }));
  return screen.findByRole("button", { name: "تأكيد نقل المجموعة" });
}

it("uses the academy date and shows both schedules before confirming the exact saved transfer", async () => {
  const calls = setup(), saved = vi.fn();
  render(<PlayerGroupChange playerId="player" enrollmentId="enrollment" groupId="a" onSaved={saved} />);
  await select();
  expect(screen.queryByText("مجموعة خاصة")).toBeNull();
  expect(screen.queryByText("فرع آخر")).toBeNull();
  expect(screen.queryByRole("button", { name: "تأكيد نقل المجموعة" })).toBeNull();
  const confirm = await review();
  expect(screen.getByRole("heading", { name: "مواعيد تخرج من جدول اللاعب" })).toBeVisible();
  expect(screen.getByRole("heading", { name: "مواعيد تضاف إلى جدول اللاعب" })).toBeVisible();
  expect(calls[0].body).toEqual({ groupId: "b", effectiveDate: date });
  fireEvent.click(confirm);
  await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(calls[1].body).toEqual({ groupId: "b", effectiveDate: date, fingerprint: "fresh" });
  expect(new Headers(calls[1].headers).get("Idempotency-Key")).toBeTruthy();
  expect(screen.getByText(/تم تسجيل نقل المجموعة/)).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "نقل آخر" }));
  expect(await screen.findByLabelText("تعديل المجموعة")).toHaveValue("");
  expect(screen.queryByRole("button", { name: "تأكيد نقل المجموعة" })).toBeNull();
});

it("blocks conflicts and removes the preview when the date changes", async () => {
  const calls = setup({ conflicts: ["المجموعة مكتملة"] });
  render(<PlayerGroupChange playerId="player" enrollmentId="e" groupId="a" />);
  await select();
  expect(await review()).toBeDisabled();
  expect(screen.getByRole("alert")).toHaveTextContent("المجموعة مكتملة");
  fireEvent.change(screen.getByLabelText("بدء النقل"), { target: { value: "2026-12-03" } });
  expect(screen.queryByRole("button", { name: "تأكيد نقل المجموعة" })).toBeNull();
  expect(calls).toHaveLength(1);
});

it("reads the transfer after a lost confirmation response without posting again", async () => {
  const calls = setup({ saveStatus: 503 });
  render(<PlayerGroupChange playerId="player" enrollmentId="e" groupId="a" />);
  await select();
  fireEvent.click(await review());
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تأكيد النقل");
  fireEvent.click(screen.getByRole("button", { name: "تحقّق من سجل النقل" }));
  expect(await screen.findByText(/تم تسجيل نقل المجموعة/)).toBeVisible();
  expect(calls).toHaveLength(2);
});

it("keeps an acknowledged transfer locked until the exact receipt can be read", async () => {
  let exact = false;
  const calls = setup({ records: () => [exact ? transfer : { ...transfer, id: "another" }] });
  render(<PlayerGroupChange playerId="player" enrollmentId="e" groupId="a" />);
  await select();
  fireEvent.click(await review());
  const check = await screen.findByRole("button", { name: "تحقّق من سجل النقل" });
  expect(screen.queryByText(/تم تسجيل نقل المجموعة/)).toBeNull();
  expect(screen.getByRole("button", { name: "معاينة نقل المجموعة" })).toBeDisabled();
  exact = true;
  fireEvent.click(check);
  expect(await screen.findByText(/تم تسجيل نقل المجموعة/)).toBeVisible();
  expect(calls).toHaveLength(2);
});

it("requires a new preview after a conflict and a read that finds no matching transfer", async () => {
  const calls = setup({ saveStatus: 409, records: () => [] });
  render(<PlayerGroupChange playerId="player" enrollmentId="e" groupId="a" />);
  await select();
  fireEvent.click(await review());
  fireEvent.click(await screen.findByRole("button", { name: "تحقّق من سجل النقل" }));
  expect(await screen.findByText(/لا يوجد نقل مسجّل/)).toBeVisible();
  expect(screen.queryByRole("button", { name: "تأكيد نقل المجموعة" })).toBeNull();
  expect(calls).toHaveLength(2);
});
