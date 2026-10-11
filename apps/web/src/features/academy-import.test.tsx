import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { AcademyImport } from "./academy-import";
const report = { schemaVersion: "Academity.Import.2.1", fileHash: "file-hash", referenceStamp: "reference-stamp", sourceSystem: "synthetic", cutoffDate: "2026-09-28", rowCount: 1000, playerCount: 999, householdCount: 1, financiallyIncomplete: 1, canApprove: true, errors: [], rows: [{ row: 2, legacyPlayerId: "P1", legacyEnrollmentId: "E1", playerName: "طفل اصطناعي", guardianName: "ولي اصطناعي", guardianPhone: "01000000001", agreedNet: null, currency: "EGP", existingNew: "existing" }] };
const ok = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("starts with tenant template and import, previews without a create/approve/payment request", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok(report)); render(<AcademyImport />);
  expect(screen.getByRole("link", { name: /تنزيل قالب أكاديميتي/ })).toHaveAttribute("href", "/api/v1/people/import/template");
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["synthetic"], "test.xlsx")] } });
  await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" }); expect(screen.getByText("1000")).toBeVisible(); expect(screen.getByText("غير معروف — مراجعة مالية")).toBeVisible();
  expect(screen.getByText(/معاينة وليست نتيجة استيراد معتمدة/)).toBeVisible();
  const call = vi.mocked(fetch).mock.calls.find(c => String(c[0]).endsWith("/preview")); expect(call?.[1]?.body).toBeInstanceOf(FormData); expect(call?.[1]?.headers).toEqual({ "X-CSRF-TOKEN": "token" });
  expect(vi.mocked(fetch).mock.calls.filter(c => c[1]?.method === "POST")).toHaveLength(1); expect(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toBeDisabled();
});
it("rejects wrong file types locally and never sends the contents", () => {
  global.fetch = vi.fn(async () => ok([])); render(<AcademyImport />); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsm")] } }); expect(screen.getByText(/اختر ملف xlsx صالحًا/)).toBeVisible(); expect(vi.mocked(fetch).mock.calls.filter(c => c[1]?.method === "POST")).toHaveLength(0);
});
it("shows actionable row errors and does not present failed data as a successful import", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok({ ...report, canApprove: false, rows: [], errors: [{ sheet: "Enrollments", row: 2, column: "GuardianPhone", message: "اكتب الهاتف كنص" }] })); render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } }); await screen.findByText("الملف يحتاج تصحيحًا"); expect(screen.getByText("اكتب الهاتف كنص")).toBeVisible(); expect(screen.getByRole("button", { name: /تنزيل تقرير الأخطاء/ })).toBeVisible(); expect(screen.queryByText("طفل اصطناعي")).not.toBeInTheDocument();
});
it("network failures remain Arabic and no retry can silently approve data", async () => {
  global.fetch = vi.fn(async () => { throw new Error("Network failed"); }); render(<AcademyImport />); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } }); await screen.findByText(/تعذر الاتصال أثناء المعاينة/); expect(screen.getByRole("button", { name: "استيراد ملف" })).toBeEnabled(); expect(screen.queryByText("Network failed")).not.toBeInTheDocument();
});
const job = { id: "job-1", sourceSystem: "synthetic", status: "Completed", rowCount: 1, publishedRows: 1, replayedRows: 0, financiallyIncomplete: 0 };
const records = [{ id: "player-1", legacyEnrollmentId: "E1", player: "طفل معتمد", playerCode: "IMP-1", sport: "سباحة", branch: "فرع صناعي", groupName: "مجموعة صناعية", priceSnapshot: null, currencySnapshot: "EGP" }];
it("requires relationship and incomplete-finance review before explicit durable commit and shows real published links", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : String(url).endsWith("/jobs") ? ok([]) : String(url).endsWith("/records") ? ok(records) : String(url).endsWith("/commit") ? ok(job, 202) : ok(report)); render(<AcademyImport />);
  const file = new File(["synthetic"], "test.xlsx"); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [file] } }); await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" });
  fireEvent.click(screen.getByRole("checkbox", { name: /راجعت علاقات/ })); expect(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toBeDisabled(); fireEvent.click(screen.getByRole("checkbox", { name: /أقر أن/ })); fireEvent.click(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" }));
  await screen.findByRole("heading", { name: "اكتمل الاستيراد" }); expect(await screen.findByRole("link", { name: /طفل معتمد/ })).toHaveAttribute("href", "/dashboard/players/player-1");
  const commit = vi.mocked(fetch).mock.calls.find(c => String(c[0]).endsWith("/commit"))![1]!; const form = commit.body as FormData; expect(form.get("file")).toBe(file); expect(form.get("ExpectedFileHash")).toBe("file-hash"); expect(form.get("ExpectedReferenceStamp")).toBe("reference-stamp"); expect(form.get("GuardianLinksApproved")).toBe("true"); expect(form.get("AcceptFinancialReview")).toBe("true"); expect(commit.headers).toHaveProperty("Idempotency-Key"); expect(vi.mocked(fetch).mock.calls.some(c => /payment|collection|receipt/.test(String(c[0])))).toBe(false);
});
it("uncertain commit network response retries the same intent rather than creating a second import", async () => {
  let attempts = 0; global.fetch = vi.fn(async url => { if (String(url).endsWith("/csrf")) return ok({ token: "token" }); if (String(url).endsWith("/jobs")) return ok([]); if (String(url).endsWith("/records")) return ok(records); if (String(url).endsWith("/commit")) { if (++attempts === 1) throw new Error("network"); return ok(job); } return ok({ ...report, financiallyIncomplete: 0 }); }); render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } }); await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" }); fireEvent.click(screen.getByRole("checkbox", { name: /راجعت علاقات/ })); fireEvent.click(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" })); await screen.findByText(/أعد المحاولة بنفس الصفحة/); fireEvent.click(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" })); await screen.findByRole("heading", { name: "اكتمل الاستيراد" });
  const calls = vi.mocked(fetch).mock.calls.filter(c => String(c[0]).endsWith("/commit")); expect(calls).toHaveLength(2); expect(calls[0][1]?.headers).toEqual(calls[1][1]?.headers);
});
it("restores the durable completed job after closing the page without another approval POST", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/records") ? ok(records) : ok([job])); render(<AcademyImport />); await screen.findByRole("heading", { name: "اكتمل الاستيراد" }); await waitFor(() => expect(screen.getByRole("link", { name: /طفل معتمد/ })).toBeVisible()); expect(vi.mocked(fetch).mock.calls.some(c => c[1]?.method === "POST")).toBe(false);
});
