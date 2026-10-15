import {act,cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {PayrollAccount} from "./payroll-account";
const item={id:"current",month:"2026-10-01",approvedAtUtc:"2026-10-15T10:00:00Z",netDue:3600,paid:2500,remaining:1100,payments:[{id:"p",amount:2500,createdAtUtc:"2026-10-15T10:01:00Z"}]};
const value={employeeName:"موظف عرض",currency:"EGP",asOf:"2026-10-15",cutoffUtc:"2026-10-15T10:02:00Z",timeZone:"Africa/Cairo",totalDue:6600,totalPaid:3500,remaining:3100,priorRemaining:2000,currentRemaining:1100,currentMonthApproved:true,legacyPaidMonthsExcluded:1,items:[item,{...item,id:"prior",month:"2026-09-01",netDue:3000,paid:1000,remaining:2000,payments:[]}]};
const reply=(v:unknown,status=200)=>Promise.resolve(new Response(JSON.stringify(v),{status}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it("includes a reviewed opening exactly once in totals and links to its source, not a fake salary month",async()=>{
  global.fetch=vi.fn(()=>reply({...value,totalDue:7800,remaining:4300,opening:{asOf:"2026-09-01",amount:1200,paid:0,remaining:1200,payments:[]}})) as typeof fetch;render(<PayrollAccount id="worker"/>);await screen.findByLabelText("مجاميع الحساب");expect(screen.getByLabelText("مجاميع الحساب")).toHaveTextContent("7,800");expect(screen.getByText(/داخل الإجمالي بالفعل/)).toHaveTextContent("1,200");expect(screen.queryByText(/ليست رصيدًا افتتاحيًا موثقًا/)).not.toBeInTheDocument();expect(screen.getByRole("link",{name:"مراجعة الافتتاح والصرف"})).toHaveAttribute("href","/dashboard/payroll/worker/opening");expect(screen.getAllByRole("link",{name:"كشف الشهر والصرف"})).toHaveLength(2);
});
it("distinguishes allocated advances from actual cash without netting unallocated balance automatically",async()=>{
  global.fetch=vi.fn(()=>reply({...value,recordedAdvanceTotal:750,unallocatedAdvances:250,advanceApplied:500,directPaid:3000,cashOutRecorded:3750})) as typeof fetch;render(<PayrollAccount id="worker"/>);await screen.findByLabelText("مجاميع الحساب");expect(screen.getByText(/سلف سبق صرفها/)).toHaveTextContent("750");expect(screen.getByText(/غير المخصص ·/)).toHaveTextContent("250");expect(screen.getByText(/خروج المال المسجل ·/)).toHaveTextContent("3,750");expect(screen.getByLabelText("مجاميع الحساب")).toHaveTextContent("3,100");expect(screen.getByRole("link",{name:"تفاصيل السلف وتخصيصها"})).toHaveAttribute("href","/dashboard/payroll/worker/advances");
});
it("today is shown from the academy clock and refreshing unchanged filters fetches the new payout",async()=>{
  let refreshed=false;global.fetch=vi.fn(()=>reply(refreshed?{...value,totalPaid:5500,remaining:1100,priorRemaining:0,items:[item,{...value.items[1],paid:3000,remaining:0}]}:value)) as typeof fetch;render(<PayrollAccount id="worker"/>);await screen.findByLabelText("مجاميع الحساب");expect(screen.getByLabelText("حتى تاريخ")).toHaveValue("2026-10-15");refreshed=true;fireEvent.click(screen.getByRole("button",{name:"حتى اليوم"}));await waitFor(()=>expect(screen.getByLabelText("مجاميع الحساب")).toHaveTextContent("1,100"));expect(fetch).toHaveBeenCalledTimes(2);
});
it("ties totals, prior/current drilldowns and CSV to the server cutoff, with no duplicate payment action",async()=>{
  global.fetch=vi.fn(()=>reply(value)) as typeof fetch;render(<PayrollAccount id="worker"/>);
  expect(await screen.findByLabelText("مجاميع الحساب")).toHaveTextContent(/6,600.*3,500.*3,100/);
  expect(screen.getByText(/الأشهر السابقة ·/)).toHaveTextContent("2,000");expect(screen.getByText(/الشهر المختار ·/)).toHaveTextContent("1,100");
  expect(screen.getAllByRole("link",{name:"كشف الشهر والصرف"}).map(a=>a.getAttribute("href"))).toEqual(["/dashboard/payroll/worker/statement?month=2026-10-01","/dashboard/payroll/worker/statement?month=2026-09-01"]);
  expect(screen.getByRole("link",{name:"تنزيل نفس الكشف CSV"})).toHaveAttribute("href","/api/v1/manage/payroll/worker/account/export?asOf=2026-10-15&cutoffUtc=2026-10-15T10%3A02%3A00Z");
  expect(screen.getByText(/ليست رصيدًا افتتاحيًا موثقًا/)).toBeVisible();expect(screen.getByText(/مسدد بالمسار السابق مستبعد/)).toBeVisible();expect(screen.queryByRole("button",{name:/صرف|اعتماد/})).not.toBeInTheDocument();expect(vi.mocked(fetch).mock.calls.every(([,o])=>!o?.method)).toBe(true);
});
it("filters by date, never treats an empty approved list as zero wages, and returns to actual today",async()=>{
  global.fetch=vi.fn(input=>reply(String(input).includes("asOf=")?{...value,asOf:"2026-09-30",items:[],currentMonthApproved:false,totalDue:0,totalPaid:0,remaining:0}:value)) as typeof fetch;
  render(<PayrollAccount id="worker"/>);await screen.findByLabelText("مجاميع الحساب");fireEvent.change(screen.getByLabelText("حتى تاريخ"),{target:{value:"2026-09-30"}});fireEvent.click(screen.getByRole("button",{name:/^عرض$/}));await screen.findByText(/لا مصادر معتمدة حتى هذا التاريخ/);expect(screen.getByText(/غيابه لا يعني/)).toBeVisible();fireEvent.click(screen.getByRole("button",{name:"حتى اليوم"}));await screen.findByLabelText("أشهر حساب الموظف");expect(vi.mocked(fetch).mock.calls.at(-1)?.[0]).toBe("/api/v1/manage/payroll/worker/account");
});
it("hides stale employee totals and the export on permission or date failure",async()=>{
  global.fetch=vi.fn(()=>reply({title:"no"},403)) as typeof fetch;render(<PayrollAccount id="worker"/>);await screen.findByText(/تعذر تحميل كشف الحساب/);expect(screen.queryByLabelText("مجاميع الحساب")).not.toBeInTheDocument();expect(screen.queryByRole("link",{name:/CSV/})).not.toBeInTheDocument();
});
it("a slower historical response cannot overwrite the newly selected date",async()=>{
  let finish!:(r:Response)=>void;global.fetch=vi.fn(input=>String(input).includes("2026-09-30")?new Promise<Response>(resolve=>{finish=resolve;}):reply(value)) as typeof fetch;
  render(<PayrollAccount id="worker"/>);await screen.findByLabelText("مجاميع الحساب");fireEvent.change(screen.getByLabelText("حتى تاريخ"),{target:{value:"2026-09-30"}});fireEvent.click(screen.getByRole("button",{name:/^عرض$/}));await waitFor(()=>expect(finish).toBeTypeOf("function"));fireEvent.click(screen.getByRole("button",{name:"حتى اليوم"}));await screen.findByLabelText("مجاميع الحساب");await act(async()=>finish(new Response(JSON.stringify({...value,totalDue:1}))));expect(screen.getByLabelText("مجاميع الحساب")).toHaveTextContent("6,600");
});
