import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { OccupancySummary, VisitDialog } from "./resource-visits";
afterEach(()=>{cleanup();vi.restoreAllMocks();});
beforeEach(()=>{ Object.defineProperty(HTMLDialogElement.prototype,"showModal",{configurable:true,value:function(this:HTMLDialogElement){this.open=true;}}); Object.defineProperty(HTMLDialogElement.prototype,"close",{configurable:true,value:function(this:HTMLDialogElement){this.open=false;}}); });
const data={serviceKind:"Training",players:[{enrollmentId:"e",name:"لاعب اصطناعي",playerCode:"P1"}],companions:[],visits:[{id:"v",version:7,name:"طفل بالداخل",personKind:"Participant",enteredAtUtc:"2026-10-12T08:00:00Z",staleOpen:true}]};
it("does not equate open visits with physical attendance or hide stale exits",()=>{render(<OccupancySummary value={{participants:6,companions:2,staleOpen:1,estimated:true}} />);expect(screen.getByText("6 مشارك · 2 مرافق")).toBeVisible();expect(screen.getByText(/إشغال تقديري/)).toBeVisible();expect(screen.getByText(/تحتاج مراجعة/)).toBeVisible();});
it("requires explicit identity check before entry and saves only an entry intent",async()=>{
  global.fetch=vi.fn(async(url,init)=>new Response(JSON.stringify(String(url).endsWith("/csrf")?{token:"test"}:init?.method==="POST"?{}:data),{status:200})) as typeof fetch; const saved=vi.fn(async()=>{});render(<VisitDialog booking={{id:"b",resource:"A"}} canAdd canEdit onSaved={saved} onClose={()=>{}} />);
  const select=await screen.findByLabelText("اللاعب");fireEvent.change(select,{target:{value:"e"}});expect(screen.getByRole("button",{name:"تسجيل الدخول"})).toBeDisabled();fireEvent.click(screen.getByLabelText("تحققت من هوية الشخص الموجود أمامي"));fireEvent.click(screen.getByRole("button",{name:"تسجيل الدخول"}));await vi.waitFor(()=>expect(saved).toHaveBeenCalledOnce());const call=vi.mocked(fetch).mock.calls.find(([url,init])=>String(url).endsWith("/entry")&&init?.method==="POST");expect(JSON.parse(String(call?.[1]?.body))).toEqual({enrollmentId:"e",identityConfirmed:true});expect(new Headers(call?.[1]?.headers).get("Idempotency-Key")).toBeTruthy();expect(screen.getByText(/حضور الحصة والرصيد لم يتغيرا/)).toBeVisible();
});
it("read-only scope exposes history but neither admission nor checkout writes",async()=>{
  global.fetch=vi.fn(async()=>new Response(JSON.stringify(data),{status:200})) as typeof fetch;render(<VisitDialog booking={{id:"b",resource:"A"}} canAdd={false} canEdit={false} onSaved={async()=>{}} onClose={()=>{}} />);await screen.findByText("طفل بالداخل");expect(screen.queryByRole("button",{name:"تسجيل الدخول"})).not.toBeInTheDocument();expect(screen.queryByRole("button",{name:/تسجيل خروج/})).not.toBeInTheDocument();
});
it("checkout sends the displayed version and does not pretend success on conflict",async()=>{
  global.fetch=vi.fn(async(url,init)=>new Response(JSON.stringify(String(url).endsWith("/csrf")?{token:"test"}:init?.method==="POST"?{title:"تغيرت الزيارة"}:data),{status:init?.method==="POST"?409:200})) as typeof fetch;const saved=vi.fn(async()=>{});render(<VisitDialog booking={{id:"b",resource:"A"}} canAdd={false} canEdit onSaved={saved} onClose={()=>{}} />);fireEvent.click(await screen.findByRole("button",{name:"تسجيل خروج طفل بالداخل"}));await screen.findByText("تغيرت الزيارة");expect(saved).not.toHaveBeenCalled();const call=vi.mocked(fetch).mock.calls.find(([,init])=>init?.method==="POST");expect(JSON.parse(String(call?.[1]?.body))).toEqual({expectedVersion:7});
});
it("admits a confirmed day-pass ticket only after explicit identity confirmation",async()=>{
  const ticketData={...data,serviceKind:"DayPass",players:[],tickets:[{ticketId:"ticket",name:"طفل زيارة اصطناعي",playerCode:"P2",used:false}],visits:[]};
  global.fetch=vi.fn(async(url,init)=>new Response(JSON.stringify(String(url).endsWith("/csrf")?{token:"test"}:init?.method==="POST"?{}:ticketData),{status:200})) as typeof fetch;
  const saved=vi.fn(async()=>{});render(<VisitDialog booking={{id:"b",resource:"A"}} canAdd canEdit onSaved={saved} onClose={()=>{}} />);
  const select=await screen.findByLabelText("اللاعب");fireEvent.change(select,{target:{value:"ticket"}});expect(screen.getByRole("button",{name:"تسجيل الدخول"})).toBeDisabled();fireEvent.click(screen.getByLabelText("تحققت من هوية الشخص الموجود أمامي"));fireEvent.click(screen.getByRole("button",{name:"تسجيل الدخول"}));await vi.waitFor(()=>expect(saved).toHaveBeenCalledOnce());
  const call=vi.mocked(fetch).mock.calls.find(([url,init])=>String(url).endsWith("/entry")&&init?.method==="POST");expect(JSON.parse(String(call?.[1]?.body))).toEqual({dayPassTicketId:"ticket",identityConfirmed:true});
});
