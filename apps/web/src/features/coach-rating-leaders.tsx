"use client";
import Link from "next/link";
import {useEffect,useState} from "react";
import {SectionCard} from "../components/dashboard-shell";
type Coach={id:string;name:string;averageRating:number;reviewCount:number};
type Rankings={highest:Coach[];lowest:Coach[];minimumReviews:number;insufficientSampleCount:number};
export function CoachRatingLeaders({from="",to="",branchId=""}:{from?:string;to?:string;branchId?:string}){
  const [data,setData]=useState<Rankings|null>(null),[error,setError]=useState(false);
  useEffect(()=>{let c:AbortController;const load=()=>{c?.abort();c=new AbortController();const signal=c.signal;setData(null);if(document.hidden)return;const q=new URLSearchParams();if(from)q.set("from",from);if(to)q.set("to",to);if(!from&&!to)q.set("allDates","true");if(branchId)q.set("branchId",branchId);fetch(`/api/v1/manage/feedback/coach-rankings?${q}`,{signal,cache:"no-store"}).then(async r=>{if(!r.ok)throw new Error();const v=await r.json();if(!Array.isArray(v?.highest)||!Array.isArray(v?.lowest)||typeof v?.minimumReviews!=="number")throw new Error();return v as Rankings;}).then(d=>{if(!signal.aborted){setData(d);setError(false);}}).catch(()=>{if(!signal.aborted)setError(true);});};load();document.addEventListener("visibilitychange",load);window.addEventListener("focus",load);return()=>{c?.abort();document.removeEventListener("visibilitychange",load);window.removeEventListener("focus",load);};},[from,to,branchId]);
  const q=new URLSearchParams({kind:"Rating"});if(branchId)q.set("branchId",branchId);if(from)q.set("from",from);if(to)q.set("to",to);
  const list=(items:Coach[])=>items.length?<ol className="vip-member-list">{items.map(c=><li key={c.id}><Link href={`/dashboard/feedback?${q}&coachId=${c.id}`}>{c.name}</Link><span><strong>{c.averageRating.toLocaleString("ar-EG-u-nu-latn")} / 5</strong><small> · {c.reviewCount} تقييمات</small></span></li>)}</ol>:<p>لا توجد عينة تقييم كافية في الفترة المختارة.</p>;
  return <section className="owner-chart-grid" aria-label="تقييمات المدربين"><SectionCard title="أعلى 5 مدربين تقييمًا" action={<Link href={`/dashboard/feedback?${q}`}>عرض الكل</Link>}>{error?<p>تعذر تحميل التقييمات.</p>:data?<><small>{data.minimumReviews} تقييمات على الأقل لكل مدرب</small>{list(data.highest)}</>:<p>جارٍ التحميل…</p>}</SectionCard><SectionCard title="أقل 5 مدربين تقييمًا">{data?<><small>نفس الفترة والحد الأدنى للعينة؛ ليست عقوبة آلية.</small>{list(data.lowest)}{data.insufficientSampleCount>0&&<small>{data.insufficientSampleCount} مدربين بعينة غير كافية للترتيب.</small>}</>:<p>{error?"تعذر تحميل التقييمات.":"جارٍ التحميل…"}</p>}</SectionCard></section>;
}
export function CoachRatingStars({value,onChange,disabled=false}:{value:number;onChange:(n:number)=>void;disabled?:boolean}){
  return <fieldset className="rating-stars"><legend>التقييم</legend>{[1,2,3,4,5].map(n=><label key={n}><input type="radio" name="coach-rating" value={n} checked={value===n} disabled={disabled} onChange={()=>onChange(n)}/><span aria-hidden="true" className={n<=value?"selected":""}>★</span><span className="sr-only">{n} من 5</span></label>)}</fieldset>;
}
