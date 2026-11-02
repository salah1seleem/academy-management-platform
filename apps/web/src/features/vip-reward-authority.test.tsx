import {act,cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {VipRewards} from "./vip-rewards";
const root="/api/v1/manage/vip-rewards";
const conditions="باقة واحدة دون جمع العروض";
const terms={items:[{id:"guardian",name:"أسرة اصطناعية",monthlyValue:1000}],currency:"EGP",today:"2026-10-23",conditions};
const ledger=(items:object[])=>({items,count:items.length,page:1,conditions});
const reward={id:"reward",guardianId:"guardian",guardian:"أسرة اصطناعية",percentage:20,message:"رسالة المكافأة",expiresOn:"2026-10-23",issuedOn:"2026-10-23",state:"Available",read:false,delivery:"StoredInApp",history:[]};
const me=(role="AcademyOwner",membershipId="member")=>({userId:"user",academyId:"academy",membershipId,role});
function deferred<T>(){let resolve!:(value:T)=>void;const promise=new Promise<T>(r=>{resolve=r;});return {promise,resolve};}
afterEach(()=>{cleanup();vi.restoreAllMocks();});
async function preview(){
  fireEvent.click(await screen.findByRole("button",{name:"إرسال مكافأة"}));
  fireEvent.click(await screen.findByRole("checkbox"));
  fireEvent.change(screen.getByLabelText("نسبة الخصم %"),{target:{value:"20"}});
  fireEvent.change(screen.getByLabelText("رسالة الأسرة"),{target:{value:reward.message}});
  fireEvent.click(screen.getByRole("button",{name:"معاينة المكافأة"}));
  await screen.findByRole("region",{name:"تأكيد المكافأة"});
}
function setup(handler:(path:string,init?:RequestInit)=>Promise<Response>|Response){
  global.fetch=vi.fn(async(url,init)=>{
    const path=String(url);
    if(path==="/api/v1/auth/csrf")return Response.json({token:"csrf"});
    if(path===`${root}/options`)return Response.json(terms);
    if(path===`${root}/preview`){const body=JSON.parse(String(init?.body));return Response.json({...body,quoteHash:"hash",recipients:terms.items,conditions,delivery:"إشعار داخلي",currency:"EGP"});}
    return handler(path,init);
  }) as typeof fetch;
}
it("waits for authoritative matching detail before announcing issue success",async()=>{
  const detail=deferred<Response>();let body:Record<string,unknown>={};let reads=0;
  const announce=vi.fn();window.addEventListener("academy-save-success",announce);
  setup((path,init)=>{
    if(path==="/api/v1/me")return Response.json(me());
    if(path===`${root}/`){body=JSON.parse(String(init?.body));return Response.json({ids:["reward"]},{status:201});}
    if(path===`${root}/reward`){reads++;return detail.promise;}
    return Response.json(ledger([]));
  });
  render(<VipRewards owner/>);await preview();fireEvent.click(screen.getByRole("button",{name:"تأكيد الإصدار وحفظ الإشعار الداخلي"}));
  await waitFor(()=>expect(reads).toBe(1));expect(announce).not.toHaveBeenCalled();expect(screen.getByLabelText("نسبة الخصم %")).toBeDisabled();
  await act(async()=>detail.resolve(Response.json(ledger([{...reward,requestId:body.requestId,currency:"EGP"}]))));
  await screen.findByRole("button",{name:"إرسال مكافأة"});expect(announce).toHaveBeenCalledTimes(1);
  window.removeEventListener("academy-save-success",announce);
});
it("lost POST response recovers by request lookup and never posts again",async()=>{
  let body:Record<string,unknown>={};let posts=0;let lookups=0;
  setup((path,init)=>{
    if(path==="/api/v1/me")return Response.json(me());
    if(path===`${root}/`){posts++;body=JSON.parse(String(init?.body));throw new TypeError("network");}
    if(path.includes("/requests/")){lookups++;return Response.json({ids:["reward"]});}
    if(path===`${root}/reward`)return Response.json(ledger([{...reward,requestId:body.requestId,currency:"EGP"}]));
    return Response.json(ledger([]));
  });
  render(<VipRewards owner/>);await preview();fireEvent.click(screen.getByRole("button",{name:"تأكيد الإصدار وحفظ الإشعار الداخلي"}));
  const retry=await screen.findByRole("button",{name:"إعادة التحقق من الإصدار"});await waitFor(()=>expect(retry).toBeEnabled());
  expect(screen.getByLabelText("رسالة الأسرة")).toBeDisabled();fireEvent.click(retry);
  await screen.findByRole("button",{name:"إرسال مكافأة"});expect(posts).toBe(1);expect(lookups).toBe(1);
});
it("only an explicit missing-request recovery enables the identical POST retry",async()=>{
  const bodies:Record<string,unknown>[]=[];
  setup((path,init)=>{
    if(path==="/api/v1/me")return Response.json(me());
    if(path===`${root}/`){bodies.push(JSON.parse(String(init?.body)));if(bodies.length===1)throw new TypeError("network");return Response.json({ids:["reward"]});}
    if(path.includes("/requests/"))return new Response(null,{status:404});
    if(path===`${root}/reward`)return Response.json(ledger([{...reward,requestId:bodies[0].requestId,currency:"EGP"}]));
    return Response.json(ledger([]));
  });
  render(<VipRewards owner/>);await preview();fireEvent.click(screen.getByRole("button",{name:"تأكيد الإصدار وحفظ الإشعار الداخلي"}));
  const retry=await screen.findByRole("button",{name:"إعادة التحقق من الإصدار"});await waitFor(()=>expect(retry).toBeEnabled());expect(screen.queryByText("إعادة تأكيد الطلب نفسه")).not.toBeInTheDocument();fireEvent.click(retry);
  fireEvent.click(await screen.findByRole("button",{name:"إعادة تأكيد الطلب نفسه"}));await screen.findByRole("button",{name:"إرسال مكافأة"});expect(bodies).toHaveLength(2);expect(bodies[1]).toEqual(bodies[0]);
});
it("a mismatched saved percentage cannot close the issue form or announce success",async()=>{
  let body:Record<string,unknown>={};const announce=vi.fn();window.addEventListener("academy-save-success",announce);
  setup((path,init)=>{if(path==="/api/v1/me")return Response.json(me());if(path===`${root}/`){body=JSON.parse(String(init?.body));return Response.json({ids:["reward"]});}if(path===`${root}/reward`)return Response.json(ledger([{...reward,percentage:30,requestId:body.requestId,currency:"EGP"}]));return Response.json(ledger([]));});
  render(<VipRewards owner/>);await preview();fireEvent.click(screen.getByRole("button",{name:"تأكيد الإصدار وحفظ الإشعار الداخلي"}));await screen.findByText(/تعذر مطابقة/);expect(announce).not.toHaveBeenCalled();expect(screen.getByLabelText("رسالة الأسرة")).toBeDisabled();window.removeEventListener("academy-save-success",announce);
});
it("a definite rejection unlocks editing and invalidates preview",async()=>{
  setup(path=>path==="/api/v1/me"?Response.json(me()):path===`${root}/`?Response.json({message:"تغيرت الأهلية. أعد المعاينة."},{status:409}):Response.json(ledger([])));
  render(<VipRewards owner/>);await preview();fireEvent.click(screen.getByRole("button",{name:"تأكيد الإصدار وحفظ الإشعار الداخلي"}));await screen.findByText("تغيرت الأهلية. أعد المعاينة.");expect(screen.getByLabelText("رسالة الأسرة")).toBeEnabled();expect(screen.queryByRole("region",{name:"تأكيد المكافأة"})).not.toBeInTheDocument();
});
it("a notification detail reads the exact record regardless of pagination and rejects another id",async()=>{
  const paths:string[]=[];
  setup(path=>{paths.push(path);return path==="/api/v1/me"?Response.json(me("Guardian")):Response.json(ledger([{...reward,id:"another",message:"رسالة أسرة أخرى"}]));});
  render(<VipRewards rewardId="older"/>);await screen.findByText("تعذر التحقق من المكافأة المطلوبة.");expect(paths).toContain("/api/v1/guardian/vip-rewards/older");expect(paths.some(p=>p.includes("?page="))).toBe(false);expect(screen.queryByText("رسالة أسرة أخرى")).not.toBeInTheDocument();expect(screen.queryByRole("navigation",{name:"صفحات المكافآت"})).not.toBeInTheDocument();
});
it("focus revocation erases private data and remains revoked after the same account returns",async()=>{
  let revoked=false;
  setup(path=>path==="/api/v1/me"?(revoked?new Response(null,{status:403}):Response.json(me())):Response.json(ledger([reward])));
  render(<VipRewards owner/>);await screen.findByText(reward.message);revoked=true;fireEvent(window,new Event("focus"));await waitFor(()=>expect(screen.queryByText(reward.message)).not.toBeInTheDocument());revoked=false;fireEvent(window,new Event("focus"));expect(screen.queryByRole("button",{name:"إرسال مكافأة"})).not.toBeInTheDocument();
});
it("an old deferred ledger cannot appear after membership changes",async()=>{
  const result=deferred<Response>();let changed=false;let requested=false;
  setup(path=>{if(path==="/api/v1/me")return Response.json(me("AcademyOwner",changed?"other":"member"));requested=true;return result.promise;});
  render(<VipRewards owner/>);await waitFor(()=>expect(requested).toBe(true));changed=true;fireEvent(window,new Event("focus"));await screen.findByText(/تعذر تحميل المكافآت/);await act(async()=>result.resolve(Response.json(ledger([reward]))));expect(screen.queryByText(reward.message)).not.toBeInTheDocument();
});
it("cancellation verifies authoritative state and recovers with GET only",async()=>{
  vi.spyOn(window,"confirm").mockReturnValue(true);let posts=0,details=0;const announce=vi.fn();window.addEventListener("academy-save-success",announce);
  setup(path=>{if(path==="/api/v1/me")return Response.json(me());if(path.endsWith("/cancel")){posts++;return Response.json({});}if(path===`${root}/reward`){details++;if(details===1)throw new TypeError("network");return Response.json(ledger([{...reward,state:"Cancelled"}]));}return Response.json(ledger([{...reward,state:details>1?"Cancelled":"Available"}]));});
  render(<VipRewards owner/>);fireEvent.click(await screen.findByRole("button",{name:"إلغاء المكافأة"}));const retry=await screen.findByRole("button",{name:"إعادة التحقق من الإلغاء"});await waitFor(()=>expect(retry).toBeEnabled());expect(announce).not.toHaveBeenCalled();fireEvent.click(retry);await waitFor(()=>expect(screen.queryByRole("button",{name:"إعادة التحقق من الإلغاء"})).not.toBeInTheDocument());expect(posts).toBe(1);expect(announce).toHaveBeenCalledTimes(1);window.removeEventListener("academy-save-success",announce);
});
it("a transient focus check preserves the frozen request for GET recovery",async()=>{
  let temporary=false;let body:Record<string,unknown>={};
  setup((path,init)=>{if(path==="/api/v1/me"){if(temporary)throw new TypeError("network");return Response.json(me());}if(path===`${root}/`){body=JSON.parse(String(init?.body));throw new TypeError("network");}if(path.includes("/requests/"))return Response.json({ids:["reward"]});if(path===`${root}/reward`)return Response.json(ledger([{...reward,requestId:body.requestId,currency:"EGP"}]));return Response.json(ledger([]));});
  render(<VipRewards owner/>);await preview();fireEvent.click(screen.getByRole("button",{name:"تأكيد الإصدار وحفظ الإشعار الداخلي"}));const retry=await screen.findByRole("button",{name:"إعادة التحقق من الإصدار"});await waitFor(()=>expect(retry).toBeEnabled());temporary=true;await act(async()=>fireEvent(window,new Event("focus")));expect(screen.getByLabelText("رسالة الأسرة")).toBeDisabled();temporary=false;fireEvent.click(retry);await screen.findByRole("button",{name:"إرسال مكافأة"});
});
it("a late issuance detail after logout cannot announce success or retain private terms",async()=>{
  let revoked=false,requested=false;const detail=deferred<Response>();let body:Record<string,unknown>={};const announce=vi.fn();window.addEventListener("academy-save-success",announce);
  setup((path,init)=>{if(path==="/api/v1/me")return revoked?new Response(null,{status:401}):Response.json(me());if(path===`${root}/`){body=JSON.parse(String(init?.body));return Response.json({ids:["reward"]});}if(path===`${root}/reward`){requested=true;return detail.promise;}return Response.json(ledger([]));});
  render(<VipRewards owner/>);await preview();fireEvent.click(screen.getByRole("button",{name:"تأكيد الإصدار وحفظ الإشعار الداخلي"}));await waitFor(()=>expect(requested).toBe(true));revoked=true;fireEvent(window,new Event("focus"));await screen.findByText(/تعذر تحميل المكافآت/);await act(async()=>detail.resolve(Response.json(ledger([{...reward,requestId:body.requestId,currency:"EGP"}]))));expect(announce).not.toHaveBeenCalled();expect(screen.queryByText(reward.message)).not.toBeInTheDocument();window.removeEventListener("academy-save-success",announce);
});
it("a definite cancellation conflict retains its reason and refreshes the actual reserved state",async()=>{
  vi.spyOn(window,"confirm").mockReturnValue(true);let conflict=false;
  setup(path=>{if(path==="/api/v1/me")return Response.json(me());if(path.endsWith("/cancel")){conflict=true;return Response.json({message:"المكافأة محجوزة لطلب قائم."},{status:409});}return Response.json(ledger([{...reward,state:conflict?"Reserved":"Available"}]));});
  render(<VipRewards owner/>);fireEvent.click(await screen.findByRole("button",{name:"إلغاء المكافأة"}));await screen.findByText(/محجوزة لطلب ·/);expect(screen.getByText("المكافأة محجوزة لطلب قائم.")).toBeInTheDocument();expect(screen.queryByRole("button",{name:"إعادة التحقق من الإلغاء"})).not.toBeInTheDocument();
});
it("changing role remounts the scoped ledger and ignores its old response",async()=>{
  let guardian=false,requested=false;const old=deferred<Response>();
  setup(path=>{if(path==="/api/v1/me")return Response.json(me(guardian?"Guardian":"AcademyOwner"));if(path.startsWith(root)){requested=true;return old.promise;}return Response.json(ledger([{...reward,message:"مكافأة الأسرة الحالية"}]));});
  const view=render(<VipRewards owner/>);await waitFor(()=>expect(requested).toBe(true));guardian=true;view.rerender(<VipRewards/>);await screen.findByText("مكافأة الأسرة الحالية");await act(async()=>old.resolve(Response.json(ledger([{...reward,message:"بيانات المالك السابقة"}]))));expect(screen.queryByText("بيانات المالك السابقة")).not.toBeInTheDocument();expect(screen.queryByRole("button",{name:"إرسال مكافأة"})).not.toBeInTheDocument();
});
