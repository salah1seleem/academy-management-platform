"use client";
import Link from "next/link";
import { FormEvent, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Button, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { GuardianShell } from "../components/guardian-shell";
import { csrfRequest } from "./dashboard-api";
import { useGoalData } from "./player-goals";
import styles from "./player-skills.module.css";

const root = "/api/v1/evaluations/skills";
const units: Record<string,string> = { Checklist: "مراجعة فنية", Count: "عدد", Seconds: "ثانية", Metres: "متر" };
const states: Record<string,string> = { NotAssessed: "لم يُقيّم", Learning: "قيد التعلم", Mastered: "أتقن" };
const published: Record<string,string> = { Draft: "مسودة", Published: "منشور", Retired: "متوقف مع حفظ السجل" };
type Requirement = { id: string; name: string; unit: string; protocol: string };
type Curriculum = { id: string; trainingLevelId: string; title: string; levelNameSnapshot: string; revisionNumber: number; version: number; state: string };
type Review = { id: string; requirementId: string; state: string; measuredValue: number|null; note: string|null; observedOn: string; actorName: string };
type Plan = { id: string; version: number; assignedOn: string; curriculum: Curriculum };
type Context = { id: string; playerId: string; player: string; sport: string; level: string|null };
type PlanData = { enrollment: Context; plan: Plan; asOfDate: string; contextChanged: boolean; canReview: boolean;
  progress: { mastered: number; assessed: number; total: number; percentage: number|null; sourcePercentage: number|null; readyForTechnicalReview: boolean };
  skills: { requirement: Requirement; latest: Review|null; baseline: Review|null }[]; history: Review[]; historyHasMore: boolean };
type Journey = { enrollment: Context; asOfDate: string; canAssign: boolean; availableCurricula: Curriculum[]; active: PlanData|null; history: Plan[]; historyHasMore: boolean };
type Options = { canCreate: boolean; canEdit: boolean; canCreateLevel: boolean; levels: {id:string; arabicName:string; sport:string}[]; sports: {id:string;name:string}[] };
function useSkills<T>(path:string,revision=0) {return useGoalData<T>(path,revision,"خطة المهارات أو اللاعب غير متاح لك الآن. أعد فتح ملف اللاعب.");}
function useCommand(onSaved:(data:{id:string})=>void) {
  const [busy,setBusy]=useState(false),[error,setError]=useState(""); const pending=useRef<{key:string;id:string}|null>(null);
  async function save(path:string,body:object,method:"POST"|"PUT"="POST") {
    if(busy)return; setBusy(true);setError("");const key=JSON.stringify({path,body,method});if(pending.current?.key!==key)pending.current={key,id:crypto.randomUUID()};
    try {const r=await csrfRequest(path,method,{...body,requestId:pending.current.id});const data=await r.json() as {id:string};pending.current=null;onSaved(data);}catch(e){setError(e instanceof Error?e.message:"تعذر الحفظ. لم نسجل نجاحًا.");}finally{setBusy(false);}
  }
  return {busy,error,save};
}
export function SkillCurricula() {
  const [level,setLevel]=useState("");const {data:options,error:oError}=useSkills<Options>(root+"/options");
  const {data,error}=useSkills<{items:Curriculum[];hasMore:boolean}>(root+"/curricula"+(level?`?levelId=${level}`:""));
  return <><PageHeader title="مناهج المهارات" context="التقييمات والتطور"/>{(error||oError)&&<ErrorState message={error||oError}/>}
    <SectionCard title="متطلبات واضحة لكل مستوى"><p>نسخة منشورة ثابتة، وأدلة فنية بتاريخ ومدرب. النسبة تحسب المتطلبات المتقنة فقط، ولا تغيّر المستوى أو المجموعة تلقائيًا.</p>
      {options?.canCreate&&<Link className="button button-primary" href="/dashboard/evaluations/skills/new">منهج جديد</Link>}
      <label>المستوى الرياضي<select value={level} onChange={e=>setLevel(e.target.value)}><option value="">كل المستويات</option>{options?.levels.map(l=><option key={l.id} value={l.id}>{l.sport} · {l.arabicName}</option>)}</select></label>
    </SectionCard>{!data?!error&&<LoadingState/>:data.items.map(c=><SectionCard key={c.id} title={c.title}><p>{c.levelNameSnapshot} · نسخة{c.revisionNumber} · {published[c.state]}</p><Link className="button button-secondary" href={`/dashboard/evaluations/skills/curricula/${c.id}`}>فتح المنهج</Link></SectionCard>)}
    {data&&!data.items.length&&<p>لا توجد نسخ مطابقة. يجهز المسؤول الفني منهج المستوى ثم ينشره.</p>}{data?.hasMore&&<p>نعرض أحدث100 نسخة؛ اختر مستوى لتضييق القائمة.</p>}
  </>;
}
type Definition = { item:Curriculum; requirements:Requirement[]; canEdit:boolean;canCopy:boolean };
export function SkillCurriculumEditor({id}:{id?:string}) {
  const router=useRouter(),[revision,setRevision]=useState(0),[message,setMessage]=useState("");
  const {data:options,error:oError}=useSkills<Options>(root+"/options",revision),{data,error}=useSkills<Definition>(id?`${root}/curricula/${id}`:root+"/options",revision);
  const command=useCommand(result=>{if(!id||result.id!==id)router.push(`/dashboard/evaluations/skills/curricula/${result.id}`);else {setMessage("تم حفظ نسخة المنهج.");setRevision(n=>n+1);}});
  const [extra,setExtra]=useState(0); const c=data?.item, editable=id?data?.canEdit:options?.canCreate;
  const requirements=data?.requirements??[]; const count=Math.min(20,Math.max(1,requirements.length)+extra);
  function submit(e:FormEvent<HTMLFormElement>){e.preventDefault();const f=new FormData(e.currentTarget);void command.save(id?`${root}/curricula/${id}`:root+"/curricula",{title:f.get("title"),trainingLevelId:f.get("level"),version:c?.version??0,requirements:Array.from({length:count},(_,i)=>({name:f.get(`name-${i}`),unit:f.get(`unit-${i}`),protocol:f.get(`protocol-${i}`)}))},id?"PUT":"POST");}
  return <><PageHeader title={id?"منهج المستوى ومراجعات نسخته":"منهج مهارات جديد"} context="التدريب والتطور"/>{(error||oError||command.error)&&<ErrorState message={error||oError||command.error}/>}{message&&<p role="status">{message}</p>}
    <Link className="button button-secondary" href="/dashboard/evaluations/skills">العودة للمناهج</Link>
    {!data||!options?!error&&!oError&&<LoadingState/>:<>
      {c&&<SectionCard title={c.title}><p>{c.levelNameSnapshot} · نسخة{c.revisionNumber} · {published[c.state]}</p></SectionCard>}
      {editable&&(!c||c.state==="Draft")?<form key={`${c?.id}:${c?.version}`} className="form-shell" onSubmit={submit}>
        <label>اسم نسخة المنهج<input name="title" required maxLength={100} defaultValue={c?.title} disabled={command.busy}/></label>
        <label>المستوى الرياضي<select name="level" required defaultValue={c?.trainingLevelId??""} disabled={command.busy||!!c}><option value="">اختر مستوى موجودًا</option>{options.levels.map(l=><option key={l.id} value={l.id}>{l.sport} · {l.arabicName}</option>)}</select></label>
        {c&&<input name="level" type="hidden" value={c.trainingLevelId}/>}
        {Array.from({length:count},(_,i)=><fieldset key={i}><legend>المتطلب{i+1}</legend><label>اسم المهارة<input name={`name-${i}`} required maxLength={120} defaultValue={requirements[i]?.name}/></label><label>وحدة القياس<select name={`unit-${i}`} defaultValue={requirements[i]?.unit??"Checklist"}>{Object.entries(units).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label><label>بروتوكول الملاحظة<textarea name={`protocol-${i}`} required maxLength={500} defaultValue={requirements[i]?.protocol}/></label></fieldset>)}
        <p>الأوزان متساوية؛ لا متوسط بين العدد والزمن والمسافة. عند النشر تثبت المتطلبات. المسودة لا تظهر للأسرة.</p>
        {count<20&&<Button type="button" variant="secondary" onClick={()=>setExtra(n=>n+1)} disabled={command.busy}>إضافة متطلب</Button>}{count>1&&<Button type="button" variant="secondary" onClick={()=>setExtra(n=>n-1)} disabled={command.busy}>إزالة آخر متطلب</Button>}
        <Button type="submit" disabled={command.busy}>حفظ المسودة</Button>
      </form>:<SectionCard title="المتطلبات المنشورة">{requirements.map(r=><article key={r.id}><h3>{r.name} · {units[r.unit]}</h3><p>{r.protocol}</p></article>)}</SectionCard>}
      {c&&data.canEdit&&c.state==="Draft"&&<Button disabled={command.busy} onClick={()=>void command.save(`${root}/curricula/${c.id}/publish`,{version:c.version})}>نشر نسخة المنهج</Button>}
      {c&&data.canCopy&&<Button variant="secondary" disabled={command.busy} onClick={()=>void command.save(root+"/curricula",{trainingLevelId:c.trainingLevelId,title:c.title+" — نسخة جديدة",requirements:requirements.map(({name,unit,protocol})=>({name,unit,protocol})),copiedFromId:c.id,sourceVersion:c.version})}>نسخ إلى مسودة جديدة</Button>}
      {c&&data.canEdit&&c.state!=="Retired"&&<Button variant="danger" disabled={command.busy} onClick={()=>{if(window.confirm("إيقاف النسخة يمنع إسنادها ومراجعاتها الجديدة مع حفظ التاريخ. هل تتابع؟"))void command.save(`${root}/curricula/${c.id}/retire`,{version:c.version});}}>إيقاف النسخة مع حفظ السجل</Button>}
      {!options.levels.length&&<p>أضف مستوى من إعدادات تسكين المجموعات أولًا، ثم عُد لتجهيز منهجه.</p>}
      {!id&&options.canCreateLevel&&<details><summary>إضافة مستوى رياضي للمناهج من الإعدادات القائمة</summary><form className="form-shell" onSubmit={async e=>{e.preventDefault();const f=new FormData(e.currentTarget);try {await csrfRequest("/api/v1/manage/training-levels","POST",{sportId:f.get("sport"),arabicName:f.get("name"),rank:Number(f.get("rank"))});setMessage("تم إضافة المستوى؛ اختره في المنهج.");setRevision(n=>n+1);}catch {setMessage("تعذر إضافة المستوى. راجع الاسم والصلاحية.");}}}><label>رياضة المستوى<select name="sport" required>{options.sports.map(s=><option key={s.id} value={s.id}>{s.name}</option>)}</select></label><label>اسم المستوى<input name="name" required maxLength={100}/></label><label>ترتيب المستوى<input name="rank" type="number" required min="0" defaultValue="1"/></label><Button type="submit">إضافة المستوى</Button></form></details>}
    </>}
  </>;
}
function Progress({data}:{data:PlanData}) {
  const p=data.progress;return <SectionCard title={data.plan.curriculum.title}><p>{data.enrollment.player} · {data.enrollment.sport} · {data.plan.curriculum.levelNameSnapshot} · نسخة{data.plan.curriculum.revisionNumber}</p>
    <h2><bdi>{p.mastered}/{p.total}</bdi> متطلبات متقنة {p.percentage!==null&&<>· <bdi>{p.percentage}%</bdi></>}</h2>
    {p.percentage!==null&&<progress className={styles.progressTrack} max={p.total} value={p.mastered} aria-label="متطلبات متقنة"/>}
    <p>{p.assessed} تم تقييمها؛ لم يُقيّم لا يعني صفرًا. الأوزان متساوية، وهذه ليست درجة قدرة أو احتمال احتراف.</p>
    {data.contextChanged&&<p role="status">هذه خطة سابقة أو تغير مستواها أو توقفت نسختها؛ نتيجتها التاريخية {p.sourcePercentage??"—"}% وليست نسبة المستوى الحالي.</p>}
    {p.readyForTechnicalReview&&<p role="status">استوفى المتطلبات · يحتاج قرار مراجعة فنية بشرية. لا ترقية أو نقل تلقائي.</p>}
  </SectionCard>;
}
function ReviewText({review,unit}:{review:Review|null;unit:string}){return <>{review?<><b>{states[review.state]}{review.measuredValue!==null&&<> · <bdi>{review.measuredValue}</bdi> {units[unit]}</>}</b><p><bdi>{review.observedOn}</bdi> · {review.actorName}</p>{review.note&&<p>{review.note}</p>}</>:<b>لم يُقيّم</b>}</>;}
export function PlayerSkillJourney({enrollmentId}:{enrollmentId:string}) {
  const [revision,setRevision]=useState(0),[message,setMessage]=useState(""),[expectedPlan,setExpectedPlan]=useState<string|null>(null);const {data,error}=useSkills<Journey>(`${root}/enrollments/${enrollmentId}/journey`,revision);const command=useCommand(result=>{setExpectedPlan(result.id);setMessage("تم إسناد الخطة مع حفظ السجل السابق.");setRevision(n=>n+1);});
  return <><PageHeader title="رحلة مهارات اللاعب" context="التدريب والتطور"/>{(error||command.error)&&<ErrorState message={error||command.error}/>} {message&&<p role="status">{message}</p>}
    {!data?!error&&<LoadingState/>:<><SectionCard title={`${data.enrollment.player} · ${data.enrollment.sport}`}><p>المستوى المسجل: {data.enrollment.level??"لم يحدد فنيًا"}</p><Link className="button button-secondary" href={`/dashboard/players/${data.enrollment.playerId}?tab=training`}>ملف اللاعب ومراجعة مستواه</Link></SectionCard>
      {data.canAssign&&<form className="form-shell" onSubmit={e=>{e.preventDefault();const f=new FormData(e.currentTarget);if(data.active&&!window.confirm("استبدال الخطة يبدأ متابعة نسخة جديدة ويحفظ القديمة. هل تتابع؟"))return;void command.save(`${root}/enrollments/${enrollmentId}/skill-plan`,{curriculumId:f.get("curriculum"),previousPlanId:data.active?.plan.id??null,previousVersion:data.active?.plan.version??0});}}><label>نسخة المستوى الحالي<select name="curriculum" required disabled={command.busy}><option value="">اختر أحدث نسخة منشورة</option>{data.availableCurricula.map(c=><option key={c.id} value={c.id}>{c.title} · نسخة{c.revisionNumber}</option>)}</select></label><Button type="submit" disabled={command.busy||!data.availableCurricula.length}>{data.active?"استبدال الخطة مع حفظ التاريخ":"إسناد خطة المهارات"}</Button>{!data.availableCurricula.length&&<p>راجع المستوى الفني من ملف اللاعب ثم جهز وانشر منهجًا لنفس المستوى.</p>}</form>}
      {data.active?<><Progress data={data.active}/>{!command.busy&&(!expectedPlan||data.active.plan.id===expectedPlan)&&<Link className="button button-primary" href={`/dashboard/evaluations/skills/plans/${data.active.plan.id}`}>فتح المهارات والملاحظات</Link>}</>:<p>لم تُسند خطة مهارات بعد؛ لا نحسب نسبة افتراضية.</p>}
      <SectionCard title="الخطط السابقة">{data.history.map(p=><p key={p.id}><Link href={`/dashboard/evaluations/skills/plans/${p.id}`}>{p.curriculum.title} · نسخة{p.curriculum.revisionNumber} · {p.assignedOn}</Link></p>)}{!data.history.length&&<p>لا توجد خطط سابقة.</p>}{data.historyHasMore&&<p>تظهر أحدث20 خطة سابقة.</p>}</SectionCard>
    </>}
  </>;
}
export function PlayerSkillPlan({id,playerId}:{id:string;playerId?:string}) {
  const [revision,setRevision]=useState(0),[message,setMessage]=useState("");const path=playerId?`/api/v1/guardian/children/${playerId}/skills/plans/${id}`:`${root}/plans/${id}`;
  const {data,error}=useSkills<PlanData>(path,revision);const command=useCommand(()=>{setMessage("تم حفظ الملاحظة ونشرها للأسرة المخولة.");setRevision(n=>n+1);});
  const [requirement,setRequirement]=useState(""),[state,setState]=useState("Learning");const unit=data?.skills.find(x=>x.requirement.id===requirement)?.requirement.unit;
  const content=<><PageHeader title="مهارات اللاعب وملاحظات التقدم" context="التدريب والتطور"/>{(error||command.error)&&<ErrorState message={error||command.error}/>} {message&&<p role="status">{message}</p>}
    {!data?!error&&<LoadingState/>:<><Progress data={data}/>{!playerId&&<Link className="button button-secondary" href={`/dashboard/evaluations/skills/journey?enrollmentId=${data.enrollment.id}`}>العودة لرحلة اللاعب</Link>}
      {data.canReview&&!playerId&&<form key={revision} className="form-shell" onSubmit={e=>{e.preventDefault();const f=new FormData(e.currentTarget);void command.save(`${root}/plans/${id}/reviews`,{requirementId:requirement,state,measuredValue:unit==="Checklist"||state==="NotAssessed"?null:Number(f.get("value")),observedOn:f.get("date"),note:f.get("note")||null});}}>
        <label>المهارة<select required value={requirement} onChange={e=>setRequirement(e.target.value)}><option value="">اختر المهارة</option>{data.skills.map(s=><option key={s.requirement.id} value={s.requirement.id}>{s.requirement.name}</option>)}</select></label>
        {requirement&&<p>{data.skills.find(s=>s.requirement.id===requirement)?.requirement.protocol}</p>}
        <label>الحالة الفنية<select value={state} onChange={e=>setState(e.target.value)}>{Object.entries(states).map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label>
        {unit&&unit!=="Checklist"&&state!=="NotAssessed"&&<label>القياس · {units[unit]}<input name="value" type="number" min="0" max="1000000" step={unit==="Count"?"1":"0.001"} required/></label>}
        <label>تاريخ الملاحظة<input name="date" type="date" min={data.plan.assignedOn} max={data.asOfDate} defaultValue={data.asOfDate} required/></label><label>ملاحظة للأسرة (اختياري)<textarea name="note" maxLength={500}/></label>
        <p>تظهر الملاحظة للأسرة المرتبطة. التصحيح بملاحظة جديدة؛ لا تعاد كتابة الدليل السابق.</p><Button type="submit" disabled={command.busy}>حفظ ونشر الملاحظة للأسرة</Button>
      </form>}
      {data.skills.map(s=><SectionCard key={s.requirement.id} title={s.requirement.name}><p>{s.requirement.protocol}</p><ReviewText review={s.latest} unit={s.requirement.unit}/>{s.baseline&&s.baseline.id!==s.latest?.id&&<details><summary>أول ملاحظة</summary><ReviewText review={s.baseline} unit={s.requirement.unit}/></details>}</SectionCard>)}
      <SectionCard title="سجل الملاحظات الفنية"><details><summary>عرض أحدث20 ملاحظة</summary>{data.history.map(r=><article key={r.id}><h3>{data.skills.find(s=>s.requirement.id===r.requirementId)?.requirement.name}</h3><ReviewText review={r} unit={data.skills.find(s=>s.requirement.id===r.requirementId)?.requirement.unit??"Checklist"}/></article>)}{data.historyHasMore&&<p>للسجل الأقدم راجع الإدارة المخولة.</p>}</details></SectionCard>
    </>}
  </>;
  return playerId?<GuardianShell><div className={styles.family}><Link href={`/guardian/children/${playerId}/skills`}>العودة لمهارات ابنك</Link>{content}</div></GuardianShell>:content;
}
export function GuardianPlayerSkills({playerId}:{playerId:string}) {
  const {data,error}=useSkills<{items:Journey[]}>(`/api/v1/guardian/children/${playerId}/skills`);
  return <GuardianShell><div className={styles.family}><header className="guardian-page-header"><h1>خطوات تعلم ابنك</h1><p>أدلة فنية فعلية من مدربيه، وليس توقعًا أو ترقية تلقائية.</p><Link className="button" href={`/guardian/children/${playerId}`}>العودة لملف اللاعب</Link></header>{error&&<ErrorState message={error}/>}{!data?!error&&<LoadingState/>:data.items.map(j=><section key={j.enrollment.id}>{j.active?<><Progress data={j.active}/><Link className="button button-secondary" href={`/guardian/children/${playerId}/skills/plans/${j.active.plan.id}`}>مهارات {j.enrollment.sport} وملاحظات المدرب</Link></>:<p>{j.enrollment.sport} · لم تُنشر خطة مهارات بعد.</p>}{j.history.map(p=><p key={p.id}><Link className="button" href={`/guardian/children/${playerId}/skills/plans/${p.id}`}>خطة سابقة · {p.curriculum.title} · نسخة{p.curriculum.revisionNumber}</Link></p>)}</section>)}</div></GuardianShell>;
}
