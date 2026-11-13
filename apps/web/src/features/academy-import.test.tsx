import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { AcademyImport } from "./academy-import";
const report = { schemaVersion: "Academity.Import.2.1", fileHash: "file-hash", referenceStamp: "reference-stamp", sourceSystem: "synthetic", cutoffDate: "2026-09-28", rowCount: 1000, playerCount: 999, householdCount: 1, financiallyIncomplete: 1, canApprove: true, errors: [], rows: [{ row: 2, legacyPlayerId: "P1", legacyEnrollmentId: "E1", playerName: "طفل اصطناعي", guardianName: "ولي اصطناعي", guardianPhone: "01000000001", start: "2026-09-01", end: "2026-09-30", agreedNet: null, currency: "EGP", existingNew: "existing" }] };
const ok = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("starts with tenant template and import, previews without a create/approve/payment request", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok(report)); render(<AcademyImport />);
  expect(screen.getByRole("button", { name: /تنزيل قالب فارغ/ })).toBeEnabled();
  expect(screen.queryByText("تنزيل القالب السابق")).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["synthetic"], "test.xlsx")] } }); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"}));
  await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" }); expect(screen.getByText("1000")).toBeVisible(); expect(screen.getByText("غير معروف — مراجعة مالية")).toBeVisible();
  expect(screen.getByText("لم يُحفظ أي سجل.")).toBeVisible();
  expect(screen.getByText("E1")).toBeVisible();
  expect(screen.getByText(/تاريخ القطع 28 سبتمبر 2026/)).toBeVisible();
  expect(screen.getByText("البداية 1 سبتمبر 2026")).toBeVisible();
  expect(screen.getByText("النهاية 30 سبتمبر 2026")).toBeVisible();
  expect(screen.queryByRole("navigation", { name: "صفحات معاينة التسجيلات" })).not.toBeInTheDocument();
  const call = vi.mocked(fetch).mock.calls.find(c => String(c[0]).endsWith("/preview")); expect(call?.[1]?.body).toBeInstanceOf(FormData); expect(call?.[1]?.headers).toEqual({ "X-CSRF-TOKEN": "token" });
  expect(vi.mocked(fetch).mock.calls.filter(c => c[1]?.method === "POST")).toHaveLength(1); expect(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toBeDisabled();
});
it("rejects wrong file types locally and never sends the contents", () => {
  global.fetch = vi.fn(async () => ok([])); render(<AcademyImport />); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsm")] } }); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"})); expect(screen.getByText(/اختر ملف xlsx صالحًا/)).toBeVisible(); expect(vi.mocked(fetch).mock.calls.filter(c => c[1]?.method === "POST")).toHaveLength(0);
});
it("shows actionable row errors and does not present failed data as a successful import", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok({ ...report, canApprove: false, rows: [], errors: [{ sheet: "Enrollments", row: 2, column: "GuardianPhone", message: "اكتب الهاتف كنص" }] })); render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } }); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"})); await screen.findByText("الملف يحتاج تصحيحًا"); expect(screen.getByText("اكتب الهاتف كنص")).toBeVisible(); expect(screen.getByRole("button", { name: /تنزيل تقرير الأخطاء/ })).toBeVisible(); expect(screen.queryByText("طفل اصطناعي")).not.toBeInTheDocument();
  const issues = within(screen.getByRole("region", { name: "مراجعة أخطاء الملف" }));
  expect(issues.getByText(/الصف 2/)).toBeVisible();
  expect(issues.queryByLabelText("رقم الصف في Excel")).not.toBeInTheDocument();
  expect(issues.queryByText("الأعمدة")).not.toBeInTheDocument();
});
it("intersects sheet and original row filters, accepts both Arabic digit sets, and clears an empty result", async () => {
  const errors = [
    { sheet: "اللاعبون", row: 64, column: "الاسم الأول", message: "اكتب الاسم الأول" },
    { sheet: "اللاعبون", row: 64, column: "الاسم الأول", message: "اكتب الاسم الأول" },
    { sheet: "اللاعبون", row: 64, column: "موبايل ولي الأمر", message: "اكتب الهاتف كنص" },
    { sheet: "التسجيلات", row: 64, column: "كود المجموعة", message: "راجع كود المجموعة" },
    { sheet: "التسجيلات", row: 2, column: "كود الباقة", message: "راجع كود الباقة" },
  ];
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok({ ...report, canApprove: false, rows: [], errors }));
  render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  const issues = within(await screen.findByRole("region", { name: "مراجعة أخطاء الملف" }));
  expect(issues.getByRole("status")).toHaveTextContent(/المشكلات المعروضة 4.*الصفوف المتأثرة 3/);
  fireEvent.change(issues.getByLabelText("الورقة"), { target: { value: "اللاعبون" } });
  for (const value of ["٦٤", "۶۴"]) {
    fireEvent.change(issues.getByLabelText("رقم الصف في Excel"), { target: { value } });
    expect(issues.getByText("اكتب الاسم الأول")).toBeVisible();
    expect(issues.getByText("اكتب الهاتف كنص")).toBeVisible();
    expect(issues.getByRole("status")).toHaveTextContent(/المشكلات المعروضة 2.*الصفوف المتأثرة 1/);
  }
  fireEvent.change(issues.getByLabelText("الورقة"), { target: { value: "التسجيلات" } });
  expect(issues.getByText("راجع كود المجموعة")).toBeVisible();
  expect(issues.queryByText("راجع كود الباقة")).not.toBeInTheDocument();
  for (const value of ["65", "نص"]) {
    fireEvent.change(issues.getByLabelText("رقم الصف في Excel"), { target: { value } });
    expect(issues.getByText("لا توجد مشكلات تطابق التصفية.")).toBeVisible();
  }
  fireEvent.click(issues.getByRole("button", { name: "مسح التصفية" }));
  expect(issues.getByRole("status")).toHaveTextContent(/المشكلات المعروضة 4.*الصفوف المتأثرة 3/);
  expect(vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method === "POST")).toHaveLength(1);
});

it("finds an earlier Excel row from a later page and resets filters when the file is rechecked", async () => {
  const errors = Array.from({ length: 300 }, (_, index) => ({ sheet: "اللاعبون", row: index + 2, column: "الاسم الأول", message: `اكتب الاسم في الصف ${index + 2}` }));
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok({ ...report, canApprove: false, rows: [], errors }));
  render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  const issues = within(await screen.findByRole("region", { name: "مراجعة أخطاء الملف" }));
  expect(screen.getByText(/يعرض الفحص حتى 300 مشكلة/)).toBeVisible();
  fireEvent.click(issues.getByRole("button", { name: "الصفحة التالية" }));
  expect(issues.getByText("اكتب الاسم في الصف 27")).toBeVisible();
  fireEvent.change(issues.getByLabelText("رقم الصف في Excel"), { target: { value: "0002" } });
  expect(issues.getByText("اكتب الاسم في الصف 2")).toBeVisible();
  expect(issues.queryByRole("navigation")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة فحص الملف" }));
  const refreshed = within(await screen.findByRole("region", { name: "مراجعة أخطاء الملف" }));
  expect(refreshed.getByLabelText("رقم الصف في Excel")).toHaveValue("");
  expect(refreshed.getByRole("button", { name: "الصفحة السابقة" })).toBeDisabled();
});

it("offers a retry when a rejected preview has no error details", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok({ ...report, canApprove: false, rows: [] }));
  render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  expect(await screen.findByText(/تعذر عرض تفاصيل الفحص/)).toBeVisible();
  expect(screen.getByRole("button", { name: "إعادة فحص الملف" })).toBeEnabled();
  expect(screen.queryByLabelText("البيانات اجتازت فحص المعاينة")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "تنزيل تقرير الأخطاء" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "اعتماد وبدء الاستيراد" })).not.toBeInTheDocument();
});
it("network failures remain Arabic and no retry can silently approve data", async () => {
  global.fetch = vi.fn(async () => { throw new Error("Network failed"); }); render(<AcademyImport />); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } }); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"})); await screen.findByText(/تعذر الاتصال أثناء المعاينة/); expect(screen.getByRole("button", { name: "استيراد ملف" })).toBeEnabled(); expect(screen.queryByText("Network failed")).not.toBeInTheDocument();
});
it("explains an expired platform session even when the unauthorized response has no JSON body", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith('/csrf') ? ok({token:'demo-token'}) : String(url).endsWith('/preview') ? new Response(null,{status:401}) : ok([]));
  render(<AcademyImport academyId="academy-a"/>);
  fireEvent.change(screen.getByLabelText('ملف بيانات الأكاديمية'),{target:{files:[new File(['synthetic'],'private.xlsx')]}}); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"}));
  await screen.findByText(/انتهت جلسة الدخول/);
  expect(screen.queryByText(/تعذر الاتصال أثناء المعاينة/)).not.toBeInTheDocument();
  expect(vi.mocked(fetch).mock.calls.some(c=>String(c[0]).endsWith('/commit'))).toBe(false);
});
const job = { id: "job-1", sourceSystem: "synthetic", status: "Completed", rowCount: 1, publishedRows: 1, replayedRows: 0, financiallyIncomplete: 0 };
const records = [{ id: "player-1", legacyEnrollmentId: "E1", player: "طفل معتمد", playerCode: "IMP-1", sport: "سباحة", branch: "فرع صناعي", groupName: "مجموعة صناعية", startDate: "2026-09-01", endDate: null, priceSnapshot: null, currencySnapshot: "EGP" }];
it('identifies replayed historical receipts without describing them as new collection',async()=>{
 const replay={...job,publishedRows:0,replayedRows:1,result:{historicalPayments:1,historicalPaymentTotal:800}};
 global.fetch=vi.fn(async url=>String(url).endsWith('/records')?ok(records):String(url).endsWith('/jobs')?ok([replay]):ok([]));
 render(<AcademyImport/>);
 await screen.findByText(/لم يُنشأ قبض جديد عند إعادة الرفع/);
 expect(screen.getByText(/تم التحقق من/)).toBeVisible();
 expect(vi.mocked(fetch).mock.calls.some(c=>c[1]?.method==='POST')).toBe(false);
});
it("keeps the payment currency when records fail and labels an identical-file replay", async () => {
  const replay = { ...job, replayedRequest: true, result: {
    historicalPayments: 1, historicalPaymentTotal: 600.25, historicalPaymentCurrency: "USD",
  } };
  let reads = 0;
  global.fetch = vi.fn(async url => {
    const path = String(url);
    if (path.endsWith("/jobs")) return ok([replay]);
    if (path.endsWith("/records")) return ++reads === 1 ? ok({}, 503) : ok(records);
    return ok([]);
  });
  render(<AcademyImport />);
  const message = await screen.findByText(/لم يُنشأ قبض جديد عند إعادة الرفع/);
  expect(message).toHaveTextContent(/600\.25.*دولار/);
  expect(message).not.toHaveTextContent("جنيه");
  expect(screen.getByText("التسجيلات الجديدة 1")).toBeVisible();
  fireEvent.click(await screen.findByRole("button", { name: "إعادة تحميل تفاصيل الدفعة" }));
  await screen.findByRole("link", { name: /طفل معتمد/ });
  expect(message).toHaveTextContent(/600\.25.*دولار/);
});

it("keeps an old payment total without assuming a currency and can recover it from the records", async () => {
  let finish!: (response: Response) => void;
  const pending = new Promise<Response>(resolve => { finish = resolve; });
  const older = { ...job, result: { historicalPayments: 1, historicalPaymentTotal: 0, historicalPaymentCurrency: "bad value" } };
  global.fetch = vi.fn(async url => String(url).endsWith("/jobs") ? ok([older]) : String(url).endsWith("/records") ? pending : ok([]));
  render(<AcademyImport />);
  const message = await screen.findByText(/والعملة غير متاحة/);
  expect(message).toHaveTextContent("الإجمالي 0");
  expect(message).not.toHaveTextContent("جنيه");
  await act(async () => { finish(ok(records)); });
  expect(message).toHaveTextContent(/0\s*جنيه/);
});

it("requires historical payment approval and shows the preview's payment currency", async () => {
  const preview = { ...report, financiallyIncomplete: 0, historicalPayments: 1, historicalPaymentTotal: 600, historicalPaymentCurrency: "USD" };
  global.fetch = vi.fn(async url => {
    const path = String(url);
    if (path.endsWith("/csrf")) return ok({ token: "token" });
    if (path.endsWith("/preview")) return ok(preview);
    return ok([]);
  });
  render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["test"], "history.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  const payment = await screen.findByRole("checkbox", { name: /راجعت سندات 1 تحصيلًا سابقًا.*600.*دولار/ });
  fireEvent.click(screen.getByRole("checkbox", { name: /راجعت الأسماء والهواتف/ }));
  const approve = screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" });
  expect(approve).toBeDisabled();
  fireEvent.click(payment);
  expect(approve).toBeEnabled();
  expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url).endsWith("/commit"))).toBe(false);
});
it("requires relationship and incomplete-finance review before explicit durable commit and shows real published links", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : String(url).endsWith("/jobs") ? ok([]) : String(url).endsWith("/records") ? ok(records) : String(url).endsWith("/commit") ? ok(job, 202) : ok(report)); render(<AcademyImport />);
  const file = new File(["synthetic"], "test.xlsx"); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [file] } }); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"})); await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" });
  fireEvent.click(screen.getByRole("checkbox", { name: /راجعت الأسماء والهواتف/ })); expect(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toBeDisabled(); fireEvent.click(screen.getByRole("checkbox", { name: /أقر أن/ })); fireEvent.click(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" }));
  await screen.findByRole("heading", { name: "اكتمل الاستيراد" }); expect(await screen.findByRole("link", { name: /طفل معتمد/ })).toHaveAttribute("href", "/dashboard/players/player-1");
  expect(screen.getByText("البداية 1 سبتمبر 2026")).toBeVisible();
  expect(screen.queryByText(/^النهاية/)).not.toBeInTheDocument();
  const commit = vi.mocked(fetch).mock.calls.find(c => String(c[0]).endsWith("/commit"))![1]!; const form = commit.body as FormData; expect(form.get("file")).toBe(file); expect(form.get("ExpectedFileHash")).toBe("file-hash"); expect(form.get("ExpectedReferenceStamp")).toBe("reference-stamp"); expect(form.get("GuardianLinksApproved")).toBe("true"); expect(form.get("AcceptFinancialReview")).toBe("true"); expect(commit.headers).toHaveProperty("Idempotency-Key"); expect(vi.mocked(fetch).mock.calls.some(c => /payment|collection|receipt/.test(String(c[0])))).toBe(false);
});
it("uncertain commit locks the whole draft and replays the same approval", async () => {
  let attempts = 0;
  global.fetch = vi.fn(async url => {
    const path = String(url);
    if (path.endsWith("/csrf")) return ok({ token: "token" });
    if (path.endsWith("/jobs")) return ok([]);
    if (path.includes("/by-request")) return new Response(null, { status: 404 });
    if (path.endsWith("/records")) return ok(records);
    if (path.endsWith("/commit")) {
      if (++attempts === 1) throw new Error("network");
      if (attempts === 2) return ok({ status: "Completed" });
      return ok(job);
    }
    return ok({ ...report, financiallyIncomplete: 0 });
  });
  render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" });
  const approved = screen.getByRole("checkbox", { name: /راجعت الأسماء والهواتف/ });
  fireEvent.click(approved);
  fireEvent.click(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" }));
  const verify = await screen.findByRole("button", { name: "التحقق من حالة الاعتماد" });
  expect(approved).toBeDisabled();
  expect(screen.getByLabelText("ملف بيانات الأكاديمية")).toBeDisabled();
  expect(screen.getByRole("button", { name: "إعادة فحص الملف" })).toBeDisabled();
  fireEvent.click(verify);
  await screen.findByText(/لم تظهر دفعة مرتبطة بهذا الطلب بعد/);
  expect(attempts).toBe(1);
  fireEvent.click(screen.getByRole("button", { name: "إعادة إرسال الطلب نفسه" }));
  await screen.findByText(/تعذر تأكيد اعتماد الدفعة/);
  expect(screen.queryByRole("heading", { name: "اكتمل الاستيراد" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة إرسال الطلب نفسه" }));
  await screen.findByRole("heading", { name: "اكتمل الاستيراد" });
  const calls = vi.mocked(fetch).mock.calls.filter(([url]) => String(url).endsWith("/commit"));
  expect(calls).toHaveLength(3);
  for (const [, replay] of calls.slice(1)) {
    expect(replay?.headers).toEqual(calls[0][1]?.headers);
    expect(Array.from((replay?.body as FormData).entries())).toEqual(Array.from((calls[0][1]?.body as FormData).entries()));
  }
});

it("retrying saved records refreshes only the details of the existing batch", async () => {
  let reads = 0;
  global.fetch = vi.fn(async url => String(url).endsWith("/records")
    ? ++reads === 1 ? new Response(null, { status: 503 }) : ok(records)
    : String(url).endsWith("/jobs") ? ok([job]) : ok([]));
  render(<AcademyImport />);
  await screen.findByRole("heading", { name: "اكتمل الاستيراد" });
  fireEvent.click(await screen.findByRole("button", { name: "إعادة تحميل تفاصيل الدفعة" }));
  await screen.findByRole("link", { name: /طفل معتمد/ });
  expect(reads).toBe(2);
  expect(vi.mocked(fetch).mock.calls.some(([, request]) => request?.method === "POST")).toBe(false);
});
it("hides protected details while a revoked job read is retried", async () => {
  let reads = 0;
  let finish!: (response: Response) => void;
  const pending = new Promise<Response>(resolve => { finish = resolve; });
  global.fetch = vi.fn(async url => String(url).endsWith("/records")
    ? ++reads === 1 ? new Response(null, { status: 403 }) : pending
    : String(url).endsWith("/jobs") ? ok([job]) : ok([]));
  render(<AcademyImport />);
  const retry = await screen.findByRole("button", { name: "إعادة تحميل تفاصيل الدفعة" });
  expect(screen.queryByRole("heading", { name: "اكتمل الاستيراد" })).not.toBeInTheDocument();
  fireEvent.click(retry);
  expect(retry).toBeDisabled();
  expect(screen.queryByRole("heading", { name: "اكتمل الاستيراد" })).not.toBeInTheDocument();
  await act(async () => { finish(ok(records)); });
  expect(screen.getByRole("heading", { name: "اكتمل الاستيراد" })).toBeVisible();
  expect(screen.getByRole("link", { name: /طفل معتمد/ })).toBeVisible();
});

it("serializes slow job polling and stops it once the batch is complete", async () => {
  vi.useFakeTimers();
  try {
    let finishStatus!: (response: Response) => void;
    const pending = new Promise<Response>(resolve => { finishStatus = resolve; });
    const fetcher = vi.fn(async url => {
      const path = String(url);
      if (path.endsWith("/jobs")) return ok([{ ...job, status: "Running" }]);
      if (path.endsWith("/jobs/job-1")) return pending;
      return ok([]);
    });
    global.fetch = fetcher;
    render(<AcademyImport />);
    await act(async () => {});
    const statusReads = () => fetcher.mock.calls.filter(([url]) => String(url).endsWith("/jobs/job-1"));
    await act(async () => { await vi.advanceTimersByTimeAsync(2_000); });
    expect(statusReads()).toHaveLength(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(6_000); });
    expect(statusReads()).toHaveLength(1);
    await act(async () => { finishStatus(ok(job)); });
    expect(screen.getByRole("heading", { name: "اكتمل الاستيراد" })).toBeVisible();
    await act(async () => { await vi.advanceTimersByTimeAsync(6_000); });
    expect(statusReads()).toHaveLength(1);
  } finally {
    vi.useRealTimers();
  }
});

it("requires an explicit candidate for each account conflict before approval", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok({
    ...report, financiallyIncomplete: 0,
    guardianMatches: [{ householdKey: "family", guardianName: "ولي اصطناعي", phone: "01000000001", candidates: ["candidate"] }],
  }));
  render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  const approved = await screen.findByRole("checkbox", { name: /راجعت الأسماء والهواتف/ });
  fireEvent.click(approved);
  const submit = screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" });
  expect(submit).toBeDisabled();
  fireEvent.change(screen.getByRole("combobox", { name: /مراجعة رابط ولي اصطناعي/ }), { target: { value: "candidate" } });
  expect(submit).toBeEnabled();
  expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url).endsWith("/commit"))).toBe(false);
});
it("aborts pending approval reads when the platform changes academy", async () => {
  let finish!: (response: Response) => void;
  let signal: AbortSignal | undefined;
  const pending = new Promise<Response>(resolve => { finish = resolve; });
  global.fetch = vi.fn(async (url, request) => {
    const path = String(url);
    if (path.endsWith("/csrf")) return ok({ token: "token" });
    if (path.endsWith("/jobs") || path.endsWith("/schema")) return ok([]);
    if (path.includes("/by-request")) {
      signal = request?.signal ?? undefined;
      return pending;
    }
    if (path.endsWith("/commit")) throw new Error("network");
    return ok({ ...report, financiallyIncomplete: 0 });
  });
  const view = render(<AcademyImport academyId="academy-a" />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "academy-a.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  fireEvent.click(await screen.findByRole("checkbox", { name: /راجعت الأسماء والهواتف/ }));
  fireEvent.click(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" }));
  fireEvent.click(await screen.findByRole("button", { name: "التحقق من حالة الاعتماد" }));
  expect(signal?.aborted).toBe(false);
  view.rerender(<AcademyImport academyId="academy-b" />);
  expect(signal?.aborted).toBe(true);
  await act(async () => { finish(ok(job)); });
  expect(screen.queryByText("academy-a.xlsx")).not.toBeInTheDocument();
  expect(screen.queryByRole("heading", { name: "اكتمل الاستيراد" })).not.toBeInTheDocument();
});
it("restores the durable completed job after closing the page without another approval POST", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/records") ? ok(records) : ok([job])); render(<AcademyImport />); await screen.findByRole("heading", { name: "اكتمل الاستيراد" }); await waitFor(() => expect(screen.getByRole("link", { name: /طفل معتمد/ })).toBeVisible()); expect(vi.mocked(fetch).mock.calls.some(c => c[1]?.method === "POST")).toBe(false);
});
it("opens the last imported result first and separates a new upload without repeating a publish",async()=>{
  global.fetch=vi.fn(async url=>String(url).endsWith("/records")?ok(records):ok([job]));
  render(<AcademyImport/>);
  await screen.findByRole("heading",{name:"اكتمل الاستيراد"});
  expect(screen.queryByLabelText("ملف بيانات الأكاديمية")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button",{name:"استيراد ملف جديد"}));
  expect(screen.getByLabelText("ملف بيانات الأكاديمية")).toBeInTheDocument();
  expect(screen.queryByRole("heading",{name:"اكتمل الاستيراد"})).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button",{name:"العودة إلى نتيجة الدفعة"}));
  expect(screen.getByRole("heading",{name:"اكتمل الاستيراد"})).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.some(c=>c[1]?.method==="POST")).toBe(false);
});
it("exposes real connected blank/sample downloads and reports coach/tournament relations from the API",async()=>{
 global.fetch=vi.fn(async url=>String(url).endsWith('/schema')?ok({maxRows:1000,maxFileBytes:5242880,connectedSheets:[{name:'دليل الاستخدام',optional:true,columns:[]},{name:'المدربون',optional:false,columns:[{key:'Code',label:'كود المدرب',required:true}]},{name:'البطولات',optional:true,columns:[{key:'Code',label:'كود البطولة',required:true}]}]}):String(url).endsWith('/csrf')?ok({token:'token'}):String(url).endsWith('/jobs')?ok([]):ok({...report,financiallyIncomplete:0,coachAssignments:2,tournamentParticipations:17,effects:[{sheet:'المدربون',create:2,reuse:0},{sheet:'البطولات',create:2,reuse:0}]}));
 render(<AcademyImport/>);
 expect(screen.getByRole('button',{name:/تنزيل قالب فارغ/})).toBeEnabled();
 expect(screen.getByRole('button',{name:/تنزيل نموذج مكتمل/})).toBeEnabled();
 await screen.findByText('الحقول المطلوبة والاختيارية');
 fireEvent.change(screen.getByLabelText('ملف بيانات الأكاديمية'),{target:{files:[new File(['synthetic'],'linked.xlsx')]}}); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"}));
 await screen.findByRole('heading',{name:'اكتملت المعاينة دون أخطاء'});
 const effects=screen.getByText('الإضافة والربط من الملف').closest('details')!;
 expect(effects).not.toHaveAttribute('open');
 expect(effects).toHaveTextContent('17');
 expect(effects).toHaveTextContent('مشاركة في البطولات');
 expect(effects).toHaveTextContent('إسناد مدرب لمجموعة');
 expect(screen.getByRole('button',{name:'اعتماد وبدء الاستيراد'})).toBeDisabled();
 expect(vi.mocked(fetch).mock.calls.some(c=>c[1]?.method==='POST'&&String(c[0]).endsWith('/commit'))).toBe(false);
});

it("opens a saved batch without displaying late records from the previous selection", async () => {
  const second = { ...job, id: "job-2", sourceSystem: "دفعة ثانية", rowCount: 2, publishedRows: 2 };
  let finish!: (response: Response) => void;
  const pending = new Promise<Response>(resolve => { finish = resolve; });
  global.fetch = vi.fn(async url => {
    const path = String(url);
    if (path.endsWith("/jobs")) return ok([job, second]);
    if (path.endsWith("/job-1/records")) return pending;
    if (path.endsWith("/job-2/records")) return ok([{ ...records[0], player: "لاعب الدفعة الثانية" }]);
    return ok([]);
  });
  render(<AcademyImport />);
  await screen.findByText("جارٍ تحميل التسجيلات المحفوظة…");
  expect(screen.queryByText("لا توجد تسجيلات محفوظة لهذه الدفعة.")).not.toBeInTheDocument();
  const history = screen.getByText("دفعات الاستيراد السابقة").closest("details")!;
  history.open = true;
  fireEvent.click(screen.getByRole("button", { name: /دفعة ثانية/ }));
  await screen.findByRole("link", { name: /لاعب الدفعة الثانية/ });
  await act(async () => { finish(ok(records)); });
  expect(screen.getByText("التسجيلات الجديدة 2")).toBeVisible();
  expect(screen.queryByText("طفل معتمد")).not.toBeInTheDocument();
  expect(screen.getByRole("region", { name: "نتيجة دفعة الاستيراد" })).toHaveFocus();
  expect(vi.mocked(fetch).mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
});

it("recovers history after 503 and omits a history disclosure for an empty list", async () => {
  let reads = 0;
  global.fetch = vi.fn(async url => {
    if (String(url).endsWith("/jobs")) return ++reads === 1 ? ok({}, 503) : ok([job]);
    if (String(url).endsWith("/records")) return ok([]);
    return ok([]);
  });
  const { unmount } = render(<AcademyImport />);
  fireEvent.click(await screen.findByRole("button", { name: "إعادة تحميل سجل الدفعات" }));
  await screen.findByText("التسجيلات الجديدة 1");
  expect(await screen.findByText("لا توجد تسجيلات محفوظة لهذه الدفعة.")).toBeVisible();
  expect(reads).toBe(2);
  unmount();
  global.fetch = vi.fn(async () => ok([]));
  render(<AcademyImport />);
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
  expect(screen.queryByText("دفعات الاستيراد السابقة")).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "استيراد ملف" })).toBeEnabled();
});

it("keeps confirmed zero distinct from missing counts and payment totals", async () => {
  const malformed = { ...job, publishedRows: "1", replayedRows: -1,
    result: { historicalPayments: 2, effects: [null, { sheet: "الفروع", create: "3", reuse: 0 }] } };
  const zeros = { ...job, id: "job-2", sourceSystem: "دفعة صفرية", publishedRows: 0, replayedRows: 1,
    result: { effects: [{ sheet: "الفروع", create: 0, reuse: 0 }] } };
  global.fetch = vi.fn(async url => String(url).endsWith("/jobs") ? ok([malformed, zeros]) : ok([]));
  render(<AcademyImport />);
  expect(await screen.findByText("عدد التسجيلات الجديدة غير متاح")).toBeVisible();
  expect(screen.getByText(/إجمالي التحصيل السابق غير متاح/)).toBeVisible();
  expect(screen.queryByText("تفاصيل الاستيراد")).not.toBeInTheDocument();
  screen.getByText("دفعات الاستيراد السابقة").closest("details")!.open = true;
  fireEvent.click(screen.getByRole("button", { name: /دفعة صفرية/ }));
  expect(screen.getByText("التسجيلات الجديدة 0")).toBeVisible();
  expect(screen.getByText("تسجيلات سبق استيرادها 1")).toBeVisible();
  expect(screen.queryByText("تفاصيل الاستيراد")).not.toBeInTheDocument();
});
it("uses the same import UI with explicit platform academy routes and platform CSRF",async()=>{
 global.fetch=vi.fn(async url=>String(url).endsWith('/csrf')?ok({token:'platform-token'}):String(url).endsWith('/jobs')?ok([]):ok(report));
 render(<AcademyImport academyId="academy-a"/>);
 expect(screen.getByRole('button',{name:/تنزيل قالب فارغ/})).toBeEnabled();
 fireEvent.change(screen.getByLabelText('ملف بيانات الأكاديمية'),{target:{files:[new File(['synthetic'],'linked.xlsx')]}}); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"}));
 await screen.findByRole('heading',{name:'اكتملت المعاينة دون أخطاء'});
 expect(vi.mocked(fetch).mock.calls.some(c=>c[0]==='/api/v1/platform/auth/csrf')).toBe(true);
 expect(vi.mocked(fetch).mock.calls.some(c=>c[0]==='/api/v1/auth/csrf')).toBe(false);
 expect(vi.mocked(fetch).mock.calls.find(c=>String(c[0]).endsWith('/preview'))?.[0]).toBe('/api/v1/platform/academies/academy-a/import/preview');
});

it("keeps the setup stable when job history arrives after the user starts reviewing the template", async () => {
  let finish!: (response: Response) => void;
  global.fetch = vi.fn(async url => String(url).endsWith("/jobs") ? new Promise<Response>(resolve => { finish = resolve; }) : ok([]));
  render(<AcademyImport />);
  fireEvent.focus(screen.getByRole("button", {name: /تنزيل قالب فارغ/}));
  finish(ok([job]));
  await waitFor(() => expect(fetch).toHaveBeenCalled());
  expect(screen.getByLabelText("ملف بيانات الأكاديمية")).toBeInTheDocument();
  expect(screen.queryByRole("heading", {name:"اكتمل الاستيراد"})).not.toBeInTheDocument();
});

it("shows the selected filename and waits for explicit continue before uploading",async()=>{
 global.fetch=vi.fn(async()=>ok([]));render(<AcademyImport academyId="academy-a"/>);
 fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"),{target:{files:[new File(["x"],"academy-50.xlsx")]}});
 expect(screen.getByRole("region",{name:"الملف المختار"})).toHaveTextContent("academy-50.xlsx");expect(screen.getByRole("button",{name:"متابعة لفحص الملف"})).toBeEnabled();expect(vi.mocked(fetch).mock.calls.some(c=>c[1]?.method==="POST")).toBe(false);
 expect(screen.queryByText(/تمرين 300/)).not.toBeInTheDocument();expect(screen.queryByText("تنزيل القالب السابق")).not.toBeInTheDocument();
});
it("download errors are visible Arabic and never saved as fake Excel",async()=>{
 global.fetch=vi.fn(async url=>String(url).endsWith("/connected-template")?ok({title:"راجع إعدادات الأكاديمية"},403):ok([]));render(<AcademyImport academyId="academy-a"/>);fireEvent.click(screen.getByRole("button",{name:/تنزيل قالب فارغ/}));await screen.findByText("راجع إعدادات الأكاديمية");expect(vi.mocked(fetch).mock.calls.some(c=>c[0]==="/api/v1/platform/academies/academy-a/import/connected-template")).toBe(true);
});

it("refreshes academy readiness and member counts once when an import completes",async()=>{const complete=vi.fn();global.fetch=vi.fn(async url=>String(url).endsWith("/jobs")?ok([job]):String(url).endsWith("/records")?ok(records):ok([]));const view=render(<AcademyImport onCompleted={complete}/>);await screen.findByRole("heading",{name:"اكتمل الاستيراد"});await waitFor(()=>expect(complete).toHaveBeenCalledTimes(1));view.rerender(<AcademyImport onCompleted={complete}/>);expect(complete).toHaveBeenCalledTimes(1);});

it("prepares draft operation before a read-only preview and requires review of existing accounts",async()=>{
 global.fetch=vi.fn(async url=>String(url).endsWith('/csrf')?ok({token:'platform-token'}):String(url).endsWith('/jobs')?ok([]):ok({...report,financiallyIncomplete:0,accountLinks:[{role:'مدرب',name:'مدرب خيالي',phone:'+201000000123'}]}));
 render(<AcademyImport academyId="academy-a"/>);
 fireEvent.change(screen.getByLabelText('ملف بيانات الأكاديمية'),{target:{files:[new File(['synthetic'],'swimming.xlsx')]}});fireEvent.click(screen.getByRole('button',{name:'متابعة لفحص الملف'}));
 await screen.findByRole('heading',{name:'اكتملت المعاينة دون أخطاء'});
 const posts=vi.mocked(fetch).mock.calls.filter(c=>c[1]?.method==='POST');expect(posts.map(c=>c[0])).toEqual(['/api/v1/platform/academies/academy-a/prepare-operation','/api/v1/platform/academies/academy-a/import/preview']);
 expect(screen.getByText('مراجعة ربط 1 حسابًا قائمًا')).toBeVisible();expect(screen.getByText(/مدرب خيالي/)).toBeVisible();
 expect(screen.getByRole('button',{name:'اعتماد وبدء الاستيراد'})).toBeDisabled();fireEvent.click(screen.getByRole('checkbox',{name:/راجعت الأسماء والهواتف/}));expect(screen.getByRole('button',{name:'اعتماد وبدء الاستيراد'})).toBeEnabled();
});

it.each([
  ["/csrf", 401, "انتهت جلسة الدخول"],
  ["/prepare-operation", 401, "انتهت جلسة الدخول"],
  ["/prepare-operation", 403, "لا تملك صلاحية معاينة الاستيراد"],
  ["/prepare-operation", 503, "تعذر تجهيز الأكاديمية للاستيراد"],
])("blocks preview after an empty or non-JSON failure at %s (HTTP %s)", async (route, status, message) => {
  global.fetch = vi.fn(async url => {
    const path = String(url);
    if (path.endsWith(route)) return new Response(null, { status });
    if (path.endsWith("/csrf")) return ok({ token: "platform-token" });
    return ok([]);
  });
  render(<AcademyImport academyId="academy-a" />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"),
    { target: { files: [new File(["synthetic"], "fictional.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  expect(await screen.findByText(new RegExp(message))).toBeVisible();
  const login = screen.queryByRole("link", { name: "تسجيل الدخول في نافذة أخرى" });
  if (status === 401) {
    expect(login).toHaveAttribute("href", "/platform/login");
    expect(login).toHaveAttribute("target", "_blank");
  } else expect(login).not.toBeInTheDocument();
  expect(screen.getByRole("region", { name: "الملف المختار" })).toHaveTextContent("fictional.xlsx");
  expect(vi.mocked(fetch).mock.calls.some(([url]) => /\/(preview|commit)$/.test(String(url)))).toBe(false);
});

it("keeps the chosen workbook but requires fresh preview after stale approval", async () => {
  global.fetch = vi.fn(async url => {
    const path = String(url);
    if (path.endsWith("/csrf")) return ok({ token: "platform-token" });
    if (path.endsWith("/preview")) return ok({ ...report, financiallyIncomplete: 0 });
    if (path.endsWith("/commit")) return ok({ code: "stale", title: "تغيّرت المراجع. أعد المعاينة قبل الاعتماد." }, 409);
    return ok([]);
  });
  render(<AcademyImport academyId="academy-a" />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"),
    { target: { files: [new File(["synthetic"], "fictional.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" });
  fireEvent.click(screen.getByRole("checkbox", { name: /راجعت الأسماء والهواتف/ }));
  fireEvent.click(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" }));
  expect(await screen.findByText(/تغيّرت المراجع/)).toBeVisible();
  expect(screen.queryByRole("button", { name: "اعتماد وبدء الاستيراد" })).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "متابعة لفحص الملف" })).toBeEnabled();
  expect(screen.getByRole("region", { name: "الملف المختار" })).toHaveTextContent("fictional.xlsx");
});

it("blocks a workbook larger than the academy's published limit before sending it", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/schema")
    ? ok({ maxRows: 50, maxFileBytes: 2, connectedSheets: [] })
    : ok([]));
  render(<AcademyImport academyId="academy-a" />);
  await screen.findByText(/حتى 50 تسجيل/);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"),
    { target: { files: [new File(["long"], "fictional.xlsx")] } });
  fireEvent.click(screen.getByRole("button", { name: "متابعة لفحص الملف" }));
  expect(await screen.findByText(/حجم الملف أكبر من الحد المسموح/)).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.some(([, options]) => options?.method === "POST")).toBe(false);
});
