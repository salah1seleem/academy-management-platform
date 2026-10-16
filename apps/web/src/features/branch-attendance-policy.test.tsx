import {cleanup,fireEvent,render,screen} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {BranchCheckInSite} from "./branch-attendance-policy";
const policy={enabled:true,radiusMeters:50,maxAccuracyMeters:30,maxAgeSeconds:30,challengeSeconds:120,coachRequired:true,adminRequired:false,ownerRequired:false,reviewerMembershipId:null};
const site={version:7,checkInLatitude:30.05,checkInLongitude:31.3,policy,reviewers:[{id:"reviewer",name:"مسؤول اصطناعي"}],history:[]};
const response=(x:unknown,status=200)=>Promise.resolve(new Response(JSON.stringify(x),{status}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it("saves the branch policy and source version then rereads the confirmed values",async()=>{
  let updated=false;global.fetch=vi.fn((url,options)=>String(url).includes("csrf")?response({token:"t"}):options?.method==="PUT"?(updated=true,response({version:8})):response(updated?{...site,version:8,policy:{...policy,radiusMeters:80,enabled:false,reviewerMembershipId:"reviewer"}}:site)) as typeof fetch;
  render(<BranchCheckInSite id="branch"/>);await screen.findByLabelText("نصف القطر بالمتر");fireEvent.change(screen.getByLabelText("نصف القطر بالمتر"),{target:{value:"80"}});fireEvent.click(screen.getByLabelText("تفعيل التسجيل الذاتي الآمن لهذا الفرع"));fireEvent.change(screen.getByLabelText("مسؤول الاستثناءات"),{target:{value:"reviewer"}});fireEvent.click(screen.getByRole("button",{name:"حفظ سياسة الحضور"}));expect(await screen.findByText("تم حفظ سياسة حضور الفرع وتأكيد قراءتها.")).toBeVisible();
  const call=vi.mocked(fetch).mock.calls.find(([,o])=>o?.method==="PUT")!;expect(JSON.parse(String(call[1]?.body))).toEqual({latitude:30.05,longitude:31.3,version:7,policy:{...policy,radiusMeters:80,enabled:false,reviewerMembershipId:"reviewer"}});expect(screen.getByLabelText("نصف القطر بالمتر")).toHaveValue(80);expect(screen.getByLabelText("تفعيل التسجيل الذاتي الآمن لهذا الفرع")).not.toBeChecked();
});
it("does not report a stale save as success or drop the user's entered fields",async()=>{
  global.fetch=vi.fn((url,o)=>String(url).includes("csrf")?response({token:"t"}):o?.method==="PUT"?response({message:"تغيرت سياسة الفرع؛ حدّث الصفحة."},409):response(site)) as typeof fetch;render(<BranchCheckInSite id="branch"/>);await screen.findByLabelText("نصف القطر بالمتر");fireEvent.change(screen.getByLabelText("نصف القطر بالمتر"),{target:{value:"70"}});fireEvent.click(screen.getByRole("button",{name:"حفظ سياسة الحضور"}));expect(await screen.findByText("تغيرت سياسة الفرع؛ حدّث الصفحة.")).toBeVisible();expect(screen.queryByRole("status")).not.toBeInTheDocument();expect(screen.getByLabelText("نصف القطر بالمتر")).toHaveValue(70);
});
it("does not show owner settings to an unauthorized account",async()=>{global.fetch=vi.fn(()=>response({},403)) as typeof fetch;render(<BranchCheckInSite id="branch"/>);await Promise.resolve();expect(screen.queryByRole("button",{name:"حفظ سياسة الحضور"})).not.toBeInTheDocument();});
