"use client";
import {useRef,useState}from"react";
import {Button,ErrorState}from"../components/dashboard-shell";
import {csrfRequest}from"./dashboard-api";
import {useGoalData}from"./player-goals";
import styles from"./group-media-privacy.module.css";
type Policy={retention:{days:number;deleteOnExpiry:boolean}|null;storedBytes:number|null;storageLimitBytes:number;downloadLimitBytes:number;workerEnabled:boolean;notice:string};
const mb=(n:number)=>(n/1048576).toLocaleString("en-US",{maximumFractionDigits:1});
export function MediaStorageNotice({revision=0}:{revision?:number}){
 const{data,error}=useGoalData<Policy>("/api/v1/assets/storage-policy",revision,"تعذر تحميل سياسة الوسائط.");
 if(error)return <ErrorState message={error}/>;if(!data?.notice)return null;
 return <details className="detail-card"><summary>الاحتفاظ وحدود الوسائط</summary><p>{data.notice}</p><p>{data.retention?`الرفع الجديد: ${data.retention.days} يومًا، ${data.retention.deleteOnExpiry?"مع طلب الإزالة بعد الانتهاء":"حجب دون إزالة آلية"}.`:"لا يوجد موعد انتهاء آلي للرفع الجديد."} {!data.workerEnabled&&"عامل التنظيف الآلي غير مشغّل؛ لا نفترض إزالة الملفات المنتهية."}</p>{data.storedBytes!=null&&<p>تخزين الأكاديمية: <bdi>{mb(data.storedBytes)} / {mb(data.storageLimitBytes)}</bdi> ميجابايت.</p>}<p>حد تنزيل حسابك: <bdi>{mb(data.downloadLimitBytes)}</bdi> ميجابايت محجوزة لكل يوم UTC؛ طلب الجزء يحتسب حجم الملف كاملًا.</p></details>;
}
export function MediaErasure({id,onSaved}:{id:string;onSaved:()=>void}){
 const[open,setOpen]=useState(false),[note,setNote]=useState(""),[busy,setBusy]=useState(false),[error,setError]=useState("");const intent=useRef<{note:string;id:string}|null>(null);
 async function save(){if(busy)return;const n=note.trim();if(intent.current?.note!==n)intent.current={note:n,id:crypto.randomUUID()};setBusy(true);setError("");try{const response=await csrfRequest(`/api/v1/assets/${id}/delete`,"POST",{requestId:intent.current.id,note:n||null},{},{announce:false});const r=await response.json();if(r?.erased!==true)throw new Error();onSaved();}catch{setError("لم نؤكد الحذف. حدّث المعرض قبل إعادة المحاولة؛ لا تستبدل الطلب القديم.");}finally{setBusy(false);}}
 return !open?<Button onClick={()=>setOpen(true)}>حذف ملف الوسائط</Button>:<div className={styles.withdrawal}><p>إجراء نهائي: تُحجب النسخة عن الجميع ويُطلب إزالة ملفها، مع إبقاء سجل النشر والحذف. لا يمكن استرجاعها من التطبيق ولا استرجاع تنزيل سابق. هذا ليس سحب النشر فقط.</p><label>ملاحظة الحذف (اختياري)<textarea value={note} disabled={busy} maxLength={500} onChange={e=>setNote(e.target.value)}/></label>{error&&<ErrorState message={error}/>}<div className="row-actions"><Button disabled={busy} onClick={()=>void save()}>تأكيد حذف الملف نهائيًا</Button><Button disabled={busy} onClick={()=>setOpen(false)}>رجوع دون حذف</Button></div></div>;
}
