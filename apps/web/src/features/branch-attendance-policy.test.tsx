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
it('does not confirm a successful PUT when readback has different policy, and rechecks with GET only',async()=>{
 let wrote=false,matching=false;
 global.fetch=vi.fn((url,o)=>String(url).includes('csrf')?response({token:'t'}):o?.method==='PUT'?(wrote=true,response({version:8})):response(wrote?{...site,version:8,policy:{...policy,radiusMeters:matching?80:50}}:site)) as typeof fetch;
 render(<BranchCheckInSite id="branch"/>);await screen.findByLabelText('نصف القطر بالمتر');fireEvent.change(screen.getByLabelText('نصف القطر بالمتر'),{target:{value:'80'}});fireEvent.click(screen.getByRole('button',{name:'حفظ سياسة الحضور'}));
 const recheck=await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'});
 expect(screen.queryByRole('status')).not.toBeInTheDocument();expect(screen.getByLabelText('نصف القطر بالمتر')).toHaveValue(80);expect(screen.getByRole('button',{name:'حفظ سياسة الحضور'})).toBeDisabled();
 matching=true;fireEvent.click(recheck);expect(await screen.findByText('تم حفظ سياسة حضور الفرع وتأكيد قراءتها.')).toBeVisible();expect(vi.mocked(fetch).mock.calls.filter(([,o])=>o?.method==='PUT')).toHaveLength(1);
});
it('locks an uncertain PUT, hides technical transport text and verifies without replay',async()=>{
 let saved=false;
 global.fetch=vi.fn((url,o)=>String(url).includes('csrf')?response({token:'t'}):o?.method==='PUT'?(saved=true,Promise.reject(new Error('ECONNRESET secret-internal-host'))):response(saved?{...site,version:8,policy:{...policy,radiusMeters:80}}:site)) as typeof fetch;
 render(<BranchCheckInSite id="branch"/>);await screen.findByLabelText('نصف القطر بالمتر');fireEvent.change(screen.getByLabelText('نصف القطر بالمتر'),{target:{value:'80'}});fireEvent.click(screen.getByRole('button',{name:'حفظ سياسة الحضور'}));
 const recheck=await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'});expect(screen.queryByText(/ECONNRESET/)).not.toBeInTheDocument();expect(screen.getByRole('button',{name:'حفظ سياسة الحضور'})).toBeDisabled();fireEvent.click(recheck);
 expect(await screen.findByText('تم حفظ سياسة حضور الفرع وتأكيد قراءتها.')).toBeVisible();expect(vi.mocked(fetch).mock.calls.filter(([,o])=>o?.method==='PUT')).toHaveLength(1);
});
it.each<[string, unknown]>([
 ['checkInLatitude',31],['checkInLongitude',32],['version',7],
 ...Object.entries(policy).map(([key,value]):[string,unknown]=>[key,typeof value==='boolean'?!value:typeof value==='number'?value+1:'different-reviewer']),
])('requires matching saved branch field %s before announcing success',async(key,value)=>{
 let saved=false;
 const confirmed={...site,version:8,policy:{...policy,radiusMeters:80}};
 const wrong=key in policy?{...confirmed,policy:{...confirmed.policy,[key]:key==='radiusMeters'?50:value}}:{...confirmed,[key]:value};
 global.fetch=vi.fn((url,o)=>String(url).includes('csrf')?response({token:'t'}):o?.method==='PUT'?(saved=true,response({version:8})):response(saved?wrong:site)) as typeof fetch;
 render(<BranchCheckInSite id="branch"/>);await screen.findByLabelText('نصف القطر بالمتر');fireEvent.change(screen.getByLabelText('نصف القطر بالمتر'),{target:{value:'80'}});fireEvent.click(screen.getByRole('button',{name:'حفظ سياسة الحضور'}));
 expect(await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'})).toBeVisible();expect(screen.queryByRole('status')).not.toBeInTheDocument();expect(screen.getByRole('button',{name:'حفظ سياسة الحضور'})).toBeDisabled();
});
it('hides settings when authorization is lost during readback and never reports success',async()=>{
 let saved=false;global.fetch=vi.fn((url,o)=>String(url).includes('csrf')?response({token:'t'}):o?.method==='PUT'?(saved=true,response({version:8})):response(saved?{}:site,saved?403:200)) as typeof fetch;
 render(<BranchCheckInSite id="branch"/>);await screen.findByLabelText('نصف القطر بالمتر');fireEvent.click(screen.getByRole('button',{name:'حفظ سياسة الحضور'}));await screen.findByRole('alert');expect(screen.queryByRole('button',{name:'حفظ سياسة الحضور'})).not.toBeInTheDocument();expect(screen.queryByRole('status')).not.toBeInTheDocument();
});
it('explicit reload abandons an uncertain draft and restores current values without another PUT',async()=>{
 global.fetch=vi.fn((url,o)=>String(url).includes('csrf')?response({token:'t'}):o?.method==='PUT'?Promise.reject(new Error('network')):response(site)) as typeof fetch;
 render(<BranchCheckInSite id="branch"/>);await screen.findByLabelText('نصف القطر بالمتر');fireEvent.change(screen.getByLabelText('نصف القطر بالمتر'),{target:{value:'80'}});fireEvent.click(screen.getByRole('button',{name:'حفظ سياسة الحضور'}));await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'});fireEvent.click(screen.getByRole('button',{name:'إعادة تحميل سياسة الفرع'}));
 await vi.waitFor(()=>expect(screen.getByRole('button',{name:'حفظ سياسة الحضور'})).not.toBeDisabled());expect(screen.getByLabelText('نصف القطر بالمتر')).toHaveValue(50);expect(screen.queryByRole('status')).not.toBeInTheDocument();expect(vi.mocked(fetch).mock.calls.filter(([,o])=>o?.method==='PUT')).toHaveLength(1);
});
it('a server failure with technical text stays uncertain and does not expose internals',async()=>{
 global.fetch=vi.fn((url,o)=>String(url).includes('csrf')?response({token:'t'}):o?.method==='PUT'?response({message:'SQL host internal-secret'},500):response(site)) as typeof fetch;
 render(<BranchCheckInSite id="branch"/>);await screen.findByLabelText('نصف القطر بالمتر');fireEvent.click(screen.getByRole('button',{name:'حفظ سياسة الحضور'}));await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'});expect(screen.queryByText(/internal-secret/)).not.toBeInTheDocument();expect(screen.getByRole('button',{name:'حفظ سياسة الحضور'})).toBeDisabled();
});
it('a late saved branch response cannot replace the newly selected branch',async()=>{
 let wrote=false;let release:(r:Response)=>void=()=>{};
 global.fetch=vi.fn((url,o)=>String(url).includes('csrf')?response({token:'t'}):o?.method==='PUT'?(wrote=true,response({version:8})):String(url).includes('/second/')?response({...site,policy:{...policy,radiusMeters:99}}):wrote?new Promise<Response>(resolve=>{release=resolve;}):response(site)) as typeof fetch;
 const view=render(<BranchCheckInSite id="branch"/>);await screen.findByLabelText('نصف القطر بالمتر');fireEvent.click(screen.getByRole('button',{name:'حفظ سياسة الحضور'}));await vi.waitFor(()=>expect(vi.mocked(fetch).mock.calls.filter(([u])=>String(u).includes('/branch/check-in-site'))).toHaveLength(3));view.rerender(<BranchCheckInSite id="second"/>);
 await screen.findByLabelText('نصف القطر بالمتر');release(new Response(JSON.stringify({...site,version:8})));await vi.waitFor(()=>expect(screen.getByLabelText('نصف القطر بالمتر')).toHaveValue(99));expect(screen.queryByRole('status')).not.toBeInTheDocument();
});
