"use client";
import Link from "next/link";
import {useEffect,useState} from "react";
import {ErrorState,LoadingState,PageHeader} from "../components/dashboard-shell";
import {ApprovedWorkspace} from "../components/approved-workspace";
import {RecordTabs} from "../components/record-tabs";
import {csrfRequest} from "./dashboard-api";
import {TransferSettings} from "./manual-payments";
import {OwnerHomePreferences} from "./owner-home-settings";
import {PageVideoSettings} from "./page-video-settings";
import {VersionedConfiguration} from "./versioned-configuration";
import {BranchCheckInSite} from "./branch-attendance-policy";
import styles from "./academy-settings.module.css";
type Brand={displayName:string;coachAttendanceEnabled:boolean;currency?:string};
export function AcademySettings(){
 const [brand,setBrand]=useState<Brand|null>(null),[permissions,setPermissions]=useState<string[]>([]),[owner,setOwner]=useState(false),[message,setMessage]=useState(''),[error,setError]=useState(''),[busy,setBusy]=useState(false);
 const canManage=permissions.includes('academy.settings.manage');
 useEffect(()=>{const c=new AbortController();fetch('/api/v1/academy/experience',{signal:c.signal}).then(r=>r.ok?r.json():Promise.reject()).then(setBrand).catch(()=>{if(!c.signal.aborted)setError('تعذر تحميل إعدادات الأكاديمية.');});fetch('/api/v1/my-permissions',{signal:c.signal}).then(r=>r.ok?r.json():[]).then(p=>{if(!c.signal.aborted)setPermissions(Array.isArray(p)?p:[]);}).catch(()=>{});fetch('/api/v1/me',{signal:c.signal}).then(r=>r.ok?r.json():{}).then((me:{role?:string})=>{if(!c.signal.aborted)setOwner(me.role==='AcademyOwner');}).catch(()=>{});return()=>c.abort();},[]);
 async function policy(value:boolean){if(busy)return;setBusy(true);setMessage('');setError('');try{await csrfRequest('/api/v1/manage/academy/experience','PUT',{coachAttendanceEnabled:value});const r=await fetch('/api/v1/academy/experience',{cache:'no-store'});if(!r.ok)throw Error();const next=await r.json() as Brand;if(next.coachAttendanceEnabled!==value)throw Error();setBrand(next);setMessage('تم حفظ سياسة حضور المدرب وإعادة قراءتها.');}catch{setError('تعذر حفظ سياسة الحضور أو التحقق منها.');}finally{setBusy(false);}}
 const tabs=brand?[
  {id:'academy',label:'بيانات الأكاديمية',content:<section className={styles.panel}><h2>{brand.displayName}</h2><dl><div><dt>اسم الأكاديمية</dt><dd>{brand.displayName}</dd></div><div><dt>عملة الأكاديمية</dt><dd>{brand.currency?({EGP:'الجنيه المصري',SAR:'الريال السعودي',AED:'الدرهم الإماراتي',USD:'الدولار الأمريكي'} as Record<string,string>)[brand.currency]??brand.currency:'غير متاحة'}</dd></div></dl><details><summary>هوية الأكاديمية والعملة</summary><p>يدير الاسم والشعار والألوان مشغّل المنصة بصلاحية مستقلة. تُحفظ العملة مع السجلات المالية؛ لا تغيّر هذه الصفحة قيم السجلات السابقة.</p></details></section>},
  {id:'operations',label:'التشغيل والحضور',content:<><section className={styles.panel}><h2>حضور اللاعبين</h2>{canManage?<label className={styles.toggle}><span>السماح للمدرب بتسجيل حضور اللاعبين من التطبيق</span><input type="checkbox" checked={brand.coachAttendanceEnabled} disabled={busy} onChange={e=>void policy(e.target.checked)}/></label>:<p>لا تملك صلاحية تعديل سياسة الحضور.</p>}<details><summary>أثر إعداد الحضور</summary><p>يُطبق الإعداد على الخادم والموبايل، وتبقى التقييمات والمواعيد متاحة عند إيقاف تسجيل الحضور.</p></details></section>{canManage&&<BranchAttendanceSettings/>}<section className={styles.panel}><h2>خصائص الرياضات</h2><Link href="/dashboard/settings/sports">ضبط تشغيل الرياضات والفروع ←</Link></section></>},
  {id:'finance',label:'المالية والتحصيل',content:<><TransferSettings expanded/><section className={styles.panel}><h2>هدف التحصيل</h2><p>احفظ الهدف من ملخص الرئيسية للفترة والفرع المطلوبين.</p><Link href="/dashboard">فتح ملخص الأكاديمية ←</Link></section>{owner&&<section className={styles.panel}><h2>التشغيل النقدي</h2><Link href="?tab=advanced">إدارة النقدي وإصدارات إعدادات التشغيل ←</Link></section>}</>},
  {id:'appearance',label:'المظهر والرئيسية',content:owner?<><OwnerHomePreferences/><PageVideoSettings/></>:<section className={styles.panel}>إعدادات الرئيسية وشرح الصفحات متاحة للمالك.</section>},
  ...(permissions.includes('evaluation.criteria.manage')?[{id:'criteria',label:'معايير التقييم',content:<section className={styles.panel}><h2>معايير التقييم حسب الرياضة</h2><Link href="/dashboard/evaluations/criteria">إدارة معايير التقييم ←</Link></section>}]:[]),
  ...(canManage?[{id:'advanced',label:'إعدادات متقدمة',content:<VersionedConfiguration/>}]:[]),
 ]:[];
 return <ApprovedWorkspace><div className={styles.settings}><PageHeader title="إعدادات الأكاديمية" context="الأكاديمية"/>{message&&<p role="status">{message}</p>}{error&&<ErrorState message={error}/>}<RecordTabs label="أقسام إعدادات الأكاديمية" tabs={tabs}/>{!brand&&!error&&<LoadingState/>}</div></ApprovedWorkspace>;
}
function BranchAttendanceSettings(){
 const [branches,setBranches]=useState<{id:string;arabicName:string}[]|null>(null),[selected,setSelected]=useState(''),[error,setError]=useState('');
 useEffect(()=>{const c=new AbortController();fetch('/api/v1/manage/structure/options',{signal:c.signal,cache:'no-store'}).then(async r=>{if(r.status===403)return {branches:[]};if(!r.ok)throw Error();return r.json();}).then(d=>{if(!c.signal.aborted)setBranches(d.branches);}).catch(()=>{if(!c.signal.aborted)setError('تعذر تحميل الفروع المتاحة.');});return()=>c.abort();},[]);
 return <section className={styles.panel}><h2>حضور وانصراف الموظفين</h2>{error&&<ErrorState message={error}/>}<label>الفرع<select value={selected} onChange={e=>setSelected(e.target.value)}><option value="">اختر الفرع</option>{branches?.map(b=><option key={b.id} value={b.id}>{b.arabicName}</option>)}</select></label>{selected&&<BranchCheckInSite id={selected}/>}</section>;
}
