import {afterEach,expect,it,vi} from "vitest";
import {cleanup,fireEvent,render,screen} from "@testing-library/react";
import {PlatformLauncherIcons} from "./platform-launcher-icons";
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it("shows awaiting artwork honestly and saves explicit versioned platform entitlement without a payment",async()=>{
 const saved=vi.fn();global.fetch=vi.fn(async(url,options)=>new Response(JSON.stringify(String(url).endsWith("/csrf")?{token:"csrf"}:options?.method==="PUT"?{version:4}:[{key:"professional-demo",name:"رسم عرض",minimumVersion:"0.1.0+1",platforms:["ios","android"]}]),{status:200}));
 render(<PlatformLauncherIcons academyId="academy" version={3} enabled={false} iconKey={null} onSaved={saved}/>);
 await screen.findByRole("option",{name:/رسم عرض/});
 fireEvent.click(screen.getByRole("checkbox"));expect(screen.getByRole("status")).toHaveTextContent("ينتظر إضافته");
 fireEvent.change(screen.getByLabelText("الرسم المضمّن"),{target:{value:"professional-demo"}});
 fireEvent.click(screen.getByRole("button",{name:"حفظ استحقاق الأيقونة"}));
 await vi.waitFor(()=>expect(saved).toHaveBeenCalledOnce());
 const call=vi.mocked(fetch).mock.calls.find(c=>c[1]?.method==="PUT")!;expect(call[0]).toBe("/api/v1/platform/academies/academy/launcher-icon");expect(JSON.parse(String(call[1]?.body))).toEqual({version:3,enabled:true,key:"professional-demo"});expect(vi.mocked(fetch).mock.calls.some(c=>/payment|billing|checkout/.test(String(c[0])))).toBe(false);
});
it("keeps a rejected save actionable without reporting completion",async()=>{
 const saved=vi.fn();global.fetch=vi.fn(async(url,options)=>new Response(JSON.stringify(String(url).endsWith("/csrf")?{token:"csrf"}:options?.method==="PUT"?{title:"حدّث بيانات التجهيز قبل حفظ الأيقونة."}:[]),{status:options?.method==="PUT"?409:200}));
 render(<PlatformLauncherIcons academyId="academy" version={3} enabled={true} iconKey={null} onSaved={saved}/>);fireEvent.click(screen.getByRole("button",{name:"حفظ استحقاق الأيقونة"}));expect(await screen.findByRole("alert")).toHaveTextContent("حدّث بيانات التجهيز");expect(saved).not.toHaveBeenCalled();
});
