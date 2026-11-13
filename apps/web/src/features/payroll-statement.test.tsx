import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,beforeEach,expect,it,vi} from "vitest";
import {PayrollStatement} from "./payroll-statement";
import {useDashboardPermission} from "../components/dashboard-shell";
vi.mock("../components/dashboard-shell", async importOriginal => ({
  ...(await importOriginal<typeof import("../components/dashboard-shell")>()),
  useDashboardPermission: vi.fn(),
}));
const row={employeeId:"worker",name:"موظف عرض اصطناعي",currency:"EGP",baseDue:3000,deductions:0,additions:600,privateCompensation:0,netDue:3600,paidAmount:0,remainingAmount:3600,isPaid:false,salaryBasis:"ApprovedVersion"};
const scope = "a".repeat(64);
const initial={recoveryScope:scope,month:"2026-10-01",row,statement:null,quoteHash:"hash",canApprove:true,canPay:false,payments:[],sources:{adjustments:[],attendance:[],privateSources:[]}};
const reply=(value:unknown,status=200)=>Promise.resolve(new Response(JSON.stringify(value),{status}));
beforeEach(()=>{Object.defineProperty(HTMLDialogElement.prototype,"showModal",{configurable:true,value:function(this:HTMLDialogElement){this.setAttribute("open","");}});Object.defineProperty(HTMLDialogElement.prototype,"close",{configurable:true,value:function(this:HTMLDialogElement){this.removeAttribute("open");}});});
afterEach(()=>{cleanup();vi.restoreAllMocks();sessionStorage.clear();});
it("keeps the stored attendance deduction readable and links the source only with attendance access", async () => {
  const permission = vi.mocked(useDashboardPermission);
  permission.mockReturnValue(false);
  global.fetch = vi.fn(() => reply({
    ...initial,
    row: { ...row, coachMembershipId: "coach", deductions: 75.25 },
    sources: { ...initial.sources, attendance: [
      { Id: "first", WorkDate: "2026-10-10", ApprovedDeduction: 50, DeductionReason: "خصم معتمد" },
      { Id: "historical", WorkDate: "2026-10-12", ApprovedDeduction: 25.25 },
    ] },
  })) as typeof fetch;
  const view = render(<PayrollStatement id="worker" month="2026-10-01" />);
  expect(await screen.findByText("خصم معتمد")).toBeVisible();
  expect(screen.getByText("25.25 جنيه")).toBeVisible();
  expect(screen.queryByRole("link", { name: /خصم دوام/ })).not.toBeInTheDocument();
  permission.mockReturnValue(true);
  view.rerender(<PayrollStatement id="worker" month="2026-10-01" />);
  const links = await screen.findAllByRole("link", { name: /خصم دوام/ });
  expect(links.map(link => link.getAttribute("href"))).toEqual([
    "/dashboard/attendance/staff/coach?date=2026-10-10",
    "/dashboard/attendance/staff/coach?date=2026-10-12",
  ]);
  permission.mockReset();
});
it("keeps the deduction readable when the daily attendance source is forbidden", async () => {
  const permission = vi.mocked(useDashboardPermission);
  permission.mockReturnValue(true);
  const source = "/api/v1/manage/staff-attendance/coach?date=2026-10-10";
  global.fetch = vi.fn(url => String(url) === source ? reply({}, 403) : reply({
    ...initial,
    row: { ...row, coachMembershipId: "coach", deductions: 75.25 },
    sources: { ...initial.sources, attendance: [
      { Id: "daily", WorkDate: "2026-10-10", ApprovedDeduction: 75.25, DeductionReason: "خصم معتمد" },
    ] },
  })) as typeof fetch;
  render(<PayrollStatement id="worker" month="2026-10-01" />);
  expect(await screen.findByText("خصم معتمد")).toBeVisible();
  await waitFor(() => expect(fetch).toHaveBeenCalledWith(source, expect.objectContaining({ cache: "no-store" })));
  expect(screen.queryByRole("link", { name: /خصم دوام/ })).not.toBeInTheDocument();
  permission.mockReset();
});
it("warns about unallocated advances before more cash and never labels allocation as new cash",async()=>{
  global.fetch=vi.fn(()=>reply({...initial,row:{...row,paidAmount:500,remainingAmount:3100},statement:{id:"statement"},canApprove:false,canPay:true,unallocatedAdvances:250,payments:[{id:"a",sequence:1,amount:500,remainingAfter:3100,kind:"AdvanceApplication"}]})) as typeof fetch;render(<PayrollStatement id="worker" month="2026-10-01"/>);expect(await screen.findByRole("note")).toHaveTextContent("250");expect(screen.getByRole("link",{name:"السلف قبل صرف مبلغ جديد"})).toHaveAttribute("href","/dashboard/payroll/worker/advances");expect(screen.getByText(/تخصيص سلفة سابقة — ليس صرفًا جديدًا/)).toBeVisible();expect(screen.getByLabelText("مبلغ الصرف")).toHaveAttribute("max","3100");
});
it("approval needs confirmation, keeps quote and does not pretend a payout or verified opening",async()=>{
  let approved=false;global.fetch=vi.fn((url,init)=>String(url).includes("csrf")?reply({token:"csrf"}):init?.method==="POST"?(approved=true,reply({id:"statement"},201)):reply(approved?{...initial,statement:{id:"statement"},canApprove:false}:initial)) as typeof fetch;
  render(<PayrollStatement id="worker" month="2026-10-01"/>);fireEvent.click(await screen.findByRole("button",{name:"مراجعة اعتماد الكشف"}));expect(vi.mocked(fetch).mock.calls.some(([,o])=>o?.method==="POST")).toBe(false);expect(screen.getByRole("dialog")).toHaveTextContent("الاعتماد ليس صرفًا");fireEvent.click(screen.getByRole("button",{name:"تأكيد اعتماد الكشف"}));await screen.findByText("تم اعتماد الكشف دون تسجيل صرف.");
  const write=vi.mocked(fetch).mock.calls.find(([,o])=>o?.method==="POST");expect(JSON.parse(String(write?.[1]?.body))).toEqual({month:"2026-10-01",quoteHash:"hash",note:""});expect(write?.[1]?.headers).toHaveProperty("Idempotency-Key");expect(screen.getByText(/الافتتاح والسلف التاريخية غير المستوردة غير موثقة/)).toBeInTheDocument();expect(screen.queryByRole("form",{name:"اعتماد الكشف"})).not.toBeInTheDocument();
});
it("partial payout sends reviewed balances, is bounded and updates remaining without reducing earned",async()=>{
  let paid=false;global.fetch=vi.fn((url,init)=>String(url).includes("csrf")?reply({token:"csrf"}):init?.method==="POST"?(paid=true,reply({id:"pay",remainingAfter:1100},201)):reply({...initial,row:paid?{...row,paidAmount:2500,remainingAmount:1100}:row,statement:{id:"statement"},canApprove:false,canPay:true,payments:paid?[{id:"pay",sequence:1,amount:2500,remainingAfter:1100}]:[]})) as typeof fetch;
  render(<PayrollStatement id="worker" month="2026-10-01"/>);const amount=await screen.findByLabelText("مبلغ الصرف");expect(amount).toHaveAttribute("max","3600");expect(screen.getByLabelText("مرجع الصرف (اختياري)")).not.toBeRequired();fireEvent.change(amount,{target:{value:"2500"}});fireEvent.click(screen.getByRole("button",{name:"مراجعة الصرف"}));expect(screen.getByRole("dialog")).toHaveTextContent("لا ينفذ التطبيق تحويلًا بنكيًا");fireEvent.click(screen.getByRole("button",{name:"تأكيد تسجيل الصرف"}));await screen.findByText("تم تسجيل الصرف وتحديث المتبقي.");
  const write=vi.mocked(fetch).mock.calls.find(([,o])=>o?.method==="POST");expect(JSON.parse(String(write?.[1]?.body))).toEqual({month:"2026-10-01",amount:2500,expectedPaid:0,expectedRemaining:3600,reference:""});expect(screen.getByLabelText("مبالغ الكشف")).toHaveTextContent("3,600");expect(screen.getByLabelText("مبالغ الكشف")).toHaveTextContent("1,100");expect(screen.getByLabelText("مبلغ الصرف")).toHaveAttribute("max","1100");
});
it("readers do not get approval/payment controls and stale failures never become successful payouts",async()=>{
  global.fetch=vi.fn(()=>reply({...initial,canApprove:false})) as typeof fetch;const view=render(<PayrollStatement id="worker" month="2026-10-01"/>);await screen.findByText(/معاينة للمراجعة/);expect(screen.queryByRole("form")).not.toBeInTheDocument();view.unmount();
  global.fetch=vi.fn((url,init)=>String(url).includes("csrf")?reply({token:"csrf"}):init?.method==="POST"?reply({title:"تغير المتبقي؛ حدّث الكشف."},409):reply({...initial,statement:{id:"statement"},canApprove:false,canPay:true})) as typeof fetch;
  render(<PayrollStatement id="worker" month="2026-10-01"/>);fireEvent.change(await screen.findByLabelText("مبلغ الصرف"),{target:{value:"100"}});fireEvent.click(screen.getByRole("button",{name:"مراجعة الصرف"}));fireEvent.click(screen.getByRole("button",{name:"تأكيد تسجيل الصرف"}));await waitFor(()=>expect(screen.queryByRole("dialog")).not.toBeInTheDocument());expect(await screen.findByText("تغير المتبقي؛ حدّث الكشف.")).toBeVisible();expect(screen.queryByText("تم تسجيل الصرف وتحديث المتبقي.")).not.toBeInTheDocument();
});
it("reads the exact committed payout by request key after the response is lost",async()=>{
  let committed=false;
  const requestKey:string[]=[];
  global.fetch=vi.fn((url,init)=>{
    const path=String(url);
    if(path.includes("csrf"))return reply({token:"csrf"});
    if(path.includes("/by-request?")){
      expect(new URL(path,"http://local.test").searchParams.get("requestKey")).toBe(requestKey[0]);
      return reply({id:"payment"});
    }
    if(init?.method==="POST"){
      requestKey.push((init.headers as Record<string,string>)["Idempotency-Key"]);committed=true;
      return Promise.reject(new TypeError("offline"));
    }
    return reply({...initial,statement:{id:"statement"},canApprove:false,canPay:true,row:committed?{...row,paidAmount:2500,remainingAmount:1100}:row,payments:committed?[{id:"payment",amount:2500,remainingAfter:1100}]:[]});
  }) as typeof fetch;
  render(<PayrollStatement id="worker" month="2026-10-01"/>);
  fireEvent.change(await screen.findByLabelText("مبلغ الصرف"),{target:{value:"2500"}});
  fireEvent.click(screen.getByRole("button",{name:"مراجعة الصرف"}));
  fireEvent.click(screen.getByRole("button",{name:"تأكيد تسجيل الصرف"}));
  fireEvent.click(await screen.findByRole("button",{name:"التحقق من تسجيل الصرف"}));
  expect(await screen.findByText("تم تسجيل الصرف وتحديث المتبقي.")).toBeVisible();
  expect(screen.getByLabelText("مبالغ الكشف")).toHaveTextContent("1,100");
  expect(requestKey).toHaveLength(1);
});

it("hides the payroll statement if access is revoked after a committed write", async () => {
  let committed = false;
  global.fetch = vi.fn((url, init) => {
    if (String(url).includes("csrf")) return reply({ token: "csrf" });
    if (init?.method === "POST") {
      committed = true;
      return reply({ id: "payment" }, 201);
    }
    return committed ? reply({}, 403) : reply({ ...initial, statement: { id: "statement" }, canApprove: false, canPay: true });
  }) as typeof fetch;

  render(<PayrollStatement id="worker" month="2026-10-01" />);
  fireEvent.change(await screen.findByLabelText("مبلغ الصرف"), { target: { value: "100" } });
  fireEvent.click(screen.getByRole("button", { name: "مراجعة الصرف" }));
  fireEvent.click(screen.getByRole("button", { name: "تأكيد تسجيل الصرف" }));
  expect(await screen.findByText("هذا الكشف غير متاح لهذا الحساب.")).toBeVisible();
  expect(screen.queryByLabelText("مبالغ الكشف")).not.toBeInTheDocument();
  expect(screen.queryByLabelText("مبلغ الصرف")).not.toBeInTheDocument();
  expect(screen.queryByText("تم تسجيل الصرف وتحديث المتبقي.")).not.toBeInTheDocument();
});

it("restores the exact ambiguous payout after a reload without starting another payment", async () => {
  let committed = false;
  let requestKey = "";
  global.fetch = vi.fn((url, init) => {
    const path = String(url);
    if (path.includes("csrf")) return reply({ token: "csrf" });
    if (init?.method === "POST") {
      requestKey = (init.headers as Record<string, string>)["Idempotency-Key"];
      committed = true;
      return Promise.reject(new TypeError("offline"));
    }
    if (path.includes("/by-request?")) {
      expect(new URL(path, "http://local.test").searchParams.get("requestKey")).toBe(requestKey);
      return reply({ id: "payout" });
    }
    return reply({
      ...initial, statement: { id: "statement" }, canApprove: false, canPay: true,
      row: committed ? { ...row, paidAmount: 2500, remainingAmount: 1100 } : row,
      payments: committed ? [{ id: "payout", amount: 2500, remainingAfter: 1100 }] : [],
    });
  }) as typeof fetch;

  const view = render(<PayrollStatement id="worker" month="2026-10-01" />);
  fireEvent.change(await screen.findByLabelText("مبلغ الصرف"), { target: { value: "2500" } });
  fireEvent.click(screen.getByRole("button", { name: "مراجعة الصرف" }));
  fireEvent.click(screen.getByRole("button", { name: "تأكيد تسجيل الصرف" }));
  await screen.findByText(/تعذر تأكيد الحفظ/);
  view.unmount();
  render(<PayrollStatement id="worker" month="2026-10-01" />);
  await screen.findByText("محاولة صرف بانتظار التحقق");
  expect(screen.queryByLabelText("مبلغ الصرف")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "التحقق من تسجيل الصرف" }));
  expect(await screen.findByText("تم تسجيل الصرف وتحديث المتبقي.")).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method === "POST")).toHaveLength(1);
  expect(sessionStorage.length).toBe(0);
});

async function openPayment(amount: string, reference = "") {
  fireEvent.change(await screen.findByLabelText("مبلغ الصرف"), { target: { value: amount } });
  fireEvent.change(screen.getByLabelText("مرجع الصرف (اختياري)"), { target: { value: reference } });
  fireEvent.click(screen.getByRole("button", { name: "مراجعة الصرف" }));
  fireEvent.click(screen.getByRole("button", { name: "تأكيد تسجيل الصرف" }));
}

const readyToPay = { ...initial, statement: { id: "statement" }, canApprove: false, canPay: true };

it("does not move a pending attempt to another employee, month or authorized membership", async () => {
  let currentScope = scope;
  global.fetch = vi.fn((url, init) => {
    const path = String(url);
    if (path.includes("csrf")) return reply({ token: "csrf" });
    if (init?.method === "POST") return Promise.reject(new TypeError("offline"));
    const selected = new URL(path, "http://local.test");
    const month = selected.searchParams.get("month");
    const employeeId = selected.pathname.split("/")[5];
    return reply({ ...readyToPay, month, recoveryScope: currentScope, row: { ...row, employeeId } });
  }) as typeof fetch;

  const view = render(<PayrollStatement id="worker" month={initial.month} />);
  await openPayment("2500", "مرجع خاص بالمحاولة");
  await screen.findByText("محاولة صرف بانتظار التحقق");
  view.unmount();
  for (const target of [
    { id: "other-worker", month: initial.month, scope },
    { id: "worker", month: "2026-09-01", scope },
    { id: "worker", month: initial.month, scope: "b".repeat(64) },
  ]) {
    currentScope = target.scope;
    const other = render(<PayrollStatement id={target.id} month={target.month} />);
    expect(await screen.findByLabelText("مبلغ الصرف")).toHaveValue(null);
    expect(screen.queryByText("محاولة صرف بانتظار التحقق")).not.toBeInTheDocument();
    expect(screen.queryByText("مرجع خاص بالمحاولة")).not.toBeInTheDocument();
    other.unmount();
  }
  currentScope = scope;
  render(<PayrollStatement id="worker" month={initial.month} />);
  expect(await screen.findByText("محاولة صرف بانتظار التحقق")).toBeVisible();
  expect(screen.queryByText("مرجع خاص بالمحاولة")).not.toBeInTheDocument();
  expect(vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method === "POST")).toHaveLength(1);
  expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url).includes("/by-request?"))).toBe(false);
});

it("does not send a payment when the browser rejects storing its identity", async () => {
  global.fetch = vi.fn(() => reply(readyToPay)) as typeof fetch;
  const store = vi.spyOn(Storage.prototype, "setItem").mockImplementationOnce(() => {
    throw new DOMException("full", "QuotaExceededError");
  });
  render(<PayrollStatement id="worker" month={initial.month} />);
  await openPayment("2500");
  expect(await screen.findByText(/تعذر حفظ محاولة الرواتب/)).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method === "POST")).toHaveLength(0);
  expect(screen.queryByLabelText("مبلغ الصرف")).not.toBeInTheDocument();
  expect(screen.getByLabelText("مبالغ الكشف")).toBeVisible();
  store.mockRestore();
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل الكشف" }));
  expect(await screen.findByLabelText("مبلغ الصرف")).toBeVisible();
});

it("keeps a corrupt pending record read-only rather than silently starting a new payment", async () => {
  const key = `academy-payroll-attempt:${scope}:worker:${initial.month}`;
  sessionStorage.setItem(key, "{broken");
  global.fetch = vi.fn(() => reply(readyToPay)) as typeof fetch;
  render(<PayrollStatement id="worker" month={initial.month} />);
  expect(await screen.findByText(/تعذر حفظ محاولة الرواتب/)).toBeVisible();
  expect(screen.queryByLabelText("مبلغ الصرف")).not.toBeInTheDocument();
  expect(screen.getByLabelText("مبالغ الكشف")).toBeVisible();
  expect(sessionStorage.getItem(key)).toBe("{broken");
  expect(vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method === "POST")).toHaveLength(0);
});
