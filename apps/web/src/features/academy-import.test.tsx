import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { AcademyImport } from "./academy-import";
const report = { schemaVersion: "Academity.Import.2.1", sourceSystem: "synthetic", cutoffDate: "2026-09-28", rowCount: 1000, playerCount: 999, householdCount: 1, financiallyIncomplete: 1, canApprove: true, errors: [], rows: [{ row: 2, legacyPlayerId: "P1", legacyEnrollmentId: "E1", playerName: "طفل اصطناعي", guardianName: "ولي اصطناعي", guardianPhone: "01000000001", agreedNet: null, currency: "EGP", existingNew: "existing" }] };
const ok = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("starts with tenant template and import, previews without a create/approve/payment request", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok(report)); render(<AcademyImport />);
  expect(screen.getByRole("link", { name: /تنزيل قالب أكاديميتي/ })).toHaveAttribute("href", "/api/v1/people/import/template");
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["synthetic"], "test.xlsx")] } });
  await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" }); expect(screen.getByText("1000")).toBeVisible(); expect(screen.getByText("غير معروف — مراجعة مالية")).toBeVisible();
  expect(screen.getByText(/معاينة وليست نتيجة استيراد معتمدة/)).toBeVisible();
  const call = vi.mocked(fetch).mock.calls.find(c => String(c[0]).endsWith("/preview")); expect(call?.[1]?.body).toBeInstanceOf(FormData); expect(call?.[1]?.headers).toEqual({ "X-CSRF-TOKEN": "token" });
  expect(vi.mocked(fetch).mock.calls).toHaveLength(2); expect(screen.queryByRole("button", { name: /اعتماد/ })).not.toBeInTheDocument();
});
it("rejects wrong file types locally and never sends the contents", () => {
  global.fetch = vi.fn(); render(<AcademyImport />); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsm")] } }); expect(screen.getByText(/اختر ملف xlsx صالحًا/)).toBeVisible(); expect(fetch).not.toHaveBeenCalled();
});
it("shows actionable row errors and does not present failed data as a successful import", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok({ ...report, canApprove: false, rows: [], errors: [{ sheet: "Enrollments", row: 2, column: "GuardianPhone", message: "اكتب الهاتف كنص" }] })); render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } }); await screen.findByText("الملف يحتاج تصحيحًا"); expect(screen.getByText("اكتب الهاتف كنص")).toBeVisible(); expect(screen.getByRole("button", { name: /تنزيل تقرير الأخطاء/ })).toBeVisible(); expect(screen.queryByText("طفل اصطناعي")).not.toBeInTheDocument();
});
it("network failures remain Arabic and no retry can silently approve data", async () => {
  global.fetch = vi.fn(async () => { throw new Error("Network failed"); }); render(<AcademyImport />); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } }); await screen.findByText(/تعذر الاتصال أثناء المعاينة/); expect(screen.getByRole("button", { name: "استيراد ملف" })).toBeEnabled(); expect(screen.queryByText("Network failed")).not.toBeInTheDocument();
});
