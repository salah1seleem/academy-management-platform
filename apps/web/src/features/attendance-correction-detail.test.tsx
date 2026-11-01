import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {AttendanceCorrectionDetail} from "./attendance-correction-detail";
import {NotificationInbox} from "./notification-center";
vi.mock("next/navigation",()=>({useSearchParams:()=>new URLSearchParams()}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const initial={id:"exact-request",version:12,trainingSessionId:"exact-session",sessionDate:"2026-10-21",group:"مجموعة اصطناعية",kind:"Players",state:"Pending",reason:"طلب اصطناعي",canApprove:true,requester:"إداري اصطناعي",items:[{subjectId:"enrollment",name:"لاعب اصطناعي",before:"Absent",after:"NotRecorded"}]};
const response=(data:unknown,status=200)=>new Response(JSON.stringify(data),{status});
it("links the decision notice only to its persisted request and shows an administrator a read-only source",async()=>{
 global.fetch=vi.fn(async path=>String(path).includes("/notifications")?response({unread:1,total:1,items:[{id:"notice",message:"اعتمد المالك الطلب",createdAtUtc:"2026-10-22",isRead:false,reward:false,targetKind:"AttendanceCorrection",targetId:initial.id}]}):response({...initial,canApprove:false}));
 const inbox=render(<NotificationInbox/>);expect(await screen.findByRole("link",{name:"عرض طلب تصحيح الحضور"})).toHaveAttribute("href","/dashboard/attendance/corrections/exact-request");inbox.unmount();
 render(<AttendanceCorrectionDetail id={initial.id}/>);await screen.findByText("مجموعة اصطناعية");expect(screen.queryByRole("button",{name:"اعتماد وتطبيق التصحيح"})).not.toBeInTheDocument();expect(screen.getByRole("link",{name:"كشف الحصة"})).toHaveAttribute("href","/dashboard/attendance/sessions/exact-session/players");
 expect(vi.mocked(fetch).mock.calls.some(([,init])=>init?.method)).toBe(false);
});
it("recovers a lost readback with GET only and verifies approval application and the frozen reason before success",async()=>{
 let saved=false,fail=true;const announce=vi.fn();window.addEventListener("academy-save-success",announce);
 global.fetch=vi.fn(async(path,init)=>{
  if(String(path).endsWith("/csrf"))return response({token:"csrf"});
  if(init?.method==="POST"){saved=true;return response({id:initial.id,state:"Approved"});}
  if(saved&&fail){fail=false;return response({},503);}
  return response(saved?{...initial,state:"Approved",canApprove:false,decisionReason:"مراجعة اصطناعية",appliedAtUtc:"2026-10-22T12:00:00Z"}:initial);
 });
 render(<AttendanceCorrectionDetail id={initial.id}/>);fireEvent.change(await screen.findByLabelText("ملاحظة القرار (اختيارية)"),{target:{value:"  مراجعة اصطناعية  "}});fireEvent.click(screen.getByRole("button",{name:"اعتماد وتطبيق التصحيح"}));
 fireEvent.click(await screen.findByRole("button",{name:"إعادة التحقق من القرار"}));await screen.findByText("تأكد اعتماد التصحيح وتطبيقه من السجل المحفوظ.");
 const posts=vi.mocked(fetch).mock.calls.filter(([,init])=>init?.method==="POST");expect(posts).toHaveLength(1);expect(JSON.parse(String(posts[0][1]?.body))).toEqual({expectedVersion:12,reason:"مراجعة اصطناعية"});expect(announce).not.toHaveBeenCalled();window.removeEventListener("academy-save-success",announce);
});
it("replays exactly the same rejection after a lost POST response",async()=>{
 let posts=0;global.fetch=vi.fn(async(path,init)=>{if(String(path).endsWith("/csrf"))return response({token:"csrf"});if(init?.method==="POST"){if(++posts===1)throw Error("lost");return response({id:initial.id});}return response(posts?{...initial,state:"Rejected",decisionReason:null,canApprove:false}:initial);});
 render(<AttendanceCorrectionDetail id={initial.id}/>);fireEvent.click(await screen.findByRole("button",{name:"عدم الاعتماد"}));fireEvent.click(await screen.findByRole("button",{name:"إعادة محاولة القرار نفسه"}));await screen.findByText("تأكد عدم اعتماد الطلب من السجل المحفوظ.");
 const writes=vi.mocked(fetch).mock.calls.filter(([,init])=>init?.method==="POST");expect(writes).toHaveLength(2);expect(writes[0][0]).toBe(writes[1][0]);expect(writes[0][1]?.body).toBe(writes[1][1]?.body);expect(JSON.parse(String(writes[0][1]?.body))).toEqual({expectedVersion:12,reason:null});
});
it.each(["state","application","reason"])("never reports success when the persisted %s differs",async mismatch=>{
 let saved=false;global.fetch=vi.fn(async(path,init)=>{if(String(path).endsWith("/csrf"))return response({token:"csrf"});if(init?.method==="POST"){saved=true;return response({id:initial.id});}return response(saved?{...initial,state:mismatch==="state"?"Pending":"Approved",appliedAtUtc:mismatch==="application"?null:"2026-10-22T12:00:00Z",decisionReason:mismatch==="reason"?"قرار آخر":null}:initial);});
 render(<AttendanceCorrectionDetail id={initial.id}/>);fireEvent.click(await screen.findByRole("button",{name:"اعتماد وتطبيق التصحيح"}));await screen.findByRole("button",{name:"إعادة التحقق من القرار"});expect(screen.queryByRole("status")).not.toBeInTheDocument();expect(screen.queryByRole("button",{name:"عدم الاعتماد"})).not.toBeInTheDocument();
});
it.each([403,404])("does not show a request outside the authoritative scope (%s)",async status=>{
 global.fetch=vi.fn(async()=>response({message:"غير متاح"},status));render(<AttendanceCorrectionDetail id="foreign-request"/>);await screen.findByText("تعذر تحميل طلب التصحيح أو لا توجد صلاحية لعرضه.");expect(screen.queryByRole("region",{name:"تفاصيل طلب تصحيح الحضور"})).not.toBeInTheDocument();expect(screen.queryByRole("button",{name:"اعتماد وتطبيق التصحيح"})).not.toBeInTheDocument();
});
it("rejects a mismatched source id without exposing its contents",async()=>{
 global.fetch=vi.fn(async()=>response({...initial,id:"another-request"}));render(<AttendanceCorrectionDetail id={initial.id}/>);await waitFor(()=>expect(screen.getByText("تعذر تحميل طلب التصحيح أو لا توجد صلاحية لعرضه.")).toBeInTheDocument());expect(screen.queryByText(initial.group)).not.toBeInTheDocument();
});
it("allows an explicit authoritative reload after a rejected stale decision without resubmitting it",async()=>{
 let posted=false;global.fetch=vi.fn(async(path,init)=>{if(String(path).endsWith("/csrf"))return response({token:"csrf"});if(init?.method==="POST"){posted=true;return response({message:"تغير الطلب"},409);}return response(posted?{...initial,state:"Rejected",version:13,canApprove:false,decisionReason:"راجعه مالك آخر"}:initial);});
 render(<AttendanceCorrectionDetail id={initial.id}/>);fireEvent.click(await screen.findByRole("button",{name:"اعتماد وتطبيق التصحيح"}));await screen.findByRole("button",{name:"إعادة محاولة القرار نفسه"});fireEvent.click(screen.getByRole("button",{name:"إعادة تحميل الطلب"}));await screen.findByText("لم يُعتمد");expect(screen.queryByRole("status")).not.toBeInTheDocument();expect(screen.queryByRole("button",{name:"إعادة محاولة القرار نفسه"})).not.toBeInTheDocument();expect(vi.mocked(fetch).mock.calls.filter(([,init])=>init?.method==="POST")).toHaveLength(1);
});
