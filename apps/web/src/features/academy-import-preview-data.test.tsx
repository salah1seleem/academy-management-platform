import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { AcademyImport } from "./academy-import";

const row = { row: 2, legacyPlayerId: "PL01", legacyEnrollmentId: "EN-PL01", playerName: "لاعب اصطناعي", guardianName: "ولي أمر اصطناعي", guardianPhone: "+201000000099", start: "2026-10-01", end: "2026-10-31", agreedNet: "900.00", currency: "EGP", existingNew: "existing" };
const report = { schemaVersion: "iAcademy.Connected.3", fileHash: "test-hash", referenceStamp: "test-reference", sourceSystem: "test-import", cutoffDate: "2026-11-18", rowCount: 1, playerCount: 1, householdCount: 1, financiallyIncomplete: 0, canApprove: true, errors: [], rows: [row] };
const issue = { sheet: "اللاعبون", row: 3, column: "الاسم الأول", message: "حقل مطلوب." };
const split = { ...report, canApprove: false, errors: [issue], cleanRows: { playerCount: 1, excludedRows: 1 } };
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it.each([
  { name: "null issue", data: { ...split, errors: [null] } },
  { name: "missing issue field", data: { ...split, errors: [{ sheet: "اللاعبون", row: 3, message: "حقل مطلوب." }] } },
  { name: "invalid issue column type", data: { ...split, errors: [{ ...issue, column: 3 }] } },
  { name: "invalid issue row", data: { ...split, errors: [{ ...issue, row: "3" }] } },
  { name: "negative row count", data: { ...report, rowCount: -1 } },
  { name: "fractional player count", data: { ...report, playerCount: 1.5 } },
  { name: "text financial count", data: { ...report, financiallyIncomplete: "0" } },
  { name: "negative household count", data: { ...report, householdCount: -1 } },
  { name: "approved missing source", data: { ...report, sourceSystem: null } },
  { name: "approved missing cutoff", data: { ...report, cutoffDate: null } },
  { name: "string approval flag", data: { ...report, canApprove: "true" } },
  { name: "approval with errors", data: { ...report, errors: [issue] } },
  { name: "negative clean count", data: { ...split, cleanRows: { playerCount: -1, excludedRows: 1 } } },
  { name: "text excluded count", data: { ...split, cleanRows: { playerCount: 1, excludedRows: "1" } } },
  { name: "invalid clean summary", data: { ...split, cleanRows: [] } },
  { name: "null preview row", data: { ...report, rows: [null] } },
  { name: "missing row identity", data: { ...report, rows: [{ ...row, legacyPlayerId: "" }] } },
  { name: "missing player name", data: { ...report, rows: [{ ...row, playerName: null }] } },
  { name: "invalid JSON", raw: "{invalid-json", data: report },
])("recovers $name without a partial preview or commit", async failure => {
  let broken = true;
  const writes: string[] = [];
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input);
    if (path.endsWith("/jobs")) return Response.json([]);
    if (path.endsWith("/schema")) return Response.json({ connectedSheets: [], maxRows: 1000, maxFileBytes: 5242880 });
    if (path.endsWith("/csrf")) return Response.json({ token: "test-csrf" });
    expect(path.endsWith("/preview")).toBe(true); expect(init?.method).toBe("POST"); writes.push(path);
    return broken && failure.raw ? new Response(failure.raw, { headers: { "Content-Type": "application/json" } }) : Response.json(broken ? failure.data : report);
  }));
  render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["synthetic"], "field-boundaries.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(/تعذر/);
  expect(screen.queryByRole("region", { name: "نتيجة معاينة الاستيراد" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "اعتماد وبدء الاستيراد" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "تنزيل ملف اللاعبين السليمين" })).not.toBeInTheDocument();
  expect(screen.getByText("field-boundaries.xlsx")).toBeVisible();
  broken = false;
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  expect(await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" })).toBeVisible();
  expect(writes).toEqual(["/api/v1/people/import/preview", "/api/v1/people/import/preview"]);
});

it("preserves legitimate metadata rejection details when source and cutoff are unknown", async () => {
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input);
    if (path.endsWith("/jobs")) return Response.json([]);
    if (path.endsWith("/schema")) return Response.json({ connectedSheets: [], maxRows: 1000, maxFileBytes: 5242880 });
    if (path.endsWith("/csrf")) return Response.json({ token: "test-csrf" });
    expect(path.endsWith("/preview")).toBe(true); expect(init?.method).toBe("POST");
    return Response.json({ ...report, sourceSystem: null, cutoffDate: null, canApprove: false, rows: [], errors: [{ sheet: "Metadata", sheetLabel: "إعداد الملف", row: 1, column: "SourceSystem", columnLabel: "كود المصدر", message: "كود المصدر مطلوب." }] });
  }));
  render(<AcademyImport />); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["synthetic"], "missing-metadata.xlsx")] } }); fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  expect(await screen.findByRole("heading", { name: "الملف يحتاج تصحيحًا" })).toBeVisible(); expect(screen.getByText("كود المصدر مطلوب.")).toBeVisible(); expect(screen.queryByText("تعذر قراءة نتيجة الفحص.")).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "اعتماد وبدء الاستيراد" })).not.toBeInTheDocument();
});

it("preserves a legitimate sheet-header rejection with an empty column key", async () => {
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input);
    if (path.endsWith("/jobs")) return Response.json([]);
    if (path.endsWith("/schema")) return Response.json({ connectedSheets: [], maxRows: 1000, maxFileBytes: 5242880 });
    if (path.endsWith("/csrf")) return Response.json({ token: "test-csrf" });
    expect(path.endsWith("/preview")).toBe(true);
    return Response.json({ ...report, canApprove: false, rows: [], errors: [{ sheet: "Enrollments", sheetLabel: "التسجيلات", row: 1, column: "", columnLabel: "بنية الملف", message: "عناوين الأعمدة لا تطابق نسخة القالب." }] });
  }));
  render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["synthetic"], "invalid-headers.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  expect(await screen.findByRole("heading", { name: "الملف يحتاج تصحيحًا" })).toBeVisible();
  expect(screen.getByText("عناوين الأعمدة لا تطابق نسخة القالب.")).toBeVisible();
  expect(screen.getByRole("table", { name: "مشكلات الاستيراد" })).toHaveTextContent("بنية الملف");
  expect(screen.queryByRole("button", { name: "اعتماد وبدء الاستيراد" })).not.toBeInTheDocument();
});
