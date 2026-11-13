"use client";
import Link from "next/link";
import {useSearchParams} from "next/navigation";
import {useEffect,useRef,useState} from "react";
import {EmptyState,ErrorState,LoadingState,PageHeader,SectionCard} from "../components/dashboard-shell";
import {formatDateTimeAr,formatMoneyAr} from "../lib/formatters";
import {csrfRequest,DashboardRequestError} from "./dashboard-api";
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
const inboxPageSize = 25;
const inboxNumber = new Intl.NumberFormat("ar-EG-u-nu-latn");

function noticeDestination(notice: Notice, guardian: boolean) {
  const id = notice.targetId && encodeURIComponent(notice.targetId);
  if (notice.matchId || notice.targetKind === "Matches") {
    const match = notice.matchId || notice.targetId;
    const path = guardian ? "/guardian/matches" : "/dashboard/matches";
    return match ? { href: `${path}?matchId=${encodeURIComponent(match)}`, label: "عرض المباراة" }
      : { href: path, label: "عرض المباريات" };
  }
  if (notice.targetKind === "GroupAnnouncement" && id) return { href: `/announcements/${id}`, label: "عرض إشعار المجموعة" };
  if (guardian) {
    if (notice.reward) return { href: id ? `/guardian/rewards/${id}` : "/guardian/rewards", label: "عرض المكافأة" };
    if (notice.targetKind === "Receivables") return { href: "/guardian/receivables", label: "كشف المستحقات" };
    if (!id) return;
    switch (notice.targetKind) {
      case "SubscriptionPeriod": return { href: `/guardian/subscriptions/periods/${id}`, label: "عرض الاشتراك وتعديلاته" };
      case "Attendance": return { href: `/guardian/children/${id}`, label: "عرض ملف اللاعب وحضوره" };
      case "GuardianFeedback": return { href: `/guardian/children/${id}/feedback`, label: "تقديم شكوى أو اقتراح للمالك" };
      case "Evaluation": return { href: `/guardian/evaluations/${id}`, label: "عرض التقييم" };
      case "FamilyRenewal": return { href: `/guardian/family-renewals/${id}`, label: "عرض تجديد الأسرة" };
      case "Payment": return { href: `/guardian/checkout/${id}`, label: "عرض طلب الدفع" };
    }
    return;
  }
  const payment = paymentNoticeHref(notice);
  if (payment) return { href: payment, label: "عرض طلب الدفع ومراجعته" };
  if (notice.targetKind === "ProductReview" && id) return { href: `/dashboard/product-sales/${id}`, label: "مراجعة طلب المنتج" };
  if (notice.targetKind === "AttendanceCorrection" && id) return { href: `/dashboard/attendance/corrections/${id}`, label: "عرض طلب تصحيح الحضور" };
}

type Recipient={id:string;name:string;amount:number|null;currency:string;children:{id:string;name:string;amount:number|null}[]};
type Audience={items:Recipient[];total:number;shown:number;template:string};
type Preview={count:number;previewHash:string;messages:{id:string;message:string}[]};
async function get<T>(url:string):Promise<T>{const r=await fetch(url,{cache:"no-store"});if(!r.ok)throw Error("تعذر تحميل البيانات أو لا توجد صلاحية.");return r.json();}
export function NotificationInbox({embedded=false,guardian=false}:{embedded?:boolean;guardian?:boolean}={}){
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; data?: Inbox; error?: string }>();
  const [pending, setPending] = useState<string>();
  const [failedNotice, setFailedNotice] = useState<string>();
  const writeLock = useRef(false);
  const key = `${guardian}:${page}:${revision}`;
  const current = result?.key === key ? result : undefined;
  const data = current?.data;

  useEffect(() => {
    const controller = new AbortController();
    void fetch(`/api/v1/notifications?page=${page}`, { cache: "no-store", signal: controller.signal }).then(async response => {
      if (response.status === 401 || response.status === 403) {
        if (!controller.signal.aborted) setResult({ key, error: "لا يمكن عرض التنبيهات لهذه الجلسة." });
        return;
      }
      if (!response.ok) throw new Error();
      const value = await response.json() as Inbox;
      if (!value || !Array.isArray(value.items) || !Number.isSafeInteger(value.unread) || !Number.isSafeInteger(value.total)) throw new Error();
      if (!controller.signal.aborted) setResult({ key, data: value });
    }).catch(() => {
      if (!controller.signal.aborted) setResult({ key, error: "تعذر تحميل التنبيهات." });
    });
    return () => controller.abort();
  }, [key, page]);

  async function toggle(notice: Notice) {
    if (writeLock.current || failedNotice) return;
    writeLock.current = true;
    setPending(notice.id);
    const isRead = !notice.isRead;
    try {
      await csrfRequest(`/api/v1/notifications/${encodeURIComponent(notice.id)}/read`, "PUT", { isRead }, {}, { announce: false });
      setResult(previous => previous?.key === key && previous.data ? {
        key,
        data: {
          ...previous.data,
          unread: Math.max(0, previous.data.unread + (isRead ? -1 : 1)),
          items: previous.data.items.map(item => item.id === notice.id ? { ...item, isRead } : item),
        },
      } : previous);
    } catch {
      setFailedNotice(notice.id);
    } finally {
      writeLock.current = false;
      setPending(undefined);
    }
  }

  function reload() {
    setFailedNotice(undefined);
    setRevision(value => value + 1);
  }

  const pages = data ? Math.max(1, Math.ceil(data.total / inboxPageSize)) : 1;
  return <>
    {!embedded && <><PageHeader title="التنبيهات" context="حسابك" /><Link className="back-link" href={guardian ? "/guardian" : "/dashboard"}>العودة إلى الرئيسية</Link></>}
    <section className="notification-inbox" aria-label="الوارد" dir="rtl">
      {current?.error ? <><ErrorState message={current.error} /><div className="notification-actions">
        <button type="button" className="secondary-button" onClick={reload}>إعادة المحاولة</button>
        {page > 1 && <button type="button" className="secondary-button" onClick={() => setPage(1)}>العودة إلى الصفحة الأولى</button>}
      </div></>
      : !data ? <LoadingState message="جارٍ تحميل التنبيهات…" /> : <>
        {data.total > 0 && <p className="notification-count" role="status">غير المقروءة {inboxNumber.format(data.unread)}</p>}
        {data.items.length === 0 && <p className="detail-card">{page === 1 ? "لا توجد تنبيهات." : "لا توجد تنبيهات في هذه الصفحة."}</p>}
        {data.items.map(notice => {
          const destination = noticeDestination(notice, guardian);
          return <article className="detail-card notification-item" data-unread={!notice.isRead} key={notice.id}>
            <header className="notification-meta">
              <span>{notice.isRead ? "مقروء" : "غير مقروء"}</span>
              {notice.createdAtUtc && Number.isFinite(Date.parse(notice.createdAtUtc)) && <time dateTime={notice.createdAtUtc}>{formatDateTimeAr(notice.createdAtUtc)}</time>}
            </header>
            <p className="notification-message">{notice.message}</p>
            <div className="notification-actions">
              {destination && <Link href={destination.href}>{destination.label}</Link>}
              <button type="button" className="secondary-button" aria-disabled={Boolean(pending || failedNotice)} onClick={() => void toggle(notice)}>
                {pending === notice.id ? "جارٍ تحديث الحالة…" : notice.isRead ? "تحديد كغير مقروء" : "تحديد كمقروء"}
              </button>
            </div>
            {failedNotice === notice.id && <div className="notification-recovery">
              <ErrorState message="تعذر تأكيد تحديث الحالة. حدّث القائمة للتحقق." />
              <button type="button" className="secondary-button" onClick={reload}>تحديث القائمة</button>
            </div>}
          </article>;
        })}
        {(page > 1 || pages > 1) && <nav className="notification-actions" aria-label="صفحات التنبيهات">
          <button type="button" className="secondary-button" disabled={page === 1 || Boolean(pending)} onClick={() => { setFailedNotice(undefined); setPage(value => value - 1); }}>السابق</button>
          <span>صفحة {inboxNumber.format(page)} من {inboxNumber.format(Math.max(page, pages))}</span>
          <button type="button" className="secondary-button" disabled={page >= pages || Boolean(pending)} onClick={() => { setFailedNotice(undefined); setPage(value => value + 1); }}>التالي</button>
        </nav>}
      </>}
    </section>
  </>;
}
export function CollectionReminders(){
  const incoming=useSearchParams(),requested=incoming.get("kind");
  const kind=requested==="Renewal"||requested==="Expiring"||requested==="Expired"||requested==="Absence"?requested:"Overdue";
  const branchId=incoming.get("branchId")??"",playerId=incoming.get("playerId")??"";
  return <ReminderScreen key={JSON.stringify([kind,branchId,playerId])} kind={kind} branchId={branchId} playerId={playerId}/>;
}

function ReminderScreen({kind,branchId,playerId}:{kind:"Overdue"|"Renewal"|"Expiring"|"Expired"|"Absence";branchId:string;playerId:string}) {
  const title={Overdue:"تذكيرات التحصيل",Renewal:"خطة متابعة التجديد",Expiring:"تنبيه قرب انتهاء الاشتراك",Expired:"خطة تجديد الاشتراك المنتهي",Absence:"خطة متابعة الغياب المتكرر"}[kind];
  const [data,setData]=useState<Audience|null>(null);
  const [selected,setSelected]=useState<string[]>([]);
  const [message,setMessage]=useState("");
  const [preview,setPreview]=useState<(Preview & {requestId:string})|null>(null);
  const [error,setError]=useState("");
  const [busy,setBusy]=useState(false);
  const [sent,setSent]=useState<number|null>(null);
  const [revision,setRevision]=useState(0);
  const [recovery,setRecovery]=useState<"reload"|"send"|"denied"|"disabled"|null>(null);
  const running=useRef(false);
  const lifetime=useRef<AbortController|null>(null);
  const branch:Record<string,string>=branchId?{branchId}:{};

  useEffect(()=>{
    const controller=new AbortController();
    lifetime.current=controller;
    const {signal}=controller;
    const query=new URLSearchParams({kind,...branchId?{branchId}:{},...playerId?{playerId}:{}});
    fetch(`/api/v1/manage/notifications/recipients?${query}`,{cache:"no-store",signal}).then(async response=>{
      if ([401,403,404].includes(response.status)) throw new DashboardRequestError("تذكيرات هذا النطاق غير متاحة لهذا الحساب.",response.status);
      if (!response.ok) throw new Error();
      const audience:Audience=await response.json();
      if(!Array.isArray(audience?.items)||typeof audience.template!=="string") throw new Error();
      if(!signal.aborted){setData(audience);setMessage(audience.template);}
    }).catch(cause=>{
      if(signal.aborted)return;
      setError(cause instanceof DashboardRequestError?cause.message:"تعذر تحميل قائمة المستلمين.");
      setRecovery(cause instanceof DashboardRequestError?"denied":"reload");
    });
    return ()=>controller.abort();
  },[kind,branchId,playerId,revision]);

  function reload(){
    setData(null);setSelected([]);setPreview(null);setError("");setRecovery(null);
    setRevision(value=>value+1);
  }

  async function action(send:boolean){
    if(running.current||!selected.length||!message.trim()||send&&!preview||recovery==="reload"||recovery==="denied"||recovery==="disabled")return;
    const signal=lifetime.current?.signal;
    const requestId=send?preview!.requestId:crypto.randomUUID();
    const body={kind,branchId:branchId||null,playerId:playerId||null,recipients:selected,message,requestId,previewHash:send?preview!.previewHash:undefined};
    running.current=true;setBusy(true);setError("");
    try{
      const response=await csrfRequest(`/api/v1/manage/notifications/${send?"send":"preview"}`,"POST",body,{},{announce:false,signal});
      const result=await response.json();
      if(signal?.aborted)return;
      if(send){
        if(!Number.isInteger(result?.sent)||result.sent<1)throw new Error();
        setSent(result.sent);setPreview(null);setRecovery(null);
      }else{
        if(typeof result?.previewHash!=="string"||!Array.isArray(result.messages)||result.count!==selected.length)throw new Error();
        setPreview({...result,requestId});
      }
    }catch(cause){
      if(signal?.aborted)return;
      const status=cause instanceof DashboardRequestError?cause.status:0;
      if([401,403,404].includes(status)){
        setData(null);setSelected([]);setPreview(null);setRecovery("denied");
        setError("تذكيرات هذا النطاق غير متاحة لهذا الحساب.");
      }else if(cause instanceof DashboardRequestError&&cause.code==="notice_template_disabled"){
        setPreview(null);setRecovery("disabled");setError(cause.message);
      }else if(status===409){
        setPreview(null);setRecovery("reload");setError("تغيّرت بيانات التذكير. حدّث قائمة المستلمين قبل إعادة المعاينة.");
      }else{
        if(send)setRecovery("send");
        setError(send?"تعذر تأكيد الإرسال. أعد تأكيد المحاولة نفسها للتحقق.":"تعذرت معاينة التنبيه. أعد المحاولة.");
      }
    }finally{running.current=false;if(!signal?.aborted)setBusy(false);}
  }

  const editDisabled=busy||recovery!==null;
  const ledger=`/dashboard/finance/receivables?${new URLSearchParams({state:"overdue",...branch})}`;
  return <>
    <PageHeader title={title} context="مركز القرارات"/>
    {error&&<ErrorState message={error}/>}
    {recovery==="reload"&&<button type="button" className="secondary-button" onClick={reload}>تحديث قائمة المستلمين</button>}
    {recovery==="disabled"&&<Link className="button button-secondary" href="/dashboard/notifications#notification-templates">إدارة قوالب التنبيهات</Link>}
    {sent!==null?<SectionCard title="تم الإرسال"><p role="status">تم حفظ {sent} تنبيه في صناديق المستلمين.</p></SectionCard>
      :!data?!error&&<LoadingState/>
      :!data.items.length?<section className="list-card"><EmptyState message="لا توجد أسر متاحة للتنبيه في هذا النطاق." action={kind==="Overdue"?<Link className="button button-secondary" href={ledger}>مراجعة كشف المستحقات</Link>:undefined}/></section>
      :<SectionCard title="المستلمون">
        {data.shown<data.total&&<p>المعروض {data.shown} من {data.total}. ضيّق النطاق لاختيار أسر أخرى.</p>}
        {kind==="Renewal"&&<p>قيمة التجديد تقديرية.</p>}
        <label className="policy-switch"><input type="checkbox" disabled={editDisabled} checked={selected.length===data.items.length} onChange={event=>{setSelected(event.target.checked?data.items.map(row=>row.id):[]);setPreview(null);}}/>تحديد كل المعروض ({data.items.length})</label>
        {data.items.map(row=><label className="placement-choice" key={row.id}>
          <input type="checkbox" disabled={editDisabled} checked={selected.includes(row.id)} onChange={event=>{setSelected(value=>event.target.checked?[...value,row.id]:value.filter(id=>id!==row.id));setPreview(null);}}/>
          <span><strong>{row.name}</strong><small>{[row.children.map(child=>child.name).join("، "),row.amount==null?null:formatMoneyAr(row.amount,row.currency)].filter(Boolean).join(" · ")}</small></span>
        </label>)}
        <label className="form-shell">نص التنبيه<textarea maxLength={1000} disabled={editDisabled} value={message} onChange={event=>{setMessage(event.target.value);setPreview(null);}}/></label>
        {!preview&&selected.length>0&&recovery===null&&<button type="button" className="secondary-button" disabled={busy||!message.trim()} onClick={()=>void action(false)}>معاينة التنبيه</button>}
        {preview&&<section aria-label="معاينة التنبيهات">
          <h3>معاينة التنبيهات</h3>
          {preview.messages.map((item,index)=><article className="detail-card" key={`${item.id}:${index}`}>{item.message}</article>)}
          <p>تصل التنبيهات إلى الأسر المحددة داخل التطبيق.</p>
          <button type="button" className="primary-button" disabled={busy} onClick={()=>void action(true)}>{recovery==="send"?"إعادة تأكيد الإرسال":`تأكيد إرسال ${preview.count} تنبيه`}</button>
        </section>}
      </SectionCard>}
  </>;
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
