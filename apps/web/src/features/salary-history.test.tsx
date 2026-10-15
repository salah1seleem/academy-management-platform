import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { SalaryHistory } from "./salary-history";

const initial={id:"worker",name:"مدرب عرض اصطناعي",currency:"EGP",currentSalary:3000,hasVerifiedHistory:true,revision:1,canChange:true,currentMonth:"2026-10-01",currentMonthPaid:false,items:[{id:"v1",kind:"Initial",revision:1,effectiveFrom:"2026-10-01",monthlySalary:3000,approvedBy:"مالك العرض"}]};
const response=(value:unknown,status=200)=>Promise.resolve(new Response(JSON.stringify(value),{status}));
beforeEach(()=>{
  Object.defineProperty(HTMLDialogElement.prototype,"showModal",{configurable:true,value:function(this:HTMLDialogElement){this.setAttribute("open","");}});
  Object.defineProperty(HTMLDialogElement.prototype,"close",{configurable:true,value:function(this:HTMLDialogElement){this.removeAttribute("open");}});
});
afterEach(()=>{cleanup();vi.restoreAllMocks();});

it("reviews a dated source, submits trusted expected values and refreshes without creating a payout",async()=>{
  let saved=false;
  global.fetch=vi.fn((url,init)=>String(url).includes("csrf")?response({token:"csrf"}):init?.method==="POST"?(saved=true,response({id:"v2"},201)):response(saved?{...initial,revision:2,items:[{...initial.items[0],id:"v2",kind:"Change",revision:2,effectiveFrom:"2026-11-01",monthlySalary:3500},...initial.items]}:initial)) as typeof fetch;
  render(<SalaryHistory id="worker"/>);await screen.findByRole("form",{name:"تغيير الراتب"});
  fireEvent.change(screen.getByLabelText("شهر السريان"),{target:{value:"2026-11"}});fireEvent.change(screen.getByLabelText("الراتب الشهري الجديد"),{target:{value:"3500"}});
  expect(screen.getByLabelText("ملاحظة (اختياري)")).not.toBeRequired();fireEvent.click(screen.getByRole("button",{name:"مراجعة واعتماد التغيير"}));
  expect(vi.mocked(fetch).mock.calls.some(([,o])=>o?.method==="POST")).toBe(false);expect(screen.getByRole("dialog")).toHaveTextContent("لن يعاد حساب شهر سابق أو مسدد");
  fireEvent.click(screen.getByRole("button",{name:"تأكيد اعتماد الراتب"}));await screen.findByText("تم اعتماد التغيير المؤرخ دون تعديل راتب مسدد أو إنشاء صرف.");
  const writes=vi.mocked(fetch).mock.calls.filter(([,o])=>o?.method==="POST");expect(writes).toHaveLength(1);expect(writes[0][0]).toBe("/api/v1/manage/employees/worker/salary-versions");
  expect(JSON.parse(String(writes[0][1]?.body))).toEqual({effectiveFrom:"2026-11-01",monthlySalary:3500,expectedRevision:1,expectedCurrentSalary:3000,note:""});
  expect(writes[0][1]?.headers).toHaveProperty("Idempotency-Key");expect(screen.getByText(/الراتب الأول المعتمد/)).toBeVisible();expect(screen.getByText(/تغيير راتب معتمد/)).toBeVisible();
  expect(screen.getByLabelText("شهر السريان")).toHaveAttribute("min","2026-11");
});
it("paid months and already scheduled changes bound the next date without hiding history",async()=>{
  global.fetch=vi.fn(()=>response({...initial,currentMonthPaid:true,items:[{...initial.items[0],effectiveFrom:"2026-12-01"}]})) as typeof fetch;
  render(<SalaryHistory id="worker"/>);expect(await screen.findByLabelText("شهر السريان")).toHaveValue("2026-12");expect(screen.getByLabelText("شهر السريان")).toHaveAttribute("min","2026-12");expect(screen.getByText(/الشهر الحالي مسدد/)).toBeVisible();
});
it("legacy and read-only access never invent approvals or render mutation controls",async()=>{
  global.fetch=vi.fn(()=>response({...initial,canChange:false,hasVerifiedHistory:false,revision:0,items:[]})) as typeof fetch;
  render(<SalaryHistory id="worker"/>);await screen.findByText(/الراتب القائم مرجع غير مؤرخ/);expect(screen.queryByRole("form")).not.toBeInTheDocument();expect(screen.getByText(/القراءة وحدها لا تنشئ سجلًا/)).toBeVisible();expect(vi.mocked(fetch).mock.calls).toHaveLength(1);
});
it("a rejected stale review keeps history and retries use the same operation key",async()=>{
  global.fetch=vi.fn((url,init)=>String(url).includes("csrf")?response({token:"csrf"}):init?.method==="POST"?response({title:"تغير سجل الراتب؛ أعد المراجعة."},409):response(initial)) as typeof fetch;
  render(<SalaryHistory id="worker"/>);await screen.findByLabelText("شهر السريان");
  for(let n=0;n<2;n++){fireEvent.click(screen.getByRole("button",{name:"مراجعة واعتماد التغيير"}));fireEvent.click(screen.getByRole("button",{name:"تأكيد اعتماد الراتب"}));await waitFor(()=>expect(screen.queryByRole("dialog")).not.toBeInTheDocument());}
  const writes=vi.mocked(fetch).mock.calls.filter(([,o])=>o?.method==="POST");expect(writes).toHaveLength(2);expect(writes[0][1]?.headers).toEqual(writes[1][1]?.headers);expect(screen.getByText(/تغير سجل الراتب/)).toBeVisible();expect(screen.queryByText(/تم اعتماد التغيير المؤرخ/)).not.toBeInTheDocument();
});
