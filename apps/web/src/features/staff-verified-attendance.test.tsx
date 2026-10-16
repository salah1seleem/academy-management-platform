import { cleanup,fireEvent,render,screen } from "@testing-library/react";
import { afterEach,expect,it,vi } from "vitest";
import { StaffVerifiedAttendance } from "./staff-verified-attendance";
vi.mock("./staff-passkeys",()=>({browserPasskey:vi.fn(async()=>({rawId:"proof",response:{signature:"signed"}}))}));
const state={configured:true,hasCredential:true,hasOpenAttendance:false,requiresSourceReview:false,branches:[{id:"site",name:"فرع عرض اصطناعي",configured:true}],recent:[]};
const response=(x:unknown,status=200)=>Promise.resolve(new Response(JSON.stringify(x),{status}));
afterEach(()=>{cleanup();vi.restoreAllMocks();vi.unstubAllGlobals();});
function location(fail=false){vi.stubGlobal("isSecureContext",true);vi.stubGlobal("navigator",{geolocation:{getCurrentPosition:vi.fn((ok:(v:unknown)=>void,bad:()=>void)=>fail?bad():ok({timestamp:Date.now(),coords:{latitude:30.05,longitude:31.3,accuracy:5}}))}});}
it("confirms server attendance only after location, operation-bound options and signed complete; no client biometric boolean",async()=>{
  location();let done=false;global.fetch=vi.fn(p=>String(p).includes("csrf")?response({token:"t"}):String(p).endsWith("/options")?response({purpose:"CheckIn",challengeId:"one",options:{challenge:"AQ"}}):String(p).endsWith("/complete")?(done=true,response({attendanceRecorded:true,attendanceId:"day",workPeriodId:"period",operation:"CheckIn"})):response({...state,hasOpenAttendance:done})) as typeof fetch;
  render(<StaffVerifiedAttendance/>);await screen.findByRole("option",{name:"فرع عرض اصطناعي"});fireEvent.change(screen.getByLabelText("فرع الحضور"),{target:{value:"site"}});fireEvent.click(screen.getByRole("button",{name:"تسجيل حضوري"}));expect(await screen.findByText("تم تسجيل حضورك بوقت الخادم المؤكد.")).toBeVisible();
  const call=vi.mocked(fetch).mock.calls.find(([p])=>String(p).endsWith("/complete"))!;expect(JSON.parse(String(call[1]?.body))).toMatchObject({challengeId:"one",intent:{branchId:"site",operation:"CheckIn",accuracyMeters:5},credential:{rawId:"proof"}});expect(String(call[1]?.body)).not.toContain("biometric");expect(screen.getByRole("button",{name:"تسجيل حضوري"})).toBeDisabled();
});
it("location denial never submits options or claims success",async()=>{
  location(true);global.fetch=vi.fn(()=>response(state)) as typeof fetch;render(<StaffVerifiedAttendance/>);await screen.findByRole("option",{name:"فرع عرض اصطناعي"});fireEvent.change(screen.getByLabelText("فرع الحضور"),{target:{value:"site"}});fireEvent.click(screen.getByRole("button",{name:"تسجيل حضوري"}));expect(await screen.findByText(/تعذر تأكيد التوقيع أو الموقع/)).toBeVisible();expect(vi.mocked(fetch).mock.calls.every(([,o])=>!o?.method)).toBe(true);
});
it("a lost reply retains the exact signed command and retries it, not a new nonce or location",async()=>{
  location();let failed=true;global.fetch=vi.fn(p=>String(p).includes("csrf")?response({token:"t"}):String(p).endsWith("/options")?response({purpose:"CheckIn",challengeId:"same",options:{}}):String(p).endsWith("/complete")?(failed?(failed=false,Promise.reject(new Error("network"))):response({attendanceRecorded:true,attendanceId:"day",workPeriodId:"period",operation:"CheckIn"})):response(state)) as typeof fetch;
  render(<StaffVerifiedAttendance/>);await screen.findByRole("option",{name:"فرع عرض اصطناعي"});fireEvent.change(screen.getByLabelText("فرع الحضور"),{target:{value:"site"}});fireEvent.click(screen.getByRole("button",{name:"تسجيل حضوري"}));fireEvent.click(await screen.findByRole("button",{name:"إعادة إرسال الطلب نفسه"}));expect(await screen.findByText("تم تسجيل حضورك بوقت الخادم المؤكد.")).toBeVisible();const calls=vi.mocked(fetch).mock.calls.filter(([p])=>String(p).endsWith("/complete"));expect(calls).toHaveLength(2);expect(calls[0][1]?.body).toBe(calls[1][1]?.body);
});
