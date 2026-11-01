import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { CoachAssignmentForm } from "./coach-assignment-form";
import { ownerDecisionRecords } from "./owner-home-decisions";

const options = { coaches: [{ id: "coach", arabicName: "مدرب اصطناعي" }], groups: [{ id: "group", arabicName: "مجموعة مختارة", branchId: "branch" }, { id: "outside", arabicName: "مجموعة فرع آخر", branchId: "other" }] };
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

for (const mismatch of [false, true]) it(`announces assignment only after exact active source read-back, mismatch=${mismatch}`, async () => {
  const saved = vi.fn(), announced = vi.fn(), calls: string[] = [];
  window.addEventListener("academy-save-success", announced);
  global.fetch = vi.fn(async (input, init) => {
    const path = String(input); calls.push(`${init?.method ?? "GET"} ${path}`);
    if (path.endsWith("/csrf")) return new Response(JSON.stringify({ token: "csrf" }));
    if (init?.method === "POST") { expect(JSON.parse(String(init.body))).toEqual({ membershipId: "coach" }); return new Response(JSON.stringify("assignment"), { status: 201 }); }
    return new Response(JSON.stringify({ id: "assignment", trainingGroupId: mismatch ? "wrong" : "group", academyMembershipId: "coach", isActive: true }));
  }) as typeof fetch;
  try {
    render(<CoachAssignmentForm loaded options={options} coachId="coach" branchId="branch" onSaved={saved} onCancel={vi.fn()} />);
    expect(screen.queryByRole("option", { name: "مجموعة فرع آخر" })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("المجموعة"), { target: { value: "group" } });
    fireEvent.click(screen.getByRole("button", { name: "حفظ" }));
    await waitFor(() => expect(calls).toContain("GET /api/v1/manage/groups/group/coaches/coach"));
    if (mismatch) {
      expect(await screen.findByText(/لم يتطابق الإسناد/)).toBeVisible(); expect(saved).not.toHaveBeenCalled(); expect(announced).not.toHaveBeenCalled();
    } else { await waitFor(() => expect(saved).toHaveBeenCalledOnce()); expect(announced).toHaveBeenCalledOnce(); }
  } finally { window.removeEventListener("academy-save-success", announced); }
});

it("rejects a revoked preset and maps malformed server data to Arabic without announcing success", async () => {
  const saved = vi.fn();
  global.fetch = vi.fn(async input => String(input).endsWith("/csrf") ? new Response('{"token":"csrf"}') : new Response("not-json")) as typeof fetch;
  const { rerender } = render(<CoachAssignmentForm loaded options={options} coachId="revoked" groupId="group" onSaved={saved} onCancel={vi.fn()} />);
  expect(screen.getByRole("button", { name: "حفظ" })).toBeDisabled();
  expect(screen.getByText(/المدرب المحدد غير متاح/)).toBeVisible();
  rerender(<CoachAssignmentForm key="valid" loaded options={options} coachId="coach" groupId="group" onSaved={saved} onCancel={vi.fn()} />);
  fireEvent.click(screen.getByRole("button", { name: "حفظ" }));
  expect(await screen.findByText("تعذر حفظ الإسناد. راجع الاتصال وحاول مجددًا.")).toBeVisible(); expect(saved).not.toHaveBeenCalled();
});

it("keeps the owner decision branch on the assignment route", () => {
  const records = ownerDecisionRecords({ currency: "EGP", overdue: { amount: 0, families: 0 }, risk: { players: 0 }, capacity: { seats: 0 }, groupAttention: [], underAssignedCoaches: [{ id: "coach", name: "مدرب اصطناعي", groupCount: 1, target: 3 }] }, { from: "2026-10-01", to: "2026-10-22", branchId: "branch" });
  expect(records[0].href).toBe("/dashboard/academy/coaches/new?coachId=coach&branchId=branch");
});
