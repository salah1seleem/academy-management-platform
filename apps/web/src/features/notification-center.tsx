"use client";
import Link from "next/link";
import {useSearchParams} from "next/navigation";
import {useCallback,useEffect,useState} from "react";
import {ErrorState,LoadingState,PageHeader,SectionCard} from "../components/dashboard-shell";
import {formatMoneyAr} from "../lib/formatters";
import {csrfRequest} from "./dashboard-api";
import {ApprovedWorkspace} from "../components/approved-workspace";
import {RecordTabs} from "../components/record-tabs";
type Notice={id:string;message:string;isRead:boolean;createdAtUtc:string;matchId?:string;targetKind?:string;targetId?:string;paymentReviewType?:string|null;reward:boolean};
function paymentNoticeHref(notice:Notice):string|undefined {
 if(notice.targetKind!=="PaymentQueue"||!notice.targetId)return undefined;
 const id=encodeURIComponent(notice.targetId);
 switch(notice.paymentReviewType){
  case "Enrollment": return `/dashboard/enrollment-requests/${id}`;
  case "Renewal": return `/dashboard/subscriptions/payments/${id}`;
  case "FamilyRenewal": return `/dashboard/family-renewals/${id}`;
  default: return undefined;
 }
}
type Inbox={items:Notice[];unread:number;total:number};
type Recipient={id:string;name:string;amount:number|null;currency:string;children:{id:string;name:string;amount:number|null}[]};
type Audience={items:Recipient[];total:number;shown:number;template:string};
type Preview={count:number;previewHash:string;messages:{id:string;message:string}[]};
async function get<T>(url:string):Promise<T>{const r=await fetch(url,{cache:"no-store"});if(!r.ok)throw Error("تعذر تحميل البيانات أو لا توجد صلاحية.");return r.json();}
export function NotificationInbox({embedded=false}:{embedded?:boolean}={}){
  const [data,setData]=useState<Inbox|null>(null),[page,setPage]=useState(1),[error,setError]=useState(""),[busy,setBusy]=useState(false);
  const load=useCallback(()=>get<Inbox>(`/api/v1/notifications?page=${page}`).then(d=>{setData(d);setError("");}).catch(e=>setError(e.message)),[page]);
  useEffect(()=>{void load();},[load]);
  async function toggle(n:Notice){setBusy(true);try{await csrfRequest(`/api/v1/notifications/${n.id}/read`,"PUT",{isRead:!n.isRead},{},{announce:false});await load();}catch(e){setError(e instanceof Error?e.message:"تعذر التحديث.");}finally{setBusy(false);}}
  return <>{!embedded&&<PageHeader title="التنبيهات" context="حسابك"/>}{error&&<ErrorState message={error}/>}{!embedded&&<Link className="back-link" href="/dashboard">العودة للرئيسية</Link>}{!data?<LoadingState/>:<SectionCard title={`${data.unread} غير مقروءة`}>{!data.items.length&&<p>لا توجد تنبيهات.</p>}{data.items.map(n=><article className="detail-card" key={n.id}><strong>{n.message}</strong><small className="muted" style={{display:"block"}}><time dateTime={n.createdAtUtc} dir="ltr">{n.createdAtUtc.slice(0,10)}</time></small><div className="row-actions"><button disabled={busy} onClick={()=>void toggle(n)}>{n.isRead?"تحديد كغير مقروء":"تمت القراءة"}</button>{n.targetKind==="GroupAnnouncement"&&n.targetId&&<Link href={`/announcements/${encodeURIComponent(n.targetId)}`}>عرض إشعار المجموعة</Link>}{n.matchId&&<Link href="/dashboard/matches">عرض المباراة</Link>}{n.reward&&<Link href={n.targetId?`/guardian/rewards/${encodeURIComponent(n.targetId)}`:"/guardian/rewards"}>عرض المكافأة</Link>}{paymentNoticeHref(n)&&<Link href={paymentNoticeHref(n)!}>عرض طلب الدفع ومراجعته</Link>}{n.targetKind==="ProductReview"&&n.targetId&&<Link href={`/dashboard/product-sales/${n.targetId}`}>مراجعة طلب المنتج</Link>}{n.targetKind==="SubscriptionPeriod"&&n.targetId&&<Link href={`/guardian/subscriptions/periods/${n.targetId}`}>عرض الاشتراك وتعديلاته</Link>}{n.targetKind==="Attendance"&&n.targetId&&<Link href={`/guardian/children/${n.targetId}`}>عرض ملف اللاعب وحضوره</Link>}{n.targetKind==="Evaluation"&&n.targetId&&<Link href={`/guardian/evaluations/${n.targetId}`}>عرض التقييم</Link>}{n.targetKind==="AttendanceCorrection"&&n.targetId&&<Link href={`/dashboard/attendance/corrections/${n.targetId}`}>عرض طلب تصحيح الحضور</Link>}{n.targetKind==="FamilyRenewal"&&n.targetId&&<Link href={`/guardian/family-renewals/${n.targetId}`}>عرض تجديد الأسرة</Link>}</div></article>)}<nav className="row-actions"><button disabled={page===1} onClick={()=>{setData(null);setError("");setPage(p=>p-1);}}>السابق</button><span>{page}</span><button disabled={page*25>=data.total} onClick={()=>{setData(null);setError("");setPage(p=>p+1);}}>التالي</button></nav></SectionCard>}</>;
}
export function CollectionReminders(){
  const incoming=useSearchParams(),kind=incoming.get("kind")==="Renewal"?"Renewal":"Overdue",branchId=incoming.get("branchId")??"";
  const [data,setData]=useState<Audience|null>(null),[selected,setSelected]=useState<string[]>([]),[message,setMessage]=useState(""),[preview,setPreview]=useState<Preview|null>(null),[error,setError]=useState(""),[busy,setBusy]=useState(false),[sent,setSent]=useState<number|null>(null);
  const [requestId]=useState(()=>crypto.randomUUID());
  useEffect(()=>{let live=true;get<Audience>(`/api/v1/manage/notifications/recipients?${new URLSearchParams({kind,...branchId?{branchId}:{}})}`).then(d=>{if(live){setData(d);setMessage(d.template);}}).catch(e=>{if(live)setError(e.message);});return()=>{live=false;};},[kind,branchId]);
  const body={kind,branchId:branchId||null,recipients:selected,message,requestId,previewHash:preview?.previewHash};
  async function action(send:boolean){if(busy)return;setBusy(true);setError("");try{const response=await csrfRequest(`/api/v1/manage/notifications/${send?"send":"preview"}`,"POST",body,{},{announce:false});const result=await response.json();if(send){setSent(result.sent);setPreview(null);}else setPreview(result);}catch(e){setError(e instanceof Error?e.message:"تعذر تنفيذ الإجراء.");}finally{setBusy(false);}}
  return <><PageHeader title={kind==="Overdue"?"تذكيرات التحصيل":"خطة متابعة التجديد"} context="مركز القرارات"/>{error&&<ErrorState message={error}/>}<p>التنبيه داخل التطبيق فقط للأسر المحددة. {kind==="Renewal"?"قيم التجديد المعروضة ليست ديونًا قائمة.":"المبالغ من المطالبات المتأخرة المحفوظة."}</p>{sent!=null?<SectionCard title="تم الإرسال"><p role="status">تم حفظ {sent} تنبيه في صناديق المستلمين.</p></SectionCard>:!data?<LoadingState/>:<SectionCard title={`${data.total} ولي أمر · المعروض ${data.shown}`}><label className="policy-switch"><input type="checkbox" disabled={busy} checked={data.items.length>0&&selected.length===data.items.length} onChange={e=>{setSelected(e.target.checked?data.items.map(r=>r.id):[]);setPreview(null);}}/>تحديد كل المعروض ({data.items.length})</label>{data.items.map(r=><label className="placement-choice" key={r.id}><input type="checkbox" disabled={busy} checked={selected.includes(r.id)} onChange={e=>{setSelected(s=>e.target.checked?[...s,r.id]:s.filter(x=>x!==r.id));setPreview(null);}}/><span><strong>{r.name}</strong><small>{r.children.map(c=>c.name).join("، ")} · {r.amount==null?"القيمة تحتاج مراجعة":formatMoneyAr(r.amount,r.currency)}</small></span></label>)}<label className="form-shell">نص التنبيه<textarea maxLength={1000} disabled={busy} value={message} onChange={e=>{setMessage(e.target.value);setPreview(null);}}/></label><button className="secondary-button" disabled={busy||!selected.length} onClick={()=>void action(false)}>معاينة {selected.length} تنبيه</button>{preview&&<section aria-label="معاينة التنبيهات"><h3>معاينة {preview.count} رسالة</h3>{preview.messages.map(m=><article className="detail-card" key={m.id}>{m.message}</article>)}<button className="primary-button" disabled={busy} onClick={()=>void action(true)}>تأكيد إرسال {preview.count} تنبيه</button></section>}</SectionCard>}</>;
}
const templateNames:Record<string,string>={Enrollment:"اشتراك جديد",SingleRenewal:"تجديد اشتراك",FamilyRenewal:"تجديد الأسرة",RecordedAbsence:"غياب حصة مسجل",DaysAdded:"إضافة أيام",SessionsAdded:"إضافة حصص وتحديد نهاية الاشتراك",DaysDeducted:"خصم أيام",Overdue:"مستحقات متأخرة",Renewal:"متابعة تجديد",Expiring:"قرب الانتهاء",Expired:"اشتراك منتهٍ",Absence:"غياب متكرر",Evaluation:"تقييم منشور",Star:"نجمة شهرية",Match:"تحديث مباراة",Payment:"طلب دفع",VipReward:"مكافأة عضو مميز"};
type TemplateItem={key:string;message:string;variables?:string[];enabled?:boolean;version?:number;timingDays?:number|null;condition?:string;recipients?:string[]};
export function NotificationTemplates(){
  const [items,setItems]=useState<TemplateItem[]|null>(null),[error,setError]=useState(""),[message,setMessage]=useState(""),[busy,setBusy]=useState(false);
  useEffect(()=>{let current=true;get<TemplateItem[]>("/api/v1/manage/notifications/templates").then(d=>{if(current)setItems(d);}).catch(e=>{if(current)setError(e.message);});return()=>{current=false;};},[]);
  function change(key:string,value:Partial<TemplateItem>){setMessage("");setItems(s=>s!.map(t=>t.key===key?{...t,...value}:t));}
  async function refresh(key:string){
    const data=await get<TemplateItem[]>("/api/v1/manage/notifications/templates");const saved=data.find(t=>t.key===key);
    if(!saved)throw Error("تعذر قراءة إعداد التنبيه المحفوظ.");
    setItems(s=>s!.map(t=>t.key===key?saved:t));return saved;
  }
  async function save(t:TemplateItem){
    if(busy)return;setBusy(true);setError("");setMessage("");
    try{
      await csrfRequest(`/api/v1/manage/notifications/templates/${t.key}`,"PUT",{message:t.message,...t.version===undefined?{}:{expectedVersion:t.version,enabled:t.enabled,...t.key==='Expiring'?{timingDays:t.timingDays}:{} }},{},{announce:false});
      const saved=await refresh(t.key);
      if(saved.message!==t.message.trim()||t.enabled!==undefined&&saved.enabled!==t.enabled||t.key==='Expiring'&&t.timingDays!=null&&saved.timingDays!==t.timingDays)throw Error("لم تتأكد مطابقة الإعداد المحفوظ. أعد تحميل القالب قبل المتابعة.");
      setMessage("حُفظ إعداد التنبيه وأُعيدت قراءته. لم يُرسل حفظ القالب رسالة.");
    }catch(e){setError(e instanceof Error?e.message:"تعذر حفظ القالب.");}finally{setBusy(false);}
  }
  return <section id="notification-templates" className="detail-card"><h2>القوالب والمشغلات</h2>{error&&<ErrorState message={error}/>}<p className="muted">استخدم المتغيرات المعروضة لكل حدث. حفظ القالب لا يرسل رسالة ولا يعيد إرسال أحداث قديمة. تبقى التنبيهات السابقة كما كانت عند حفظها.</p>{message&&<p role="status">{message}</p>}
    {items?.map(t=><form className="form-shell detail-card" key={t.key} onSubmit={e=>{e.preventDefault();void save(t);}}><fieldset disabled={busy}>
      <legend>{templateNames[t.key]}</legend>{t.condition&&<p>{t.condition}</p>}{t.recipients&&<p>المستلمون: {t.recipients.join("، ")}. يراجع الخادم الربط والنطاق الفعلي.</p>}
      {typeof t.enabled==='boolean'&&<label className="policy-switch"><input type="checkbox" checked={t.enabled} onChange={e=>change(t.key,{enabled:e.target.checked})}/>تفعيل الحدث</label>}
      {t.key==='Expiring'&&t.timingDays!=null&&<label>التنبيه قبل الانتهاء بالأيام<input required type="number" min={1} max={30} value={t.timingDays} onChange={e=>change(t.key,{timingDays:Number(e.target.value)})}/></label>}
      <label>{templateNames[t.key]}{t.variables&&<small>المتغيرات المتاحة: {t.variables.map(v=>`{${v}}`).join("، ")||"نص ثابت"}</small>}<textarea required maxLength={1000} value={t.message} onChange={e=>change(t.key,{message:e.target.value})}/></label>
      <div className="row-actions"><button className="primary-button">حفظ القالب</button><button className="secondary-button" type="button" onClick={()=>{setBusy(true);setError("");void refresh(t.key).catch(e=>setError(e.message)).finally(()=>setBusy(false));}}>إعادة تحميل القالب</button></div>
    </fieldset></form>)}
  </section>;
}
export function NotificationHistory(){
  const [data,setData]=useState<{items:{id:string;eventKey:string|null;message:string;recipient:string|null;audienceRole:string;status:string;isRead:boolean;createdAtUtc:string}[];total:number}|null>(null),[error,setError]=useState(""),[event,setEvent]=useState(""),[page,setPage]=useState(1),[automation,setAutomation]=useState<{scheduledEnabled:boolean;externalDelivery:boolean}|null>(null);
  useEffect(()=>{let current=true;get<NonNullable<typeof data>>(`/api/v1/manage/notifications/history?${new URLSearchParams({page:String(page),...event?{eventKey:event}:{}})}`).then(d=>{if(current)setData(d);}).catch(e=>{if(current)setError(e.message);});return()=>{current=false;};},[event,page]);
  useEffect(()=>{let current=true;get<NonNullable<typeof automation>>("/api/v1/manage/notifications/automation").then(d=>{if(current)setAutomation(d);}).catch(()=>{});return()=>{current=false;};},[]);
  const roles:Record<string,string>={AcademyOwner:"المالك",AcademyAdmin:"الإداري",Coach:"المدرب",Guardian:"ولي الأمر"};
  return <SectionCard title="سجل التنبيهات وحالتها">{automation&&<p>فحص شروط الانتهاء المجدول: {automation.scheduledEnabled?"مفعّل":"غير مفعّل في هذه البيئة"}. {automation.externalDelivery?"":"النتائج داخل التطبيق؛ لا إرسال خارجي مفعّل."}</p>}
    <label>نوع الحدث<select value={event} onChange={e=>{setData(null);setError("");setEvent(e.target.value);setPage(1);}}><option value="">كل الأحداث</option>{Object.entries(templateNames).map(([key,name])=><option key={key} value={key}>{name}</option>)}</select></label>
    {error&&<ErrorState message={error}/>} {!data&&!error?<LoadingState/>:data&&<>{data.items.length===0&&<p>لا توجد نتائج محفوظة لهذا النطاق.</p>}{data.items.map(n=><article className="detail-card" key={n.id}><strong>{n.eventKey?(templateNames[n.eventKey]??"تنبيه محفوظ"):"تنبيه غير مصنّف"}</strong><p>{n.message}</p><small>{n.recipient??"حساب غير متاح"} · {roles[n.audienceRole]??"حساب مخوّل"} · {n.createdAtUtc.slice(0,10)}</small><p>{n.status==='HistoricalHidden'?"سجل تاريخي غير ظاهر؛ حالة تسليمه غير موثقة":n.status==='Disabled'?"الحدث معطل؛ لم يظهر في صندوق المستلم":n.isRead?"محفوظ داخل التطبيق · قرأه المستلم":"محفوظ داخل التطبيق · لم يقرأه المستلم بعد"}</p></article>)}<nav className="row-actions"><button disabled={page===1} onClick={()=>{setData(null);setError("");setPage(p=>p-1);}}>السابق</button><span>{page}</span><button disabled={page*25>=data.total} onClick={()=>{setData(null);setError("");setPage(p=>p+1);}}>التالي</button></nav></>}
  </SectionCard>;
}

export function NotificationWorkspace(){
 const [owner,setOwner]=useState(false);
 useEffect(()=>{const c=new AbortController();fetch('/api/v1/me',{signal:c.signal,cache:'no-store'}).then(r=>r.ok?r.json():{}).then((d:{role?:string})=>{if(!c.signal.aborted)setOwner(d.role==='AcademyOwner');}).catch(()=>{});return()=>c.abort();},[]);
 return <ApprovedWorkspace><PageHeader title="التنبيهات" context="الأكاديمية"/><RecordTabs label="أقسام التنبيهات" tabs={[{id:'inbox',label:'الوارد',content:<NotificationInbox embedded/>},...owner?[{id:'templates',label:'القوالب والمشغلات',content:<NotificationTemplates/>},{id:'history',label:'سجل التنبيهات',content:<NotificationHistory/>}]:[]]}/></ApprovedWorkspace>;
}
