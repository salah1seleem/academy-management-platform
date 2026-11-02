"use client";
import {useEffect,useRef,useState} from "react";
import {ErrorState,LoadingState} from "../components/dashboard-shell";
import {announceSaveSuccess,csrfRequest,DashboardRequestError} from "./dashboard-api";
import styles from "./owner-home-settings.module.css";

type Target={from:string;to:string;branchId:string|null;amount:number};
type Active={revision:number;settings:{ownerHome?:{quickActionsAtTop?:boolean;coachAssignmentTarget?:number|null;targets?:Target[]}}};
type Change={path:string;body:object;matches:(version:Active)=>boolean;failure:string;success:string};
async function active(signal?:AbortSignal):Promise<Active>{
  const r=await fetch('/api/v1/manage/configuration',{signal,cache:'no-store'});
  if(!r.ok)throw new DashboardRequestError('تعذر تحميل إعدادات الأكاديمية.',r.status);
  const d=await r.json();const version=d.versions.find((v:{state:string})=>v.state==='Active');
  if(!version)throw new DashboardRequestError('فعّل إعدادات الأكاديمية أولًا.',0);return version;
}
// A lost write response is resolved by reading, never by silently resending it.
function useVerifiedSettings(){
  const [version,setVersion]=useState<Active|null>(null),[busy,setBusy]=useState(false),[pending,setPending]=useState<Change|null>(null),[message,setMessage]=useState(''),[error,setError]=useState('');
  const alive=useRef(true),working=useRef(false);
  useEffect(()=>{alive.current=true;const c=new AbortController();active(c.signal).then(v=>{if(!c.signal.aborted)setVersion(v);}).catch(e=>{if(!c.signal.aborted)setError(e instanceof DashboardRequestError?e.message:'تعذر تحميل إعدادات الأكاديمية.');});return()=>{alive.current=false;c.abort();};},[]);
  function reject(e:unknown){if(!alive.current)return;if(e instanceof DashboardRequestError&&[401,403].includes(e.status))setVersion(null);setError(e instanceof DashboardRequestError?e.message:'تعذر تأكيد الحفظ. أعد التحقق من القيم المحفوظة.');}
  async function verify(change:Change){
    const next=await active();if(!alive.current)return false;
    if(!change.matches(next))throw new DashboardRequestError(change.failure,0);
    setVersion(next);setPending(null);setMessage(change.success);announceSaveSuccess(change.success);return true;
  }
  async function save(change:Change){
    if(working.current||pending||!version)return false;
    working.current=true;setBusy(true);setPending(change);setError('');setMessage('');
    try{
      try{await csrfRequest(change.path,'PUT',change.body,{}, {announce:false});}
      catch(e){if(alive.current&&e instanceof DashboardRequestError&&e.status>=400&&e.status<500&&e.status!==408)setPending(null);throw e;}
      return alive.current?await verify(change):false;
    }catch(e){reject(e);return false;}finally{working.current=false;if(alive.current)setBusy(false);}
  }
  async function recheck(){
    if(working.current||!pending)return false;
    working.current=true;setBusy(true);setError('');setMessage('');
    try{return await verify(pending);}catch(e){reject(e);return false;}finally{working.current=false;if(alive.current)setBusy(false);}
  }
  async function reload(){
    if(working.current)return null;
    working.current=true;setBusy(true);setError('');setMessage('');
    try{const next=await active();if(!alive.current)return null;setVersion(next);setPending(null);return next;}catch(e){reject(e);return null;}finally{working.current=false;if(alive.current)setBusy(false);}
  }
  return {version,busy,pending,message,error,locked:busy||pending!==null,save,recheck,reload};
}
export function OwnerHomePreferences(){
  const settings=useVerifiedSettings();const {version,busy,pending,message,error,locked}=settings;
  const [draft,setDraft]=useState<{top:boolean;target:string}|null>(null);
  const top=true,target=draft?.target??version?.settings.ownerHome?.coachAssignmentTarget?.toString()??'';
  async function save(){if(!version)return;const value=target===''?null:Number(target);if(value!==null&&(!Number.isInteger(value)||value<1||value>100))return;
    if(await settings.save({path:'/api/v1/manage/configuration/owner-home',body:{expectedRevision:version.revision,quickActionsAtTop:top,coachAssignmentTarget:value},matches:next=>(next.settings.ownerHome?.quickActionsAtTop??false)===top&&(next.settings.ownerHome?.coachAssignmentTarget??null)===value,failure:'لم تتطابق إعدادات الرئيسية المقروءة مع القيم المرسلة. أعد التحقق.',success:'تم حفظ إعدادات الرئيسية وهدف الإسناد.'}))setDraft(null);
  }
  return <section className={`detail-card ${styles.preferences}`} id="owner-home-preferences"><h2>المظهر والرئيسية</h2>{error&&<ErrorState message={error}/>}<p role="status">{message}</p>
    {pending&&<p>القيم المرسلة مقفلة حتى التحقق من الحفظ أو إعادة قراءة الوضع الحالي.</p>}
    {(pending||error)&&<div className="form-actions">{pending&&<button disabled={busy} onClick={()=>void settings.recheck().then(ok=>{if(ok)setDraft(null);})}>إعادة التحقق من الحفظ</button>}<button disabled={busy} onClick={()=>void settings.reload().then(ok=>{if(ok)setDraft(null);})}>إعادة تحميل الإعدادات الحالية</button></div>}
    {!version?!error&&<LoadingState/>:<form className="form-grid" onSubmit={e=>{e.preventDefault();void save();}}>
    <p>الإجراءات السريعة ثابتة أعلى الرئيسية، تليها الاشتراكات والتحليلات.</p>
    <label>هدف المجموعات المسندة للمدرب<input type="number" min={1} max={100} step={1} value={target} disabled={locked} onChange={e=>setDraft({top,target:e.target.value})}/><small>اختياري؛ يفعّل قرار متابعة المدرب الأقل إسنادًا.</small></label>
    <button className="primary-button" disabled={locked}>{busy?'جارٍ الحفظ…':'حفظ إعدادات الرئيسية'}</button>
  </form>}</section>;
}
type TargetProps={scope:{from:string;to:string;branchId:string};branchLabel:string;currency:string;amount:number|null;onSaved:()=>void;onClose:()=>void};
export function CollectionTargetDialog(props:TargetProps){return <ScopedTargetDialog key={`${props.scope.from}:${props.scope.to}:${props.scope.branchId}`} {...props}/>;}
function ScopedTargetDialog({scope,branchLabel,currency,amount,onSaved,onClose}:TargetProps){
  const settings=useVerifiedSettings();const {version,busy,pending,error,locked}=settings;
  const [value,setValue]=useState(amount?.toString()??'');const dialog=useRef<HTMLElement>(null);
  useEffect(()=>{const trigger=document.activeElement as HTMLElement|null;dialog.current?.querySelector<HTMLInputElement>('input')?.focus();return()=>trigger?.focus();},[]);
  async function save(){if(!version)return;const amount=value===''?null:Number(value);
    if(await settings.save({path:'/api/v1/manage/configuration/collection-target',body:{...scope,branchId:scope.branchId||null,amount,expectedRevision:version.revision},matches:next=>{const targets=(next.settings.ownerHome?.targets??[]).filter(t=>t.from===scope.from&&t.to===scope.to&&(t.branchId??'')===scope.branchId);return amount===null?targets.length===0:targets.length===1&&targets[0].amount===amount;},failure:'لم يتطابق هدف الفترة المقروء مع الفرع والفترة والقيمة المرسلة. أعد التحقق.',success:'تم حفظ هدف التحصيل.'}))onSaved();
  }
  return <div className="dialog-backdrop" onMouseDown={e=>{if(e.target===e.currentTarget&&!locked)onClose();}}><section ref={dialog} className="confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="collection-target-title" onKeyDown={e=>{
    if(e.key==='Escape'&&!locked)onClose();
    if(e.key==='Tab'){const nodes=dialog.current?.querySelectorAll<HTMLElement>('input:not(:disabled),button:not(:disabled)');if(!nodes?.length)return;const first=nodes[0],last=nodes[nodes.length-1];if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}}
  }}><h2 id="collection-target-title">ضبط هدف التحصيل</h2><p>{branchLabel} · {scope.from} — {scope.to}</p>{error&&<ErrorState message={error}/>}<form onSubmit={e=>{e.preventDefault();void save();}}><label>هدف الفترة ({currency==='EGP'?'ج.م':currency})<input type="number" min="0.01" max="1000000000" step="0.01" value={value} onChange={e=>setValue(e.target.value)} disabled={locked}/></label><small>اتركه فارغًا لإزالة هدف هذه الفترة.</small><div className="form-actions"><button className="primary-button" disabled={locked||!version}>{busy?'جارٍ الحفظ…':'حفظ الهدف'}</button><button type="button" className="secondary-button" disabled={locked} onClick={onClose}>إلغاء</button></div></form>
    {pending&&<p>القيمة والفرع والفترة مقفلة حتى التحقق من الحفظ أو إعادة القراءة.</p>}
    {(pending||error)&&<div className="form-actions">{pending&&<button disabled={busy} onClick={()=>void settings.recheck().then(ok=>{if(ok)onSaved();})}>إعادة التحقق من الحفظ</button>}<button disabled={busy} onClick={()=>void settings.reload().then(ok=>{if(ok){const t=ok.settings.ownerHome?.targets?.find(t=>t.from===scope.from&&t.to===scope.to&&(t.branchId??'')===scope.branchId);setValue(t?.amount.toString()??'');}})}>إعادة تحميل الإعدادات الحالية</button></div>}
  </section></div>;
}
