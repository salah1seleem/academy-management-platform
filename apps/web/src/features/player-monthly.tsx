"use client";
import Link from "next/link";
import {useState} from "react";
import {CalendarDays,FileDown,Milestone} from "lucide-react";
import {ErrorState,LoadingState,PageHeader,SectionCard,Button} from "../components/dashboard-shell";
import {GuardianShell} from "../components/guardian-shell";
import {useGoalData} from "./player-goals";
import styles from "./player-monthly.module.css";
type Entry={id:string;kind:string;on:string;title:string;state:string;value:number|null;unit:string|null;note:string|null;actor:string;planId:string|null};
type Report={enrollment:{id:string;player:string;sport:string};enrollments:{id:string;sport:string;branch:string}[];enrollmentsHasMore:boolean;month:string;from:string;to:string;asOfDate:string;page:number;counts:{skills:number;badges:number;certificates:number};items:Entry[];hasMore:boolean;notice:string};
const kinds:Record<string,string>={Skill:"مراجعة مهارة",Badge:"شارة مجهود",Certificate:"شهادة إنجاز"};
const states:Record<string,string>={Mastered:"أتقن",Learning:"قيد التعلم",NotAssessed:"لم يقيّم",Revoked:"شارة مسحوبة",Granted:"شارة ممنوحة",Withdrawn:"شهادة مسحوبة",Issued:"شهادة صادرة"};
const units:Record<string,string>={Checklist:"قائمة تحقق",Seconds:"ثانية",Metres:"متر",Count:"عدد",Encouragement:"نقاط تشجيعية غير مالية"};
export function PlayerMonthly({playerId,enrollmentId}:{playerId?:string;enrollmentId?:string}){
 const[month,setMonth]=useState(""),[selected,setSelected]=useState(enrollmentId??""),[page,setPage]=useState(0);
 const base=playerId?`/api/v1/guardian/children/${playerId}/monthly`:`/api/v1/evaluations/monthly/enrollments/${enrollmentId}`;
 const query=new URLSearchParams({page:String(page)});if(month)query.set("month",month);if(playerId&&selected)query.set("enrollmentId",selected);
 const{data,error}=useGoalData<Report>(`${base}?${query}`,0,"تعذر تحميل التقرير. راجع الشهر وصلاحيتك لملف اللاعب.");
 function source(r:Entry,d:Report){if(r.kind==="Skill")return playerId?`/guardian/children/${playerId}/skills/plans/${r.planId}`:`/dashboard/evaluations/skills/plans/${r.planId}`;if(r.kind==="Certificate")return playerId?`/guardian/children/${playerId}/certificates/${r.id}`:`/dashboard/evaluations/certificates/${r.id}`;return playerId?`/guardian/children/${playerId}/badges`:`/dashboard/evaluations/badges?enrollmentId=${d.enrollment.id}`;}
 const body=<div className={styles.report}><PageHeader title="تقرير التطور الشهري" context="كل خطوة لها دليل"/>{error&&<ErrorState message={error}/>} {!data?!error&&<LoadingState/>:<>
 <section className={styles.hero}><CalendarDays aria-hidden/><div><h2>{data.enrollment.player}</h2><p>{data.enrollment.sport} · <bdi>{data.month}</bdi></p><small>من <bdi>{data.from}</bdi> إلى <bdi>{data.to}</bdi> · قراءة حية، ليست أرشيفًا ثابتًا</small></div></section>
 <form className={`form-shell ${styles.filters}`} onSubmit={e=>{e.preventDefault();const f=new FormData(e.currentTarget);setMonth(String(f.get("month")));setSelected(String(f.get("enrollment")??selected));setPage(0);}}>
 <label>شهر التقرير<input type="month" name="month" required min="1900-01" max={data.asOfDate.slice(0,7)} defaultValue={data.month}/></label>
 {playerId&&<label>التسجيل الرياضي<select name="enrollment" defaultValue={data.enrollment.id}>{data.enrollments.map(e=><option key={e.id} value={e.id}>{e.sport} · {e.branch}</option>)}</select></label>}<Button type="submit">عرض الشهر</Button></form>
 <p className={styles.notice}>{data.notice}</p><div className={styles.stats}><div><b>{data.counts.skills}</b>مراجعة مهارة</div><div><b>{data.counts.badges}</b>شارة مسجلة</div><div><b>{data.counts.certificates}</b>شهادة مسجلة</div></div>
 <div className={styles.actions}><a className="button button-secondary" href={`${base}?${new URLSearchParams({enrollmentId:data.enrollment.id,month:data.month,format:"csv"})}`}><FileDown aria-hidden/>تصدير الشهر CSV</a><span>التصدير يعيد فحص الصلاحية ويشمل الشهر كله، لا الصفحة فقط. احتفظ بالملف خصوصيًا؛ لا يسترجع سحبٌ لاحق نسخة سبق تنزيلها.</span></div>
 {!data.items.length&&<SectionCard title="لا توجد أدلة في هذه الصفحة"><p>عدم وجود مراجعات ليس حكمًا على قدرات الطفل. راجع شهرًا آخر أو فريق التدريب.</p></SectionCard>}
 <div className={styles.timeline}>{data.items.map(r=><SectionCard key={`${r.kind}:${r.id}`} title={r.title}><small><Milestone size={16} aria-hidden/> {kinds[r.kind]??"دليل"} · <bdi>{r.on}</bdi></small><p><strong>{states[r.state]??"راجع المصدر"}</strong>{r.value!=null&&<> · <bdi>{r.value}</bdi> <span>{units[r.unit??""]??"قياس — راجع المصدر"}</span></>}</p>{r.note&&<p>{r.note}</p>}<p>سجلها: {r.actor}</p><Link href={source(r,data)}>فتح المصدر وسجله</Link></SectionCard>)}</div>
 <nav className={styles.actions} aria-label="صفحات أدلة الشهر"><Button variant="secondary" disabled={page===0} onClick={()=>setPage(n=>n-1)}>الصفحة السابقة</Button><span>صفحة {page+1} · حتى25 دليلًا من كل نوع</span><Button variant="secondary" disabled={!data.hasMore} onClick={()=>setPage(n=>n+1)}>الصفحة التالية</Button></nav>
 {data.enrollmentsHasMore&&<p>تظهر أول100 تسجيل رياضي. افتح التسجيل الآخر من ملف اللاعب.</p>}
 </>}</div>;
 return playerId?<GuardianShell>{body}</GuardianShell>:body;
}
