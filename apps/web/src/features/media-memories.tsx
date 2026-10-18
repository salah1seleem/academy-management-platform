"use client";
import Image from "next/image";
import Link from "next/link";
import {useState} from "react";
import {Images,FileDown} from "lucide-react";
import {Button,EmptyState,ErrorState,LoadingState,PageHeader} from "../components/dashboard-shell";
import {GuardianShell} from "../components/guardian-shell";
import {useGoalData} from "./player-goals";
import styles from "./player-monthly.module.css";
import media from "./media-memories.module.css";
type Item={id:string;caption:string|null;contentType:string;mediaEventOn:string;trainingSessionId:string|null;retainUntilUtc:string|null;url:string};
type Album={month:string;from:string;to:string;asOfDate:string;page:number;total:number;images:number;videos:number;hasMore:boolean;items:Item[];notice:string};
export function MediaMemories({playerId,groupId,family=true}:{playerId?:string;groupId?:string;family?:boolean}){
 const[month,setMonth]=useState(""),[page,setPage]=useState(0);
 const query=new URLSearchParams({page:String(page)});if(playerId)query.set("playerId",playerId);if(groupId)query.set("groupId",groupId);if(month)query.set("month",month);
 const{data,error}=useGoalData<Album>(`/api/v1/assets/memories?${query}`,0,"تعذر فتح ذكريات هذا النطاق والشهر. راجع صلاحيتك ثم أعد المحاولة.");
 const body=<div className={styles.report}><PageHeader title="ذكريات الشهر" context="لحظات التدريب الخاصة"/>{family&&<Link href={`/guardian/children/${playerId}/gallery`}>العودة للصور والفيديوهات</Link>}{error&&<ErrorState message={error}/>} {!data?!error&&<LoadingState/>:<>
 <section className={styles.hero}><Images aria-hidden/><div><h2>كل لحظة في مكانها</h2><p><bdi>{data.month}</bdi> · جمهور مخول ومراجعة خصوصية</p><small>من <bdi>{data.from}</bdi> إلى <bdi>{data.to}</bdi> حسب تاريخ الواقعة</small></div></section>
 <form className={`form-shell ${styles.filters}`} onSubmit={e=>{e.preventDefault();setMonth(String(new FormData(e.currentTarget).get("month")));setPage(0);}}><label>شهر الذكريات<input type="month" name="month" required min="1900-01" max={data.asOfDate.slice(0,7)} defaultValue={data.month}/></label><Button type="submit">عرض الذكريات</Button></form>
 <details className={styles.notice}><summary>خصوصية الذكريات وحدود الفهرس</summary><p>{data.notice}</p></details>
 <div className={styles.stats}><div><b>{data.total}</b>نسخة متاحة</div><div><b>{data.images}</b>صورة</div><div><b>{data.videos}</b>فيديو</div></div>
 <div className={styles.actions}><a className="button button-secondary" href={`/api/v1/assets/memories?${new URLSearchParams({...Object.fromEntries(query),month:data.month,format:"csv"})}`}><FileDown aria-hidden/>تنزيل فهرس الشهر CSV</a><span>الفهرس بيانات وصفية فقط، دون الصور أو الفيديوهات أو روابط مشاركة. لا يسترجع السحب تنزيلًا سابقًا.</span></div>
 {!data.items.length&&<EmptyState message="لا توجد ذكريات متاحة في هذه الصفحة. قد يتغير العرض مع الموافقة أو السحب أو انتهاء الاحتفاظ."/>}
 <div className={media.grid}>{data.items.map(x=><figure className={media.card} key={x.id}>{x.contentType.startsWith("video/")?<video src={x.url} controls preload="none"/>:<Image unoptimized src={x.url} alt={x.caption??"لحظة تدريب خاصة"} width={640} height={420}/>}<figcaption><small><bdi>{x.mediaEventOn}</bdi> · {x.trainingSessionId?"من حصة فعلية":"واقعة تدريب مسجلة"}</small><h2>{x.caption??"لحظة من التدريب"}</h2>{x.retainUntilUtc&&<p>موعد الاحتفاظ المسجل <bdi>{x.retainUntilUtc.slice(0,10)}</bdi></p>}<a href={x.url} target="_blank" rel="noreferrer">فتح النسخة الخاصة</a></figcaption></figure>)}</div>
 <nav className={styles.actions} aria-label="صفحات الذكريات"><Button variant="secondary" disabled={page===0} onClick={()=>setPage(n=>n-1)}>الصفحة السابقة</Button><span>صفحة {page+1} · حتى25 نسخة</span><Button variant="secondary" disabled={!data.hasMore} onClick={()=>setPage(n=>n+1)}>الصفحة التالية</Button></nav>
 </>}</div>;
 return family?<GuardianShell>{body}</GuardianShell>:body;
}
