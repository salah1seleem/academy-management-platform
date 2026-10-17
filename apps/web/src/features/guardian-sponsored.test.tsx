import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GuardianSponsoredCard, GuardianSponsorSettings, GuardianSponsorDetail } from "./guardian-sponsored";
import { CommunityCampaigns } from "./community-campaigns";
import { GuardianHome } from "./guardian-core";
const push=vi.hoisted(()=>vi.fn());vi.mock("next/navigation",()=>({useRouter:()=>({push,replace:vi.fn()})}));
const ad={id:"ad",title:"ورشة برمجة افتراضية",body:"تفاصيل واضحة للأسرة",advertiser:"مركز افتراضي",buttonText:"التفاصيل",imageUrl:null,endsAtUtc:"2100-01-01T00:00:00Z",destination:"InternalDetails"};
let intersect:IntersectionObserverCallback;
it("a detailed creative loses its cached content when the bounded lease is reauthorized and revoked",async()=>{
  let revoked=false;global.fetch=vi.fn(async()=>revoked?Response.json({},{status:404}):Response.json(ad)) as typeof fetch;
  vi.useFakeTimers();await act(async()=>{render(<GuardianSponsorDetail id="ad"/>);});expect(screen.getByRole("heading",{name:ad.title})).toBeVisible();revoked=true;
  await act(()=>vi.advanceTimersByTimeAsync(30_001));expect(screen.queryByRole("heading",{name:ad.title})).not.toBeInTheDocument();expect(screen.getByRole("alert")).toHaveTextContent("سُحبت الموافقة");
});
class Observer {constructor(callback:IntersectionObserverCallback){intersect=callback;}observe(){}disconnect(){}unobserve(){}takeRecords(){return [];}}
afterEach(()=>{cleanup();vi.restoreAllMocks();vi.unstubAllGlobals();vi.useRealTimers();push.mockClear();});
it("an absent optional feed adds no placeholder, view or checkout",async()=>{global.fetch=vi.fn(async()=>Response.json({item:null})) as typeof fetch;render(<GuardianSponsoredCard/>);await waitFor(()=>expect(fetch).toHaveBeenCalledTimes(1));expect(screen.queryByRole("region",{name:"محتوى برعاية"})).not.toBeInTheDocument();});
it("GET is not an impression and a half-visible uninterrupted second sends one measured event",async()=>{
  vi.stubGlobal("IntersectionObserver",Observer);const posts:unknown[]=[];global.fetch=vi.fn(async(input,init)=>{if(String(input).endsWith("/csrf"))return Response.json({token:"qa"});if(init?.method==="POST"){posts.push(JSON.parse(String(init.body)));return new Response(null,{status:204});}return Response.json({item:ad});}) as typeof fetch;
  render(<GuardianSponsoredCard/>);await screen.findByRole("region",{name:"محتوى برعاية"});expect(posts).toEqual([]);vi.useFakeTimers();
  act(()=>intersect([{intersectionRatio:.4} as IntersectionObserverEntry],{} as IntersectionObserver));await act(()=>vi.advanceTimersByTimeAsync(1200));expect(posts).toEqual([]);
  act(()=>intersect([{intersectionRatio:.6} as IntersectionObserverEntry],{} as IntersectionObserver));await act(()=>vi.advanceTimersByTimeAsync(999));expect(posts).toEqual([]);await act(()=>vi.advanceTimersByTimeAsync(2));expect(posts).toEqual([{kind:"Impression"}]);
  act(()=>intersect([{intersectionRatio:1} as IntersectionObserverEntry],{} as IntersectionObserver));await act(()=>vi.advanceTimersByTimeAsync(1500));expect(posts).toHaveLength(1);
});
it("the urgent schedule notice is before the sponsored placement and essential actions remain",async()=>{
  vi.stubGlobal("IntersectionObserver",Observer);global.fetch=vi.fn(async input=>Response.json(String(input).endsWith("/home")?{academyName:"أكاديمية افتراضية",guardianName:"ولي أمر افتراضي",today:"2026-10-17",children:[],sportContexts:[],urgentAlerts:[{id:"n",message:"تم إلغاء مباراة ابنك"}]}:String(input).endsWith("/feed")?{item:ad}:String(input).endsWith("/preference")?{available:false}:String(input).endsWith("/receivables")?{outstanding:"0.00",overpaymentCredit:"0.00",items:[]}:[])) as typeof fetch;
  render(<GuardianHome/>);const notice=await screen.findByRole("alert");const card=await screen.findByRole("region",{name:"محتوى برعاية"});expect(notice.compareDocumentPosition(card)&Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();expect(screen.getByRole("navigation",{name:"إجراءات الاشتراك"})).toBeVisible();
});
it("withdrawal remains available when the academy or licence is off",async()=>{
  let body:unknown;global.fetch=vi.fn(async(input,init)=>String(input).endsWith("/csrf")?Response.json({token:"qa"}):init?.method==="PUT"?(body=JSON.parse(String(init.body)),new Response(null,{status:204})):Response.json({available:false,inAppOptIn:true,adultDeclared:true,notice:"موافقة داخل التطبيق فقط"})) as typeof fetch;
  render(<GuardianSponsorSettings/>);const choice=await screen.findByRole("checkbox",{name:"أختار مشاهدة محتوى برعاية داخل التطبيق"});expect(choice).toBeEnabled();fireEvent.click(choice);fireEvent.click(screen.getByRole("button",{name:"حفظ الاختيار"}));await waitFor(()=>expect(body).toEqual({inAppOptIn:false,adultDeclared:true}));
});
it("a revoked creative cannot navigate through a cached CTA",async()=>{vi.stubGlobal("IntersectionObserver",Observer);global.fetch=vi.fn(async input=>String(input).endsWith("/feed")?Response.json({item:ad}):Response.json({},{status:404})) as typeof fetch;render(<GuardianSponsoredCard/>);fireEvent.click(await screen.findByRole("button",{name:"التفاصيل"}));await waitFor(()=>expect(screen.queryByRole("region",{name:"محتوى برعاية"})).not.toBeInTheDocument());expect(push).not.toHaveBeenCalled();});
it("owner creates a clearly unapproved internal-details draft, not a financial or external checkout",async()=>{
  Object.defineProperty(HTMLDialogElement.prototype,"showModal",{configurable:true,value:function(this:HTMLDialogElement){this.open=true;}});
  Object.defineProperty(HTMLDialogElement.prototype,"close",{configurable:true,value:function(this:HTMLDialogElement){this.open=false;}});
  global.fetch=vi.fn(async input=>Response.json(String(input).endsWith("options")?{licensed:true,tenantOptIn:false,branches:[{id:"b",name:"مدينتي"}],measurement:"طلب API ليس مشاهدة",now:"2026-10-17T03:00:00Z"}:{items:[]})) as typeof fetch;
  render(<CommunityCampaigns/>);fireEvent.click(await screen.findByRole("button",{name:"إنشاء مسودة"}));expect(screen.getByRole("dialog",{name:"مسودة المحتوى برعاية"})).toBeVisible();expect(screen.getByText(/لا رابط خارجي أو دفع إعلاني/)).toBeVisible();expect(screen.getByRole("button",{name:"حفظ المسودة"})).toBeDisabled();fireEvent.click(screen.getByRole("checkbox",{name:"مدينتي"}));expect(screen.getByRole("button",{name:"حفظ المسودة"})).toBeEnabled();expect(screen.queryByRole("button",{name:/دفع/})).not.toBeInTheDocument();
});
