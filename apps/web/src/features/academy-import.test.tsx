import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { AcademyImport } from "./academy-import";
const report = { schemaVersion: "Academity.Import.2.1", fileHash: "file-hash", referenceStamp: "reference-stamp", sourceSystem: "synthetic", cutoffDate: "2026-09-28", rowCount: 1000, playerCount: 999, householdCount: 1, financiallyIncomplete: 1, canApprove: true, errors: [], rows: [{ row: 2, legacyPlayerId: "P1", legacyEnrollmentId: "E1", playerName: "طفل اصطناعي", guardianName: "ولي اصطناعي", guardianPhone: "01000000001", agreedNet: null, currency: "EGP", existingNew: "existing" }] };
const ok = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("starts with tenant template and import, previews without a create/approve/payment request", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok(report)); render(<AcademyImport />);
  expect(screen.getByRole("button", { name: /تنزيل قالب فارغ/ })).toBeEnabled();
  expect(screen.queryByText("تنزيل القالب السابق")).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["synthetic"], "test.xlsx")] } }); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"}));
  await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" }); expect(screen.getByText("1000")).toBeVisible(); expect(screen.getByText("غير معروف — مراجعة مالية")).toBeVisible();
  expect(screen.getByText(/معاينة وليست نتيجة استيراد معتمدة/)).toBeVisible();
  const call = vi.mocked(fetch).mock.calls.find(c => String(c[0]).endsWith("/preview")); expect(call?.[1]?.body).toBeInstanceOf(FormData); expect(call?.[1]?.headers).toEqual({ "X-CSRF-TOKEN": "token" });
  expect(vi.mocked(fetch).mock.calls.filter(c => c[1]?.method === "POST")).toHaveLength(1); expect(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toBeDisabled();
});
it("rejects wrong file types locally and never sends the contents", () => {
  global.fetch = vi.fn(async () => ok([])); render(<AcademyImport />); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsm")] } }); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"})); expect(screen.getByText(/اختر ملف xlsx صالحًا/)).toBeVisible(); expect(vi.mocked(fetch).mock.calls.filter(c => c[1]?.method === "POST")).toHaveLength(0);
});
it("shows actionable row errors and does not present failed data as a successful import", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : ok({ ...report, canApprove: false, rows: [], errors: [{ sheet: "Enrollments", row: 2, column: "GuardianPhone", message: "اكتب الهاتف كنص" }] })); render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } }); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"})); await screen.findByText("الملف يحتاج تصحيحًا"); expect(screen.getByText("اكتب الهاتف كنص")).toBeVisible(); expect(screen.getByRole("button", { name: /تنزيل تقرير الأخطاء/ })).toBeVisible(); expect(screen.queryByText("طفل اصطناعي")).not.toBeInTheDocument();
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
const records = [{ id: "player-1", legacyEnrollmentId: "E1", player: "طفل معتمد", playerCode: "IMP-1", sport: "سباحة", branch: "فرع صناعي", groupName: "مجموعة صناعية", priceSnapshot: null, currencySnapshot: "EGP" }];
it('identifies replayed historical receipts without describing them as new collection',async()=>{
 const replay={...job,publishedRows:0,replayedRows:1,result:{historicalPayments:1,historicalPaymentTotal:800}};
 global.fetch=vi.fn(async url=>String(url).endsWith('/records')?ok(records):String(url).endsWith('/jobs')?ok([replay]):ok([]));
 render(<AcademyImport/>);
 await screen.findByText(/لم يُنشأ قبض جديد عند إعادة الرفع/);
 expect(screen.getByText(/تم التحقق من/)).toBeVisible();
 expect(vi.mocked(fetch).mock.calls.some(c=>c[1]?.method==='POST')).toBe(false);
});
it("requires relationship and incomplete-finance review before explicit durable commit and shows real published links", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("/csrf") ? ok({ token: "token" }) : String(url).endsWith("/jobs") ? ok([]) : String(url).endsWith("/records") ? ok(records) : String(url).endsWith("/commit") ? ok(job, 202) : ok(report)); render(<AcademyImport />);
  const file = new File(["synthetic"], "test.xlsx"); fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [file] } }); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"})); await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" });
  fireEvent.click(screen.getByRole("checkbox", { name: /راجعت الأسماء والهواتف/ })); expect(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toBeDisabled(); fireEvent.click(screen.getByRole("checkbox", { name: /أقر أن/ })); fireEvent.click(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" }));
  await screen.findByRole("heading", { name: "اكتمل الاستيراد" }); expect(await screen.findByRole("link", { name: /طفل معتمد/ })).toHaveAttribute("href", "/dashboard/players/player-1");
  const commit = vi.mocked(fetch).mock.calls.find(c => String(c[0]).endsWith("/commit"))![1]!; const form = commit.body as FormData; expect(form.get("file")).toBe(file); expect(form.get("ExpectedFileHash")).toBe("file-hash"); expect(form.get("ExpectedReferenceStamp")).toBe("reference-stamp"); expect(form.get("GuardianLinksApproved")).toBe("true"); expect(form.get("AcceptFinancialReview")).toBe("true"); expect(commit.headers).toHaveProperty("Idempotency-Key"); expect(vi.mocked(fetch).mock.calls.some(c => /payment|collection|receipt/.test(String(c[0])))).toBe(false);
});
it("uncertain commit network response retries the same intent rather than creating a second import", async () => {
  let attempts = 0; global.fetch = vi.fn(async url => { if (String(url).endsWith("/csrf")) return ok({ token: "token" }); if (String(url).endsWith("/jobs")) return ok([]); if (String(url).endsWith("/records")) return ok(records); if (String(url).endsWith("/commit")) { if (++attempts === 1) throw new Error("network"); return ok(job); } return ok({ ...report, financiallyIncomplete: 0 }); }); render(<AcademyImport />);
  fireEvent.change(screen.getByLabelText("ملف بيانات الأكاديمية"), { target: { files: [new File(["x"], "test.xlsx")] } }); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"})); await screen.findByRole("heading", { name: "اكتملت المعاينة دون أخطاء" }); fireEvent.click(screen.getByRole("checkbox", { name: /راجعت الأسماء والهواتف/ })); fireEvent.click(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" })); await screen.findByText(/أعد المحاولة بنفس الصفحة/); fireEvent.click(screen.getByRole("button", { name: "اعتماد وبدء الاستيراد" })); await screen.findByRole("heading", { name: "اكتمل الاستيراد" });
  const calls = vi.mocked(fetch).mock.calls.filter(c => String(c[0]).endsWith("/commit")); expect(calls).toHaveLength(2); expect(calls[0][1]?.headers).toEqual(calls[1][1]?.headers);
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
  fireEvent.click(screen.getByRole("button",{name:"العودة إلى آخر دفعة"}));
  expect(screen.getByRole("heading",{name:"اكتمل الاستيراد"})).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.some(c=>c[1]?.method==="POST")).toBe(false);
});
it("exposes real connected blank/sample downloads and reports coach/tournament relations from the API",async()=>{
 global.fetch=vi.fn(async url=>String(url).endsWith('/schema')?ok({maxRows:1000,maxFileBytes:5242880,connectedSheets:[{name:'دليل الاستخدام',optional:true,columns:[]},{name:'المدربون',optional:false,columns:[{key:'Code',label:'كود المدرب',required:true}]},{name:'البطولات',optional:true,columns:[{key:'Code',label:'كود البطولة',required:true}]}]}):String(url).endsWith('/csrf')?ok({token:'token'}):String(url).endsWith('/jobs')?ok([]):ok({...report,financiallyIncomplete:0,coachAssignments:2,tournamentParticipations:17,effects:[{sheet:'المدربون',create:2,reuse:0},{sheet:'البطولات',create:2,reuse:0}]}));
 render(<AcademyImport/>);
 expect(screen.getByRole('button',{name:/تنزيل قالب فارغ/})).toBeEnabled();
 expect(screen.getByRole('button',{name:/تنزيل نموذج مكتمل/})).toBeEnabled();
 await screen.findByText('الأوراق والأكواد التي تربطها (3)');
 fireEvent.change(screen.getByLabelText('ملف بيانات الأكاديمية'),{target:{files:[new File(['synthetic'],'linked.xlsx')]}}); fireEvent.click(screen.getByRole("button",{name:"متابعة لفحص الملف"}));
 await screen.findByRole('heading',{name:'اكتملت المعاينة دون أخطاء'});
 const effects=screen.getByRole('region',{name:'العلاقات وأثر الاستيراد'});expect(effects).toHaveTextContent('17');expect(effects).toHaveTextContent('مشاركة في البطولات');expect(effects).toHaveTextContent('إسناد مدرب لمجموعة');
 expect(vi.mocked(fetch).mock.calls.some(c=>c[1]?.method==='POST'&&String(c[0]).endsWith('/commit'))).toBe(false);
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
