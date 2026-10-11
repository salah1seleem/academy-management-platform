import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { DiscountManagement, PlayerDiscounts } from "./discount-rules";

const rule = { id: "r", arabicName: "عرض مدرسة", type: "FixedAmount", percentage: 0, fixedAmount: "100.00", maximumAmount: "150.00", currency: "EGP", reason: "", isSiblingOffer: false, isActive: true, branchId: "a", sportId: "swim", subscriptionPlanId: "p", revision: 3 };
const options = { owner: true, restricted: false, branches: [{ id: "a", arabicName: "التجمع" }], sports: [{ id: "swim", arabicName: "السباحة" }], plans: [{ id: "p", arabicName: "باقة سباحة", sportId: "swim", allBranches: false, branchIds: ["a"] }] };
const ok = (value: unknown) => new Response(JSON.stringify(value));
function mock(restricted = false) { global.fetch = vi.fn(async (input, init) => {
  const path = String(input);
  if (path.endsWith("/csrf")) return ok({ token: "token" });
  if (init?.method === "PUT" || init?.method === "POST") return ok({ id: "r" });
  if (path.endsWith("/options")) return ok({ ...options, owner: !restricted, restricted });
  if (path.endsWith("/versions")) return ok([{ revision: 3, createdAtUtc: "2026-09-28T10:00:00Z", snapshot: rule }]);
  if (path.includes("/players/")) return ok([]);
  return ok({ items: [rule, { ...rule, id: "global", arabicName: "عرض عام", branchId: null }] });
}) as typeof fetch; }
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("saves fixed money as decimal strings with optional description and expected revision", async () => {
  mock(); render(<DiscountManagement />); await screen.findByRole("button", { name: "تعديل عرض مدرسة" }); fireEvent.click(screen.getByRole("button", { name: "تعديل عرض مدرسة" }));
  expect(screen.getByLabelText("وصف العرض — اختياري")).not.toBeRequired(); expect(screen.getByLabelText("قيمة الخصم")).toHaveValue(100);
  fireEvent.change(screen.getByLabelText("قيمة الخصم"), { target: { value: "120.50" } }); fireEvent.click(screen.getByRole("button", { name: "حفظ العرض" }));
  await screen.findByRole("button", { name: "إضافة خصم" });
  const call = vi.mocked(fetch).mock.calls.find(c => c[1]?.method === "PUT")!; expect(JSON.parse(String(call[1]?.body))).toMatchObject({ type: 2, fixedAmount: "120.50", percentage: "0", maximumAmount: "150.00", reason: null, expectedRevision: 3, branchId: "a", sportId: "swim", subscriptionPlanId: "p" });
});
it("switches the financial type without accidentally sending a stale fixed amount", async () => {
  mock(); render(<DiscountManagement />); fireEvent.click(await screen.findByRole("button", { name: "تعديل عرض مدرسة" }));
  fireEvent.change(screen.getByLabelText("نوع الخصم"), { target: { value: "Percentage" } }); fireEvent.change(screen.getByLabelText("النسبة المئوية"), { target: { value: "25" } }); fireEvent.click(screen.getByRole("button", { name: "حفظ العرض" }));
  await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(c => c[1]?.method === "PUT")).toBe(true));
  const call = vi.mocked(fetch).mock.calls.find(c => c[1]?.method === "PUT")!; expect(JSON.parse(String(call[1]?.body))).toMatchObject({ type: 1, fixedAmount: null, percentage: "25" });
});
it("branch admins cannot edit global rules or sibling offers and must select a branch", async () => {
  mock(true); render(<DiscountManagement />); await screen.findByRole("button", { name: "إضافة خصم" });
  expect(screen.queryByRole("button", { name: "تعديل عرض عام" })).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "تعديل عرض الإخوة" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إضافة خصم" })); expect(screen.getByLabelText("الفرع")).toBeRequired();
});
it("shows immutable human-readable rule history without mutation buttons", async () => {
  mock(); render(<DiscountManagement />); fireEvent.click(await screen.findByRole("button", { name: "سجل عرض مدرسة" })); await screen.findByRole("heading", { name: "سجل نسخ العرض" });
  expect(screen.getByText(/نسخة 3 · عرض مدرسة · 100 جنيه/)).toBeVisible(); expect(screen.queryByRole("button", { name: /حذف نسخة|تعديل نسخة/ })).not.toBeInTheDocument();
});
it("persistent player offers show fixed money, not a misleading zero percentage", async () => {
  mock(); render(<PlayerDiscounts playerId="player" />); expect(await screen.findByLabelText("عرض مدرسة · 100 جنيه")).toBeVisible();
  fireEvent.click(screen.getByLabelText("عرض مدرسة · 100 جنيه")); await screen.findByText(/تم تحديث الخصم المستمر/); expect(screen.getByLabelText("عرض مدرسة · 100 جنيه")).toBeChecked();
});
