"use client";
import {useEffect,useRef,useState} from "react";
import {formatDateAr,formatMoneyAr} from "../lib/formatters";
import {ErrorState,LoadingState} from "../components/dashboard-shell";
import {announceSaveSuccess,csrfRequest,DashboardRequestError} from "./dashboard-api";
import styles from "./owner-home-settings.module.css";

type Target={from:string;to:string;branchId:string|null;amount:number};
type Active={revision:number;settings:{monthlyCollectionTarget?:number|null;ownerHome?:{quickActionsAtTop?:boolean;coachAssignmentTarget?:number|null;targets?:Target[]}}};
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
  const [stale,setStale]=useState(false);
  const alive=useRef(true),working=useRef(false);
  useEffect(()=>{alive.current=true;const c=new AbortController();active(c.signal).then(v=>{if(!c.signal.aborted)setVersion(v);}).catch(e=>{if(!c.signal.aborted)setError(e instanceof DashboardRequestError?e.message:'تعذر تحميل إعدادات الأكاديمية.');});return()=>{alive.current=false;c.abort();};},[]);
  function reject(e:unknown){if(!alive.current)return;if(e instanceof DashboardRequestError&&[401,403].includes(e.status)){setVersion(null);setPending(null);}setError(e instanceof DashboardRequestError?e.message:'تعذر تأكيد الحفظ. أعد التحقق من القيم المحفوظة.');}
  async function verify(change:Change){
    const next=await active();if(!alive.current)return false;
    if(!change.matches(next))throw new DashboardRequestError(change.failure,0);
    setVersion(next);setPending(null);setMessage(change.success);announceSaveSuccess(change.success);return true;
  }
  async function save(change:Change){
    if(working.current||pending||stale||!version)return false;
    working.current=true;setBusy(true);setPending(change);setError('');setMessage('');
    try{
      try{await csrfRequest(change.path,'PUT',change.body,{}, {announce:false});}
      catch(e){
        if(alive.current&&e instanceof DashboardRequestError&&e.status>=400&&e.status<500&&e.status!==408){
          setPending(null);
          if(e.status===409)setStale(true);
        }
        throw e;
      }
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
    try{const next=await active();if(!alive.current)return null;setVersion(next);setPending(null);setStale(false);return next;}catch(e){reject(e);return null;}finally{working.current=false;if(alive.current)setBusy(false);}
  }
  return {version,busy,pending,stale,message,error,locked:busy||pending!==null||stale,save,recheck,reload};
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
type TargetProps={
  scope:{from:string;to:string;branchId:string};
  branchLabel:string;
  currency:string;
  usesMonthlyDefault?:boolean;
  onSaved:()=>void;
  onClose:(unconfirmed:boolean)=>void;
};
export function CollectionTargetDialog(props:TargetProps){
  return <ScopedTargetDialog key={`${props.scope.from}:${props.scope.to}:${props.scope.branchId}`} {...props}/>;
}
function ScopedTargetDialog({scope,branchLabel,currency,usesMonthlyDefault,onSaved,onClose}:TargetProps){
  const settings=useVerifiedSettings();
  const {version,busy,pending,stale,error,locked}=settings;
  const [draft,setDraft]=useState<string|null>(null);
  const [validation,setValidation]=useState('');
  const [removing,setRemoving]=useState(false);
  const dialog=useRef<HTMLElement>(null);
  const input=useRef<HTMLInputElement>(null);
  const recovery=useRef<HTMLButtonElement>(null);
  const closeButton=useRef<HTMLButtonElement>(null);
  const matchesScope=(t:Target)=>t.from===scope.from&&t.to===scope.to&&(t.branchId??'')===scope.branchId;
  const stored=version?.settings.ownerHome?.targets?.find(matchesScope);
  const value=draft??stored?.amount.toString()??'';
  const monthly=usesMonthlyDefault ? version?.settings.monthlyCollectionTarget : null;
  useEffect(()=>{
    const trigger=document.activeElement as HTMLElement|null;
    dialog.current?.focus();
    function containFocus(event: FocusEvent) {
      if(event.target instanceof Node && !dialog.current?.contains(event.target)) dialog.current?.focus();
    }
    document.addEventListener('focusin',containFocus);
    return()=>{
      document.removeEventListener('focusin',containFocus);
      if(trigger?.isConnected)trigger.focus();
    };
  },[]);
  useEffect(()=>{
    if(busy)dialog.current?.focus();
    else if(pending||stale||(!version&&error))recovery.current?.focus();
    else if(removing)closeButton.current?.focus();
    else if(version&&document.activeElement===dialog.current)input.current?.focus();
  },[busy,error,pending,removing,stale,version]);
  async function save(amount:number|null){
    if(!version||locked||(amount===null&&!stored))return;
    setRemoving(false);
    setValidation('');
    if(await settings.save({
      path:'/api/v1/manage/configuration/collection-target',
      body:{...scope,branchId:scope.branchId||null,amount,expectedRevision:version.revision},
      matches:next=>{
        const targets=(next.settings.ownerHome?.targets??[]).filter(matchesScope);
        return amount===null?targets.length===0:targets.length===1&&targets[0].amount===amount;
      },
      failure:'لم يتطابق هدف الفترة المقروء مع التغيير المطلوب.',
      success:amount===null?'تمت إزالة هدف الفترة.':'تم حفظ هدف التحصيل.',
    }))onSaved();
  }
  function saveAmount(){
    const canonical=value.trim()
      .replace(/[٠-٩]/g,digit=>String(digit.charCodeAt(0)-0x660))
      .replace(/[۰-۹]/g,digit=>String(digit.charCodeAt(0)-0x6f0))
      .replaceAll('٫','.');
    const amount=Number(canonical);
    if(!/^(?:\d+(?:\.\d{0,2})?|\.\d{1,2})$/.test(canonical)||amount<=0||amount>1_000_000_000){
      setValidation('أدخل مبلغًا موجبًا حتى مليار، بمنزلتين عشريتين كحد أقصى.');
      input.current?.focus();
      return;
    }
    void save(amount);
  }
  function close(){
    if(busy)return;
    if(removing){setRemoving(false);dialog.current?.focus();return;}
    onClose(pending!==null);
  }
  return <div className="dialog-backdrop" onMouseDown={e=>{if(e.target===e.currentTarget)close();}}>
    <section ref={dialog} tabIndex={-1} className={`confirmation-dialog ${styles.targetDialog}`} role="dialog" aria-modal="true" aria-labelledby="collection-target-title" aria-describedby="collection-target-scope" aria-busy={busy||undefined} onKeyDown={e=>{
      if(e.key==='Escape'){e.preventDefault();close();}
      if(e.key==='Tab'){
        const nodes=dialog.current?.querySelectorAll<HTMLElement>('input:not(:disabled),button:not(:disabled)');
        if(!nodes?.length){e.preventDefault();return;}
        const first=nodes[0],last=nodes[nodes.length-1];
        if(e.shiftKey&&(document.activeElement===first||document.activeElement===dialog.current)){e.preventDefault();last.focus();}
        else if(!e.shiftKey&&(document.activeElement===last||document.activeElement===dialog.current)){e.preventDefault();first.focus();}
      }
    }}>
      <h2 id="collection-target-title">ضبط هدف التحصيل</h2>
      <p id="collection-target-scope">{branchLabel} · {scope.from===scope.to ? formatDateAr(scope.to) : `من ${formatDateAr(scope.from)} إلى ${formatDateAr(scope.to)}`}</p>
      {error&&<p className={styles.targetError} role="alert">{stale?'تغيّرت إعدادات الأكاديمية. اقرأ الهدف الحالي قبل الحفظ.':error}</p>}
      {!version&&!error&&<LoadingState/>}
      <form onSubmit={e=>{e.preventDefault();if(!removing)saveAmount();}}>
        {version&&!removing&&<>
          <label>هدف الفترة ({currency==='EGP'?'ج.م':currency})
            <input ref={input} inputMode="decimal" dir="ltr" value={value} onChange={e=>{setDraft(e.target.value);setValidation('');}} disabled={locked||removing} aria-invalid={!!validation} aria-describedby={validation?'collection-target-error':undefined}/>
          </label>
          {validation&&<p id="collection-target-error" role="alert">{validation}</p>}
          {monthly!=null&&<small>الهدف الشهري الافتراضي {formatMoneyAr(monthly,currency)}</small>}
        </>}
        {removing&&stored&&<p role="status">سيُزال هدف الفترة البالغ {formatMoneyAr(stored.amount,currency)}.{monthly!=null?' سيعود عرض الهدف الشهري الافتراضي.':''}</p>}
        {pending&&<p>يمكنك التحقق من الحالة أو إغلاق النافذة والعودة لاحقًا.</p>}
        <div className={styles.targetActions}>
          {pending&&<button type="button" ref={recovery} className="primary-button" disabled={busy} onClick={()=>void settings.recheck().then(ok=>{if(ok)onSaved();})}>التحقق من الحفظ</button>}
          {(pending||stale||(!version&&error))&&<button type="button" ref={pending?undefined:recovery} className="secondary-button" disabled={busy} onClick={()=>void settings.reload().then(ok=>{if(ok){setDraft(null);setValidation('');}})}>{version?'قراءة الهدف الحالي':'إعادة المحاولة'}</button>}
          {version&&!pending&&!stale&&(removing
            ? <button type="button" className="button-danger" onClick={()=>void save(null)}>تأكيد إزالة الهدف</button>
            : <button type="submit" className="primary-button" disabled={busy||!value.trim()}>حفظ الهدف</button>)}
          {stored&&!locked&&!removing&&<button type="button" className="secondary-button" onClick={()=>setRemoving(true)}>إزالة هدف الفترة</button>}
          <button type="button" ref={closeButton} className="secondary-button" disabled={busy} onClick={close}>{pending?'إغلاق':removing?'العودة إلى التعديل':'إلغاء'}</button>
        </div>
      </form>
    </section>
  </div>;
}
