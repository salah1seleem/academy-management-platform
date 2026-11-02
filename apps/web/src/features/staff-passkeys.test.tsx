import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {StaffPasskeySettings} from "./staff-passkeys";
const response=(data:unknown,status=200)=>Promise.resolve(new Response(JSON.stringify(data),{status}));
const key={id:"key",name:"هاتف العرض",isActive:true,createdAtUtc:"2026-10-16T10:00:00Z",mayBeSynced:true};
afterEach(()=>{cleanup();vi.restoreAllMocks();vi.unstubAllGlobals();});
function supported(){vi.stubGlobal("isSecureContext",true);vi.stubGlobal("PublicKeyCredential",class{});}
it("shows disabled and unsupported verification explicitly without recording attendance or requesting biometrics",async()=>{
  global.fetch=vi.fn(()=>response({configured:false,items:[]})) as typeof fetch;render(<StaffPasskeySettings/>);expect(await screen.findByText(/يلزم إعداد عنوان الويب الآمن/)).toBeVisible();expect(screen.queryByRole("button",{name:"إضافة وسيلة تحقق"})).not.toBeInTheDocument();expect(vi.mocked(fetch).mock.calls.every(([,o])=>!o?.method)).toBe(true);
});
it("registers browser bytes with UV options then rereads server credentials; never submits a biometric Boolean",async()=>{
  supported();let saved=false;const create=vi.fn(async(options:CredentialCreationOptions)=>{expect(options.publicKey).toBeTruthy();return {id:"AQ",type:"public-key",rawId:new Uint8Array([1]).buffer,authenticatorAttachment:"platform",getClientExtensionResults:()=>({}),response:{clientDataJSON:new Uint8Array([2]).buffer,attestationObject:new Uint8Array([3]).buffer,getTransports:()=>["internal"]}};});
  vi.stubGlobal("navigator",{credentials:{create}});
  global.fetch=vi.fn((p)=>String(p).includes("csrf")?response({token:"test"}):String(p).endsWith("/register/options")?response({challengeId:"challenge",options:{challenge:"AQ",rp:{id:"localhost",name:"عرض"},user:{id:"Ag",name:"عرض",displayName:"عرض"},pubKeyCredParams:[{type:"public-key",alg:-7}],authenticatorSelection:{userVerification:"required"}}}):String(p).endsWith("/register/complete")?(saved=true,response({verified:true,purpose:"Register",credential:key,attendanceRecorded:false})):response({configured:true,items:saved?[key]:[]})) as typeof fetch;
  render(<StaffPasskeySettings/>);fireEvent.click(await screen.findByRole("button",{name:"إضافة وسيلة تحقق"}));expect(await screen.findByText("تم تسجيل وسيلة التحقق على الخادم. لم يسجل أي حضور.")).toBeVisible();expect(screen.getByText(key.name)).toBeVisible();
  expect(create.mock.calls[0][0]).toMatchObject({publicKey:{authenticatorSelection:{userVerification:"required"}}});
  const complete=vi.mocked(fetch).mock.calls.find(([p])=>String(p).endsWith("/register/complete"));expect(JSON.parse(String(complete![1]?.body))).toMatchObject({challengeId:"challenge",credential:{rawId:"AQ",response:{attestationObject:"Aw"}}});expect(String(complete![1]?.body)).not.toContain("biometric");
});
it("revokes only the selected credential after confirmation and retains its stopped record",async()=>{
  let stopped=false;global.fetch=vi.fn((p,o)=>String(p).includes("csrf")?response({token:"test"}):o?.method==="POST"?(stopped=true,response({...key,isActive:false})):response({configured:false,items:[{...key,isActive:!stopped}]})) as typeof fetch;
  render(<StaffPasskeySettings/>);fireEvent.click(await screen.findByRole("button",{name:"إيقاف هاتف العرض"}));expect(vi.mocked(fetch).mock.calls.every(([,o])=>!o?.method)).toBe(true);fireEvent.click(screen.getByRole("button",{name:"تأكيد الإيقاف"}));expect(await screen.findByText("متوقفة")).toBeVisible();expect(screen.getByText(key.name)).toBeVisible();expect(vi.mocked(fetch).mock.calls.find(([,o])=>o?.method==="POST")![0]).toBe("/api/v1/staff/passkeys/credentials/key/revoke");
});
it("a cancelled browser ceremony stays Arabic and never completes verification",async()=>{
  supported();vi.stubGlobal("navigator",{credentials:{create:vi.fn(()=>Promise.reject(new DOMException("Denied","NotAllowedError")))}});
  global.fetch=vi.fn((p)=>String(p).includes("csrf")?response({token:"test"}):String(p).endsWith("/options")?response({challengeId:"c",options:{challenge:"AQ",user:{id:"AQ"}}}):response({configured:true,items:[]})) as typeof fetch;
  render(<StaffPasskeySettings/>);fireEvent.click(await screen.findByRole("button",{name:"إضافة وسيلة تحقق"}));expect(await screen.findByText(/تعذر تأكيد التحقق/)).toBeVisible();await waitFor(()=>expect(vi.mocked(fetch).mock.calls.some(([p])=>String(p).endsWith("/complete"))).toBe(false));expect(screen.queryByText("Denied")).not.toBeInTheDocument();
});
