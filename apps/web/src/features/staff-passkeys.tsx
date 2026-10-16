"use client";
import { FormEvent, useCallback, useEffect, useState, useSyncExternalStore } from "react";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";

type Key = { id: string; name: string; isActive: boolean; createdAtUtc: string; lastVerifiedAtUtc?: string; revokedAtUtc?: string; mayBeSynced: boolean };
type List = { configured: boolean; items: Key[] };
type CreateJson = Omit<PublicKeyCredentialCreationOptions,"challenge"|"user"|"excludeCredentials"> & { challenge: string; user: Omit<PublicKeyCredentialUserEntity,"id"> & {id: string}; excludeCredentials?: Array<Omit<PublicKeyCredentialDescriptor,"id"> & {id:string}> };
type GetJson = Omit<PublicKeyCredentialRequestOptions,"challenge"|"allowCredentials"> & {challenge:string;allowCredentials?:Array<Omit<PublicKeyCredentialDescriptor,"id"> & {id:string}>};
const root="/api/v1/staff/passkeys";
export function decodePasskeyBytes(value:string):ArrayBuffer { return Uint8Array.from(atob(value.replace(/-/g,"+").replace(/_/g,"/")),c=>c.charCodeAt(0)).buffer; }
function encoded(value:ArrayBuffer):string { return btoa(String.fromCharCode(...new Uint8Array(value))).replace(/\+/g,"-").replace(/\//g,"_").replace(/=+$/g,""); }
export async function browserPasskey(purpose:"register"|"verify",options:CreateJson|GetJson):Promise<unknown> {
  let credential:PublicKeyCredential|null;
  if(purpose==="register") { const o=options as CreateJson;credential=await navigator.credentials.create({publicKey:{...o,challenge:decodePasskeyBytes(o.challenge),user:{...o.user,id:decodePasskeyBytes(o.user.id)},excludeCredentials:o.excludeCredentials?.map(k=>({...k,id:decodePasskeyBytes(k.id)}))}}) as PublicKeyCredential|null; }
  else {const o=options as GetJson;credential=await navigator.credentials.get({publicKey:{...o,challenge:decodePasskeyBytes(o.challenge),allowCredentials:o.allowCredentials?.map(k=>({...k,id:decodePasskeyBytes(k.id)}))}}) as PublicKeyCredential|null;}
  if(!credential)throw new Error("لم تُؤكد وسيلة التحقق؛ لم يسجل أي حضور.");
  const common={id:credential.id,rawId:encoded(credential.rawId),type:credential.type,extensions:credential.getClientExtensionResults(),authenticatorAttachment:credential.authenticatorAttachment};
  if(purpose==="register"){const r=credential.response as AuthenticatorAttestationResponse;return {...common,response:{clientDataJSON:encoded(r.clientDataJSON),attestationObject:encoded(r.attestationObject),transports:r.getTransports?.()??[]}};}
  const r=credential.response as AuthenticatorAssertionResponse;return {...common,response:{clientDataJSON:encoded(r.clientDataJSON),authenticatorData:encoded(r.authenticatorData),signature:encoded(r.signature),userHandle:r.userHandle?encoded(r.userHandle):null}};
}
export function StaffPasskeySettings() {
  const [data,setData]=useState<List|null>(null),[name,setName]=useState("وسيلة التحقق الخاصة بي"),[busy,setBusy]=useState(false),[error,setError]=useState(""),[message,setMessage]=useState(""),[revoke,setRevoke]=useState<Key|null>(null);
  const supported=useSyncExternalStore(()=>()=>{},()=>window.isSecureContext&&typeof PublicKeyCredential!=="undefined"&&!!navigator.credentials,()=>false);
  const load=useCallback(async()=>{const r=await fetch(root+"/",{cache:"no-store"});if(!r.ok){setData(null);throw new Error("تعذر عرض وسائل التحقق الخاصة بحسابك.");}setData(await r.json());},[]);
  useEffect(()=>{const abort=new AbortController();fetch(root+"/",{signal:abort.signal,cache:"no-store"}).then(async r=>{if(!r.ok)throw new Error();const d=await r.json();if(!abort.signal.aborted)setData(d);}).catch(()=>{if(!abort.signal.aborted)setError("تعذر عرض وسائل التحقق الخاصة بحسابك.");});return()=>abort.abort();},[]);
  async function ceremony(purpose:"register"|"verify",e?:FormEvent){e?.preventDefault();if(busy||!data?.configured||!supported)return;setBusy(true);setError("");setMessage("");
    try{const options=await(await csrfRequest(`${root}/${purpose}/options`,"POST",purpose==="register"?{name:name.trim()}:{},{},{announce:false})).json();const credential=await browserPasskey(purpose,options.options);const saved=await(await csrfRequest(`${root}/${purpose}/complete`,"POST",{challengeId:options.challengeId,credential},{},{announce:false})).json();if(saved.verified!==true||saved.attendanceRecorded!==false||String(saved.purpose).toLowerCase()!==purpose||!saved.credential?.id)throw new Error("لم يتأكد التحقق المتوقع؛ حدّث القائمة.");await load();setMessage(purpose==="register"?"تم تسجيل وسيلة التحقق على الخادم. لم يسجل أي حضور.":"تم التحقق من التوقيع وهوية المستخدم على الخادم. لم يسجل أي حضور.");}
    catch{setError("تعذر تأكيد التحقق. حدّث القائمة قبل محاولة جديدة؛ يمكنك طلب إثبات الحضور للمراجعة بدلًا منه.");}finally{setBusy(false);}}
  async function stop(){if(!revoke||busy)return;setBusy(true);setError("");setMessage("");try{const saved=await(await csrfRequest(`${root}/credentials/${revoke.id}/revoke`,"POST",{},{},{announce:false})).json();if(saved.id!==revoke.id||saved.isActive!==false)throw new Error("لم يتأكد الإيقاف.");await load();setRevoke(null);setMessage("تم إيقاف وسيلة التحقق؛ لا يمكن استخدامها مجددًا.");}catch{setError("تعذر تأكيد الإيقاف؛ حدّث القائمة لمعرفة الحالة المحفوظة.");}finally{setBusy(false);}}
  return <><PageHeader title="وسائل التحقق الخاصة بي" context="أمان الحساب" action={{label:"عودة للرئيسية",href:"/dashboard"}}/>
    <section className="detail-card staff-passkey-settings"><h2>تحقق آمن، دون مشاركة بصمتك</h2><p>قد تستخدم وسيلة التحقق بصمة أو وجهًا أو رمز الجهاز، وقد تتزامن بين أجهزة. لا نخزن بصمات أو صور وجوه، ولا نضمن أنها هاتف مادي وحيد.</p><p>هذه الصفحة تسجل الوسيلة وتجربها فقط؛ لا تسجل حضورًا ولا تمنح صلاحية حضور اللاعبين أو الرواتب.</p>
      {error&&<ErrorState message={error}/>} {message&&<p role="status">{message}</p>}{!data&&!error&&<LoadingState/>}
      <button type="button" disabled={busy} onClick={()=>{setError("");void load().catch(()=>setError("تعذر عرض الوسائل المحفوظة؛ حاول مجددًا."));}}>تحديث الوسائل</button>
      {data&&!data.configured&&<p>يلزم إعداد عنوان الويب الآمن من المسؤول. طلب إثبات الحضور للمراجعة ما زال متاحًا من تطبيق الموظف.</p>}
      {data?.configured&&!supported&&<p>هذا المتصفح أو الاتصال لا يدعم وسيلة التحقق المطلوبة. استخدم اتصالًا آمنًا أو مسار المراجعة؛ لن نقبل تأكيدًا محليًا بدل التوقيع.</p>}
      {data?.configured&&supported&&<form className="form-shell" onSubmit={e=>void ceremony("register",e)}><label>اسم وسيلة التحقق<input value={name} onChange={e=>setName(e.target.value)} required minLength={2} maxLength={80} disabled={busy}/></label><div className="form-actions"><button className="primary-button" disabled={busy}>إضافة وسيلة تحقق</button><button type="button" disabled={busy||!data.items.some(k=>k.isActive)} onClick={()=>void ceremony("verify")}>تجربة التحقق الآمن</button></div></form>}
      {data&&<section aria-label="وسائل التحقق المحفوظة"><h3>وسائلي المحفوظة</h3>{!data.items.length&&<p>لا توجد وسائل تحقق محفوظة لهذا الحساب.</p>}<ul className="staff-passkey-list">{data.items.map(k=><li key={k.id}><strong>{k.name}</strong><span>{k.isActive?"فعالة":"متوقفة"}</span><small>سُجلت {new Date(k.createdAtUtc).toLocaleDateString("ar-EG-u-nu-latn")}{k.lastVerifiedAtUtc&&` · آخر تحقق ${new Date(k.lastVerifiedAtUtc).toLocaleString("ar-EG-u-nu-latn")}`}</small>{k.mayBeSynced&&<small>قد تكون متزامنة بين أجهزة.</small>}{k.isActive&&<button type="button" disabled={busy} onClick={()=>setRevoke(k)}>إيقاف {k.name}</button>}</li>)}</ul></section>}
      {revoke&&<section role="dialog" aria-label="إيقاف وسيلة التحقق" aria-modal="false"><h3>إيقاف {revoke.name}؟</h3><p>ستبقى في السجل، ولا يمكن إعادة تفعيل المفتاح نفسه.</p><button className="primary-button" disabled={busy} onClick={()=>void stop()}>تأكيد الإيقاف</button><button disabled={busy} onClick={()=>setRevoke(null)}>رجوع</button></section>}
    </section></>;
}
