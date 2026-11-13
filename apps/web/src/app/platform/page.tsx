"use client";
import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import Image from "next/image";
import { Building2, ShieldCheck, LogOut, Plus } from "lucide-react";
import { platformRequest, PlatformSessionExpiredError } from "../../features/platform-api";
import { PlatformAcademyEditor } from "./platform-academy-editor";
import { apiProblemMessage } from "../../lib/api-problem";
import styles from "./platform.module.css";

type AcademyRow = { id:string;displayName:string;slug:string;hasLogo:boolean;ownerName:string|null;ownerPhone:string|null;location:string;state:string;commercialState:string;fee:string|number|null;feeCurrency:string;billingPeriod:string;branches:number;sports:number;players:number };
type Directory = {totals:{all:number;active:number;preparing:number;suspended:number;reminders:number};count:number;page:number;pageSize:number;items:AcademyRow[]};
const stateLabels:Record<string,string>={Draft:"قيد التجهيز",Active:"نشطة",Suspended:"الوصول معلّق",Unspecified:"غير محدد",Trial:"تجريبي",Subscribed:"مشتركة",Expired:"منتهي"};
export default function PlatformHome(){
 const errorRef=useRef<HTMLDivElement>(null);const [sessionExpired,setSessionExpired]=useState(false);const router=useRouter();const [ready,setReady]=useState(false),[directory,setDirectory]=useState<Directory|null>(null),[selected,setSelected]=useState<string|null>(null),[create,setCreate]=useState(false),[requestId,setRequestId]=useState(""),[busy,setBusy]=useState(false),[message,setMessage]=useState(""),[loading,setLoading]=useState(true),[revision,setRevision]=useState(0);
 const [filters,setFilters]=useState({search:"",state:"",commercial:"",currency:"",period:"",sort:"name",direction:"asc",page:1});
 const [accessError, setAccessError] = useState("");
 const [accessAttempt, setAccessAttempt] = useState(0);
 const [directoryFailure, setDirectoryFailure] = useState<{ message: string; needsLogin: boolean } | null>(null);

 useEffect(() => {
  const controller = new AbortController();
  setAccessError("");
  void (async () => {
   try {
    const response = await fetch("/api/v1/platform/me", { cache: "no-store", signal: controller.signal });
    if (controller.signal.aborted) return;
    if (response.ok) setReady(true);
    else if (response.status === 401) router.replace("/platform/login");
    else setAccessError(response.status === 403
     ? "حسابك لا يملك صلاحية إدارة المنصة."
     : "تعذر التحقق من حساب المنصة. أعد المحاولة.");
   } catch {
    if (!controller.signal.aborted) setAccessError("تعذر الاتصال بالمنصة. أعد المحاولة.");
   }
  })();
  return () => controller.abort();
 }, [router, accessAttempt]);
 useEffect(()=>{if(!ready)return;const controller=new AbortController();const renew=()=>{if(document.visibilityState==="visible")void fetch("/api/v1/platform/me",{cache:"no-store",signal:controller.signal}).then(r=>{if(r.status===401)setSessionExpired(true);}).catch(()=>{});};const timer=setInterval(renew,20*60*1000);document.addEventListener("visibilitychange",renew);return()=>{clearInterval(timer);document.removeEventListener("visibilitychange",renew);controller.abort();};},[ready]);
 useEffect(() => {
  if (!ready) return;
  const controller = new AbortController();
  setLoading(true);
  setDirectoryFailure(null);
  const timer = setTimeout(async () => {
   const query = new URLSearchParams({ ...filters, page: String(filters.page), pageSize: "15" });
   const fallback = "تعذر تحميل الأكاديميات.";
   try {
    const response = await fetch(`/api/v1/platform/academies/directory?${query}`, { cache: "no-store", signal: controller.signal });
    if (!response.ok) {
     const needsLogin = response.status === 401;
     const message = needsLogin ? "انتهت جلسة إدارة المنصة. سجّل الدخول ثم أعد تحميل القائمة."
      : response.status === 403 ? "حسابك لا يملك صلاحية عرض الأكاديميات."
      : await apiProblemMessage(response, fallback);
     if (!controller.signal.aborted) {
      setDirectory(null);
      setDirectoryFailure({ message, needsLogin });
     }
     return;
    }
    const value = await response.json() as Directory;
    if (!controller.signal.aborted) setDirectory(value);
   } catch {
    if (!controller.signal.aborted) {
     setDirectory(null);
     setDirectoryFailure({ message: fallback, needsLogin: false });
    }
   } finally {
    if (!controller.signal.aborted) setLoading(false);
   }
  }, 200);
  return () => { clearTimeout(timer); controller.abort(); };
 }, [ready, filters, revision]);
 useEffect(()=>{if(message)errorRef.current?.focus();},[message]);
 const refresh=useCallback(()=>setRevision(x=>x+1),[]);
 function filter(key:string,value:string){setFilters(x=>({...x,[key]:value,page:1}));}
 async function provision(event:FormEvent<HTMLFormElement>){event.preventDefault();if(busy)return;const form=new FormData(event.currentTarget);const slug=String(form.get("slug")??"").trim().toLowerCase();if(!/^[a-z0-9][a-z0-9-]{2,79}$/.test(slug)){setMessage("كود الرابط رمز مختصر مثل arm أو arm-academy، وليس عنوان موقع. استخدم حروفًا إنجليزية صغيرة وأرقامًا وشرطة من 3 إلى 80 حرفًا.");(event.currentTarget.elements.namedItem("slug") as HTMLInputElement|null)?.focus();return;}const password=String(form.get("password")??"");if((password.length<12||password.length>128||!/[A-Z]/.test(password)||!/[a-z]/.test(password)||!/[0-9]/.test(password)||!/[\W_]/.test(password))){setMessage("كلمة مرور المالك يجب أن تكون من 12 إلى 128 حرفًا، وتحتوي على حرف إنجليزي كبير وصغير ورقم ورمز.");(event.currentTarget.elements.namedItem("password") as HTMLInputElement|null)?.focus();return;}setBusy(true);setMessage("");try{const response=await platformRequest("/api/v1/platform/academies","POST",{requestId,slug,arabicName:form.get("name"),currency:form.get("currency"),timeZone:form.get("zone"),ownerName:form.get("ownerName"),ownerEmail:form.get("ownerEmail"),ownerPhone:form.get("ownerPhone"),initialPassword:form.get("password")??"",template:form.get("template")});const value=await response.json();setSessionExpired(false);setCreate(false);setSelected(value.id);refresh();}catch(e){setSessionExpired(e instanceof PlatformSessionExpiredError);setMessage(e instanceof Error?e.message:"تعذر حفظ المسودة.");}finally{setBusy(false);}}
 if (!ready) return <main className={styles.console}>
  <h1>إدارة المنصة</h1>
  {accessError ? <div role="alert" className={styles.error}>{accessError}</div> : <p role="status">جارٍ التحقق من حساب المنصة…</p>}
  <div className={styles.actions}>
   {accessError && <button className="secondary-button" onClick={() => setAccessAttempt(n => n + 1)}>إعادة المحاولة</button>}
   <Link href="/platform/login">دخول المنصة</Link>
  </div>
 </main>;
 return <main className={styles.console}><header className={styles.top}><div><small><ShieldCheck size={16}/> iAcademy One</small><h1>إدارة المنصة</h1></div><div className={styles.actions}><button className="secondary-button" onClick={async()=>{try{await platformRequest("/api/v1/platform/auth/logout","POST");router.replace("/platform/login");}catch{setMessage("تعذر الخروج. أعد المحاولة.");}}}><LogOut size={18}/> خروج</button></div></header>
 {message&&<div className={styles.error} role="alert" ref={errorRef} tabIndex={-1}>{message}{sessionExpired&&<p><a href="/platform/login" target="_blank" rel="noopener">تسجيل الدخول في نافذة أخرى</a><br/><small>بعد الدخول، عُد لهذه الصفحة واضغط حفظ مسودة ومتابعة التجهيز. لا تُعد تحميل هذه الصفحة حتى لا تفقد بياناتك.</small></p>}</div>}
 {selected?<PlatformAcademyEditor key={selected} academyId={selected} onBack={()=>{setSelected(null);refresh();}} onChanged={refresh}/>:<>
 {directory&&<section className={styles.stats} aria-label="ملخص الأكاديميات">{[["كل الأكاديميات",directory.totals.all],["نشطة",directory.totals.active],["قيد التجهيز",directory.totals.preparing],["الوصول معلّق",directory.totals.suspended],["اشتراكات تحتاج متابعة",directory.totals.reminders]].map(([label,value])=><article key={label}><strong>{value}</strong><span>{label}</span></article>)}</section>}
 <details className={styles.help}><summary>كيف أستخدم هذه الصفحة؟</summary><p>ابحث باسم الأكاديمية أو المالك أو هاتفه، وافتح الأكاديمية لإكمال الهوية والفروع والخدمات والاستيراد. حفظ المسودة لا يتيح الدخول؛ راجع الجاهزية ثم فعّلها صراحة. رسوم المنصة تخص اشتراك الأكاديمية في iAcademy One. لترتيبها اختر عملة واحدة وفترة فوترة واحدة. يذكّرك الملخص بالاشتراكات التي تنتهي خلال أسبوعين أو انتهت بالفعل.</p></details>
 <section className={styles.panel}><div className={styles.heading}><h2><Building2 size={22}/> الأكاديميات</h2><button className="primary-button" onClick={()=>{setCreate(x=>!x);setRequestId(crypto.randomUUID());}}><Plus size={18}/> إنشاء أكاديمية</button></div>
 {create&&<form method="post" className={styles.form} onSubmit={provision}><label>اسم الأكاديمية<input name="name" required maxLength={200}/></label><label>كود الرابط<input name="slug" required minLength={3} maxLength={80} pattern={"[A-Za-z0-9][A-Za-z0-9\\-]{2,79}"} dir="ltr" placeholder="arm-academy" autoCapitalize="none" autoCorrect="off" title="رمز من 3 إلى 80 حرفًا إنجليزيًا صغيرًا أو رقمًا أو شرطة، دون رابط كامل" onInvalid={e=>e.currentTarget.setCustomValidity("أدخل رمزًا مثل arm أو arm-academy، دون http:// أو رقم بورت.")} onInput={e=>e.currentTarget.setCustomValidity("")}/></label><label>التخصص<select name="template" defaultValue="multi-sport"><option value="multi-sport">متعدد الرياضات</option><option value="swimming">سباحة</option><option value="football">كرة قدم</option></select></label><label>العملة<select name="currency">{["EGP","SAR","AED","USD"].map(x=><option key={x}>{x}</option>)}</select></label><label>المنطقة الزمنية<input name="zone" defaultValue="Africa/Cairo" required/></label><label>اسم المالك<input name="ownerName" required maxLength={160}/></label><label>هاتف المالك<input name="ownerPhone" type="tel" dir="ltr" required autoComplete="off"/></label><label>بريد المالك<input name="ownerEmail" type="email" required autoComplete="off"/></label><label>كلمة مرور أولية<input name="password" type="password" aria-label="كلمة مرور أولية" required minLength={12} maxLength={128} autoComplete="new-password" aria-describedby="owner-password-help"/><small id="owner-password-help">من 12 إلى 128 حرفًا، بها حرف إنجليزي كبير وصغير ورقم ورمز.</small></label><button disabled={busy} className="primary-button">{busy?"جارٍ الحفظ…":"حفظ مسودة ومتابعة التجهيز"}</button></form>}
 <div className={styles.filters}><label>البحث<input value={filters.search} placeholder="اسم الأكاديمية أو المالك أو الهاتف" onChange={e=>filter("search",e.target.value)}/></label><label>حالة التشغيل<select value={filters.state} onChange={e=>filter("state",e.target.value)}><option value="">كل الحالات</option>{["Draft","Active","Suspended"].map(x=><option key={x} value={x}>{stateLabels[x]}</option>)}</select></label><details className={styles.filterDetails}><summary>التصفية والترتيب</summary><div className={styles.filters}><label>اشتراك المنصة<select value={filters.commercial} onChange={e=>filter("commercial",e.target.value)}><option value="">كل الاشتراكات</option>{["Unspecified","Trial","Subscribed","Expired"].map(x=><option key={x} value={x}>{stateLabels[x]}</option>)}</select></label><label>عملة الرسوم<select value={filters.currency} onChange={e=>filter("currency",e.target.value)}><option value="">كل العملات</option>{["EGP","SAR","AED","USD"].map(x=><option key={x}>{x}</option>)}</select></label><label>فترة الفوترة<select value={filters.period} onChange={e=>filter("period",e.target.value)}><option value="">كل الفترات</option><option value="Monthly">شهري</option><option value="Yearly">سنوي</option></select></label><label>ترتيب حسب<select value={filters.sort} onChange={e=>filter("sort",e.target.value)}><option value="name">الاسم</option><option value="players">اللاعبين</option><option value="branches">الفروع</option><option value="sports">الرياضات</option><option value="fee" disabled={!filters.currency||!filters.period}>رسوم المنصة</option></select></label><label>اتجاه الترتيب<select value={filters.direction} onChange={e=>filter("direction",e.target.value)}><option value="asc">تصاعدي</option><option value="desc">تنازلي</option></select></label></div></details></div>
 {directoryFailure ? <div className={styles.error} role="alert">
  <p>{directoryFailure.message}</p>
  <div className={styles.actions}>
   {directoryFailure.needsLogin && <a href="/platform/login" target="_blank" rel="noopener">تسجيل الدخول في نافذة أخرى</a>}
   <button className="secondary-button" onClick={refresh}>إعادة تحميل الأكاديميات</button>
  </div>
 </div> : <>
 <div className={styles.tableWrap} aria-busy={loading}><table className={`${styles.table} ${styles.directoryTable}`}><caption className="sr-only">قائمة الأكاديميات الفعلية</caption><thead><tr>{["الأكاديمية","المالك وهاتفه","الموقع","التشغيل","الفروع","الرياضات","لاعب مختلف","اشتراك المنصة"].map(x=><th key={x} scope="col">{x}</th>)}</tr></thead><tbody>{directory?.items.map(x=><tr key={x.id}><td><button className={styles.academy} onClick={()=>setSelected(x.id)}>{x.hasLogo?<Image unoptimized width={44} height={44} src={`/api/v1/platform/academies/${x.id}/logo`} alt=""/>:<Building2 size={36}/>}<span><strong>{x.displayName}</strong><small>{x.slug}</small></span></button></td><td data-label="المالك">{x.ownerName||"يلزم تعيين مالك"}<br/><span dir="ltr">{x.ownerPhone||"الهاتف غير مكتمل"}</span></td><td data-label="الموقع">{x.location||"غير مكتمل"}</td><td data-label="التشغيل"><span className={styles.status}>{stateLabels[x.state]}</span></td><td data-label="الفروع">{x.branches}</td><td data-label="الرياضات">{x.sports}</td><td data-label="لاعب مختلف">{x.players}</td><td data-label="اشتراك المنصة">{stateLabels[x.commercialState]}<br/>{x.fee===null?"رسوم غير محددة":`${Number(x.fee).toLocaleString("ar-EG")} ${x.feeCurrency} / ${x.billingPeriod==="Monthly"?"شهر":"سنة"}`}</td></tr>)}</tbody></table>{!loading&&directory?.items.length===0&&<p className={styles.empty}>لا توجد أكاديميات تطابق البحث.</p>}</div>
 <nav className={styles.pager} aria-label="صفحات الأكاديميات"><button disabled={loading||filters.page===1} onClick={()=>setFilters(x=>({...x,page:x.page-1}))}>السابق</button><span role="status">{loading?"جارٍ التحميل…":`${directory?.count??0} أكاديمية · صفحة ${filters.page}`}</span><button disabled={loading||filters.page*15>=(directory?.count??0)} onClick={()=>setFilters(x=>({...x,page:x.page+1}))}>التالي</button></nav></>}</section></>}
 </main>;
}
