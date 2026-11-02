import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StaffWorkPeriodsPanel } from "./staff-work-periods";

const saved=vi.fn();
const data={version:41,today:"2026-10-16",workDate:"2026-10-15",timeZone:"Africa/Cairo",canEdit:true,hasPreservedPeriods:false,knownWorkedSeconds:0,totalWorkedSeconds:null,items:[{id:null,version:0,startDate:"2026-10-15",startTime:"08:00:00",endDate:"2026-10-15",endTime:"10:00:00",trustSource:"LegacyLocal",workedSeconds:null}]};
const response=(v:unknown,status=200)=>Promise.resolve(new Response(JSON.stringify(v),{status}));
afterEach(()=>{cleanup();vi.restoreAllMocks();saved.mockReset();});
function inputPeriod() {for(const [label,hour] of [["بداية الفترة","2"],["نهاية الفترة","4"]]){fireEvent.change(screen.getByLabelText(`${label} — الساعة`),{target:{value:hour}});fireEvent.change(screen.getByLabelText(`${label} — الدقيقة`),{target:{value:"00"}});fireEvent.change(screen.getByLabelText(`${label} — الفترة`),{target:{value:"pm"}});}}
it("preserves legacy uncertainty, sends the selected day/source version and calls reload only after a successful period write",async()=>{
  global.fetch=vi.fn((input,init)=>String(input).includes("csrf")?response({token:"test"}):init?.method==="POST"?response({version:42}):response(data)) as typeof fetch;
  render(<StaffWorkPeriodsPanel id="member" date={data.workDate} onSaved={saved}/>);expect(await screen.findByText("سجل قديم محلي — مدته غير مؤكدة")).toBeVisible();expect(screen.getByText(/الإجمالي غير مكتمل أو غير مؤكد/)).toBeVisible();
  fireEvent.click(screen.getByRole("button",{name:"إضافة فترة عمل"}));inputPeriod();expect(saved).not.toHaveBeenCalled();fireEvent.click(screen.getByRole("button",{name:"حفظ الفترة"}));await waitFor(()=>expect(saved).toHaveBeenCalledTimes(1));
  const write=vi.mocked(fetch).mock.calls.find(([,init])=>init?.method==="POST")!;expect(write[0]).toBe("/api/v1/manage/staff-attendance/member/periods");expect(JSON.parse(String(write[1]!.body))).toEqual({date:data.workDate,version:41,startTime:"14:00:00",endTime:"16:00:00",endDate:data.workDate,periodVersion:null,reason:null});expect(write[1]!.headers).toHaveProperty("Idempotency-Key");
});
it("targets only the selected period, requires a correction reason and stops on a stale source until reload",async()=>{
  global.fetch=vi.fn((input,init)=>String(input).includes("csrf")?response({token:"test"}):init?.method==="PUT"?response({message:"تغيرت الفترة"},409):response({...data,hasPreservedPeriods:true,items:[{...data.items[0],id:"p1",version:9}]})) as typeof fetch;
  render(<StaffWorkPeriodsPanel id="member" date={data.workDate} onSaved={saved}/>);fireEvent.click(await screen.findByRole("button",{name:"تصحيح / إغلاق الفترة 1"}));expect(screen.getByLabelText("سبب التصحيح (مطلوب)")).toBeRequired();fireEvent.change(screen.getByLabelText("سبب التصحيح (مطلوب)"),{target:{value:"تأكيد التوقيت الاصطناعي"}});fireEvent.click(screen.getByRole("button",{name:"حفظ الفترة"}));
  expect(await screen.findByText("تغيرت الفترة")).toBeVisible();expect(saved).not.toHaveBeenCalled();expect(screen.getByRole("button",{name:"حفظ الفترة"})).toBeDisabled();
  const write=vi.mocked(fetch).mock.calls.find(([,init])=>init?.method==="PUT")!;expect(write[0]).toBe("/api/v1/manage/staff-attendance/member/periods/p1");expect(JSON.parse(String(write[1]!.body))).toMatchObject({version:41,periodVersion:9,reason:"تأكيد التوقيت الاصطناعي"});fireEvent.click(screen.getByRole("button",{name:"إعادة تحميل اليوم والفترات"}));expect(saved).toHaveBeenCalledOnce();
});
it("retries a lost response with exactly the same period payload/key and keeps editing locked",async()=>{
  let count=0;global.fetch=vi.fn((input,init)=>String(input).includes("csrf")?response({token:"test"}):init?.method==="POST"?++count===1?Promise.reject(new TypeError("Network failed")):response({version:42}):response(data)) as typeof fetch;
  render(<StaffWorkPeriodsPanel id="member" date={data.workDate} onSaved={saved}/>);fireEvent.click(await screen.findByRole("button",{name:"إضافة فترة عمل"}));inputPeriod();fireEvent.click(screen.getByRole("button",{name:"حفظ الفترة"}));await screen.findByText("تعذر تأكيد الحفظ؛ أعد إرسال نفس الفترة دون تغيير البيانات.");expect(screen.getByLabelText("بداية الفترة — الساعة")).toBeDisabled();expect(saved).not.toHaveBeenCalled();fireEvent.click(screen.getByRole("button",{name:"إعادة إرسال نفس الفترة"}));await waitFor(()=>expect(saved).toHaveBeenCalledOnce());
  const writes=vi.mocked(fetch).mock.calls.filter(([,init])=>init?.method==="POST");expect(writes).toHaveLength(2);expect(writes[1][1]!.body).toBe(writes[0][1]!.body);expect(writes[1][1]!.headers).toEqual(writes[0][1]!.headers);
});
it("does not offer another open period or any write to a read-only manager",async()=>{
  global.fetch=vi.fn(()=>response({...data,items:[{...data.items[0],endTime:null,endDate:null}]})) as typeof fetch;render(<StaffWorkPeriodsPanel id="member" date={data.workDate} onSaved={saved}/>);expect(await screen.findByRole("button",{name:"إضافة فترة عمل"})).toBeDisabled();expect(screen.getByText(/فترة مفتوحة — بلا انصراف مختلق/)).toBeVisible();cleanup();
  global.fetch=vi.fn(()=>response({...data,canEdit:false,items:[{...data.items[0],id:"p1"}]})) as typeof fetch;render(<StaffWorkPeriodsPanel id="member" date={data.workDate} onSaved={saved}/>);await screen.findByText("الفترة 1");expect(screen.queryByRole("button",{name:/إضافة فترة|تصحيح/})).not.toBeInTheDocument();
});
