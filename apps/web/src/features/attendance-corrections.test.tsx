import { cleanup,fireEvent,render,screen } from "@testing-library/react";
import { afterEach,beforeEach,expect,it,vi } from "vitest";
import { AttendanceCorrections,PastAttendanceRequest } from "./attendance-corrections";
afterEach(()=>{cleanup();vi.restoreAllMocks();});
beforeEach(()=>{Object.defineProperty(HTMLDialogElement.prototype,"showModal",{configurable:true,value:function(this:HTMLDialogElement){this.open=true;}});Object.defineProperty(HTMLDialogElement.prototype,"close",{configurable:true,value:function(this:HTMLDialogElement){this.open=false;}});});
const rows=[{id:"e",name:"طفل اصطناعي",status:"NotRecorded",version:41}];
it.each(["id","state","application"])("list review never reports success for a mismatched saved %s",async mismatch=>{
 const row={id:"r",version:22,trainingSessionId:"s",sessionDate:"2026-10-21",group:"مجموعة اصطناعية",kind:"Players",state:"Pending",reason:"سبب اصطناعي",canApprove:true,items:[]};
 global.fetch=vi.fn(async(url,init)=>new Response(JSON.stringify(String(url).endsWith("/csrf")?{token:"test"}:init?.method==="POST"?{id:"r"}:String(url).endsWith("/corrections/r")?{...row,id:mismatch==="id"?"other":"r",state:mismatch==="state"?"Pending":"Approved",appliedAtUtc:mismatch==="application"?null:"2026-10-22T12:00:00Z"}:{page:1,pageSize:25,total:1,items:[row]}),{status:200}));
 render(<AttendanceCorrections/>);fireEvent.click(await screen.findByRole("button",{name:"اعتماد وتطبيق التصحيح"}));await screen.findByText("لم تتأكد مطابقة القرار المحفوظ؛ أعد فتح الطلب.");expect(screen.queryByRole("status")).not.toBeInTheDocument();
});
it("requires a changed versioned snapshot and reason; request does not claim attendance was saved",async()=>{
  global.fetch=vi.fn(async url=>new Response(JSON.stringify(String(url).endsWith("/csrf")?{token:"test"}:{id:"r"}),{status:200}));render(<PastAttendanceRequest sessionId="s" kind="Players" rows={rows}/>);fireEvent.click(screen.getByRole("button",{name:"طلب تصحيح يوم سابق"}));expect(screen.getByRole("button",{name:"إرسال الطلب للمالك"})).toBeDisabled();fireEvent.change(screen.getByLabelText("الحالة المقترحة طفل اصطناعي"),{target:{value:"Absent"}});fireEvent.change(screen.getByLabelText("سبب تصحيح اليوم السابق"),{target:{value:"مراجعة ورقية اصطناعية"}});fireEvent.click(screen.getByRole("button",{name:"إرسال الطلب للمالك"}));await screen.findByText(/لم يتغير الحضور بعد/);const call=vi.mocked(fetch).mock.calls.find(([,i])=>i?.method==="POST");expect(JSON.parse(String(call?.[1]?.body))).toMatchObject({kind:"Players",items:[{subjectId:"e",status:"Absent",expectedVersion:41}]});expect(new Headers(call?.[1]?.headers).get("Idempotency-Key")).toBeTruthy();expect(vi.mocked(fetch).mock.calls.some(([,i])=>i?.method==="PUT")).toBe(false);
});
it("cannot submit an unversioned stale client row",()=>{render(<PastAttendanceRequest sessionId="s" kind="Staff" rows={[{...rows[0],version:undefined}]}/>);fireEvent.click(screen.getByRole("button",{name:"طلب تصحيح يوم سابق"}));fireEvent.change(screen.getByLabelText("الحالة المقترحة طفل اصطناعي"),{target:{value:"Absent"}});fireEvent.change(screen.getByLabelText("سبب تصحيح اليوم السابق"),{target:{value:"سبب"}});expect(screen.getByRole("button",{name:"إرسال الطلب للمالك"})).toBeDisabled();});
it("owner review sends the exact request revision and refreshes after application",async()=>{let approved=false;global.fetch=vi.fn(async(url,init)=>new Response(JSON.stringify(String(url).endsWith("/csrf")?{token:"test"}:init?.method==="POST"?(approved=true,{id:"r"}):String(url).endsWith("/corrections/r")?{id:"r",state:"Approved",appliedAtUtc:"2026-10-22T12:00:00Z"}:{page:1,pageSize:25,total:1,items:[{id:"r",version:22,trainingSessionId:"s",sessionDate:"2026-09-28",group:"سباحة اصطناعية",kind:"Players",state:approved?"Approved":"Pending",reason:"سبب مسجل",canApprove:!approved,items:[{subjectId:"e",name:"طفل اصطناعي",before:"NotRecorded",after:"Absent"}]}]}),{status:200}));render(<AttendanceCorrections/>);fireEvent.click(await screen.findByRole("button",{name:"اعتماد وتطبيق التصحيح"}));await screen.findByText("اعتمد وطُبق");const call=vi.mocked(fetch).mock.calls.find(([,i])=>i?.method==="POST");expect(JSON.parse(String(call?.[1]?.body))).toEqual({expectedVersion:22});});

it("pages older history, keeps the page after failure, and uses its exact source link",async()=>{
 let fail=true;
 global.fetch=vi.fn(async url=>{
  const page=new URL(String(url),"http://localhost").searchParams.get("page");
  if(page==="2"&&fail){fail=false;return new Response("",{status:503});}
  return new Response(JSON.stringify({page:Number(page),pageSize:25,total:26,items:[{id:`request-${page}`,group:`مجموعة الصفحة ${page}`,kind:"Players",state:"Rejected",sessionDate:"2026-10-21",trainingSessionId:"s",reason:"سجل اصطناعي",items:[]}]}),{status:200});
 });
 render(<AttendanceCorrections/>);
 await screen.findByText("مجموعة الصفحة 1");
 expect(screen.getByRole("button",{name:"الصفحة السابقة"})).toBeDisabled();
 fireEvent.click(screen.getByRole("button",{name:"الصفحة التالية"}));await screen.findByText("تعذر تحميل طلبات تصحيح الحضور.");
 expect(screen.getByText("مجموعة الصفحة 1")).toBeInTheDocument();expect(screen.getByText("الصفحة 1 · إجمالي الطلبات 26")).toBeInTheDocument();
 fireEvent.click(screen.getByRole("button",{name:"الصفحة التالية"}));await screen.findByText("مجموعة الصفحة 2");
 expect(screen.getByRole("link",{name:"عرض طلب تصحيح الحضور"})).toHaveAttribute("href","/dashboard/attendance/corrections/request-2");
 expect(screen.getByRole("button",{name:"الصفحة التالية"})).toBeDisabled();
 fireEvent.click(screen.getByRole("button",{name:"الصفحة السابقة"}));await screen.findByText("مجموعة الصفحة 1");
});
it("rejects a response for another page instead of mislabelling the visible history",async()=>{
 global.fetch=vi.fn(async()=>new Response(JSON.stringify({page:2,pageSize:25,total:26,items:[]}),{status:200}));
 render(<AttendanceCorrections/>);await screen.findByText("تعذر التحقق من صفحة طلبات التصحيح.");
 expect(screen.queryByText("الصفحة 1 · إجمالي الطلبات 26")).not.toBeInTheDocument();
});
