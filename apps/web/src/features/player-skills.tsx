"use client";
import Link from "next/link";
import { FormEvent, ReactNode, Suspense, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Button, ConfirmationDialog, EmptyState, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { GuardianShell } from "../components/guardian-shell";
import { formatDateAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { useGoalData } from "./player-goals";
import styles from "./player-skills.module.css";
import {PlayerLevelHistory,type LevelHistory} from "./player-level-history";

const root = "/api/v1/evaluations/skills";
const units: Record<string,string> = { Checklist: "مراجعة فنية", Count: "عدد", Seconds: "ثانية", Metres: "متر" };
const states: Record<string,string> = { NotAssessed: "لم يُقيّم", Learning: "قيد التعلم", Mastered: "أتقن" };
const curriculumStates: Record<string,string> = { Draft: "مسودة", Published: "منشورة", Retired: "موقوفة" };
type Requirement = { id: string; name: string; unit: string; protocol: string };
type Curriculum = { id: string; trainingLevelId: string; title: string; levelNameSnapshot: string; revisionNumber: number; version: number; state: string };
type Review = { id: string; requirementId: string; state: string; measuredValue: number|null; note: string|null; observedOn: string; actorName: string };
type Plan = { id: string; version: number; assignedOn: string; curriculum: Curriculum };
type Context = { id: string; playerId: string; player: string; sport: string; level: string|null };
type PlanData = { enrollment: Context; plan: Plan; asOfDate: string; contextChanged: boolean; canReview: boolean;
  progress: { mastered: number; assessed: number; total: number; percentage: number|null; sourcePercentage: number|null; readyForTechnicalReview: boolean };
  skills: { requirement: Requirement; latest: Review|null; baseline: Review|null }[]; history: Review[]; historyHasMore: boolean };
type Journey = { enrollment: Context; asOfDate: string; canAssign: boolean; availableCurricula: Curriculum[]; active: PlanData|null; history: Plan[]; historyHasMore: boolean;levelHistory?:LevelHistory };
type Options = { canCreate: boolean; canEdit: boolean; canCreateLevel: boolean; levels: {id:string; sportId:string; arabicName:string; sport:string}[]; sports: {id:string;name:string}[] };
function useSkills<T>(path:string,revision=0) {return useGoalData<T>(path,revision,"خطة المهارات أو اللاعب غير متاح لك الآن.");}
function useCommand(onSaved:(data:{id:string},path:string)=>void, options: {announce?:boolean} = {}) {
  const [busy,setBusy]=useState(false),[failure,setFailure]=useState<{message:string;status:number;path:string}|null>(null);
  const pending=useRef<{key:string;id:string;path:string;body:object;method:"POST"|"PUT"}|null>(null), inFlight=useRef(false);
  async function save(path:string,body:object,method:"POST"|"PUT"="POST") {
    if (inFlight.current) return;
    inFlight.current=true; setBusy(true);setFailure(null);const key=JSON.stringify({path,body,method});if(pending.current?.key!==key)pending.current={key,id:crypto.randomUUID(),path,body,method};
    try {const r=await csrfRequest(path,method,{...body,requestId:pending.current.id},{},options);const data=await r.json() as {id:string};if(!data || typeof data.id!=="string" || !data.id)throw Error();pending.current=null;onSaved(data,path);}
    catch(e){setFailure({message:e instanceof Error && /\p{Script=Arabic}/u.test(e.message) ? e.message : "تعذر تأكيد الحفظ. تحقّق من حالة السجل.",status:e instanceof DashboardRequestError ? e.status : 0,path});}
    finally{inFlight.current=false;setBusy(false);}
  }
  function retry() {
    const attempt = pending.current;
    if (attempt) return save(attempt.path, attempt.body, attempt.method);
  }
  return {busy,error:failure?.message ?? "", failure, save, retry, clearError:()=>setFailure(null)};
}
const catalogue = "/dashboard/evaluations/skills";
const uuid = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i;
const levelFilter = (value: string | null) => value && uuid.test(value) ? value : "";
const levelQuery = (level: string) => level ? `?levelId=${level}` : "";

export function SkillCurricula() {
  return <Suspense fallback={<LoadingState/>}><CurriculaCatalogue/></Suspense>;
}
function CurriculaCatalogue() {
  const level = levelFilter(useSearchParams().get("levelId"));
  const query = levelQuery(level);
  const options = useGoalData<Options>(`${root}/options`, 0, "تعذر تحميل المستويات الرياضية.");
  const result = useGoalData<{items:Curriculum[];hasMore:boolean;canCreate:boolean}>(`${root}/curricula${query}`, 0, "تعذر تحميل مناهج المهارات.");
  const canCreate = result.data?.canCreate ?? options.data?.canCreate;
  const levels = new Map(options.data?.levels.map(item => [item.id, item]));
  return <>
    <PageHeader title="مناهج المهارات" context="التقييمات والتطور" action={canCreate ? {label:"منهج جديد",href:`${catalogue}/new${query}`} : undefined}/>
    {options.error && <SkillReadError message={options.error} reload={options.reload}/>}
    {options.data && <div className={styles.curriculumFilter}><label>المستوى الرياضي<select value={levels.has(level) ? level : ""} onChange={event => window.history.replaceState(null, "", `${catalogue}${levelQuery(event.target.value)}`)}>
      <option value="">كل المستويات</option>{options.data.levels.map(item => <option key={item.id} value={item.id}>{item.sport} · {item.arabicName}</option>)}
    </select></label>{level && !levels.has(level) && <Link href={catalogue}>عرض كل المستويات</Link>}</div>}
    {result.error && <SkillReadError message={result.error} reload={result.reload}/>}
    {!result.data ? !result.error && <LoadingState message="جارٍ تحميل المناهج…"/> :
      result.data.items.length ? <SectionCard title="نسخ المناهج">
        <ul className={styles.curriculumRows}>{result.data.items.map(item => <li key={item.id}>
          <div><Link className={styles.curriculumTitle} href={`${catalogue}/curricula/${item.id}${query}`} aria-label={`${item.title}، نسخة ${item.revisionNumber}، ${curriculumStates[item.state]}`}>{item.title}</Link>
            <p>{levels.get(item.trainingLevelId)?.sport && <>{levels.get(item.trainingLevelId)?.sport} · </>}{item.levelNameSnapshot} · نسخة {item.revisionNumber}</p>
          </div><span className="status-pill">{curriculumStates[item.state]}</span>
        </li>)}</ul>
        {result.data.hasMore && <p>تظهر أحدث 100 نسخة. اختر مستوى لتضييق القائمة.</p>}
      </SectionCard> : <EmptyState title={level ? "لا توجد مناهج لهذا المستوى" : "لا توجد مناهج مهارات بعد"}/>}</>;
}

type Definition = { item:Curriculum; requirements:Requirement[]; canEdit:boolean; canCopy:boolean };
type Draft = { version:number; title:string; level:string; skills:Requirement[] };
type EditorAction = "copy" | "publish" | "retire";
const emptyRequirement = (id: string): Requirement => ({id, name:"", unit:"Checklist", protocol:""});
const requirementBody = ({name,unit,protocol}: Requirement) => ({name:name.trim(),unit,protocol:protocol.trim()});
const draftBody = (draft:Draft) => ({title:draft.title.trim(),trainingLevelId:draft.level,requirements:draft.skills.map(requirementBody)});

export function SkillCurriculumEditor({id}:{id?:string}) {
  return <Suspense fallback={<LoadingState/>}><CurriculumEditor key={id ?? "new"} id={id}/></Suspense>;
}
function CurriculumEditor({id}:{id?:string}) {
  const router = useRouter();
  const queryLevel = levelFilter(useSearchParams().get("levelId"));
  const query = levelQuery(queryLevel);
  const [revision,setRevision] = useState(0), [draft,setDraft] = useState<Draft|null>(null);
  const [message,setMessage] = useState(""), [validation,setValidation] = useState("");
  const [confirmation,setConfirmation] = useState<{action:"publish"|"retire";version:number}|null>(null);
  const [levelBusy,setLevelBusy] = useState(false), [levelError,setLevelError] = useState("");
  const newName = useRef<HTMLInputElement>(null);
  const result = useGoalData<Definition|Options>(id ? `${root}/curricula/${id}` : `${root}/options`, revision, id ? "تعذر تحميل نسخة المنهج." : "تعذر تحميل المستويات الرياضية.");
  const details = result.data && "item" in result.data ? result.data : null;
  const options = result.data && "levels" in result.data ? result.data : null;
  const current = details?.item;
  const saved: Draft = current ? {version:current.version,title:current.title,level:current.trainingLevelId,skills:details.requirements} : {
    version:0,title:"",level:options?.levels.some(level => level.id === queryLevel) ? queryLevel : "",skills:[emptyRequirement("first")],
  };
  const form = draft ?? saved;
  const dirty = !!draft && JSON.stringify(draftBody(form)) !== JSON.stringify(draftBody(saved));
  const stale = !!current && dirty && form.version !== current.version;
  const editable = details ? details.canEdit && current?.state === "Draft" : options?.canCreate;
  const command = useCommand((response, path) => {
    if (!id || response.id !== id) {
      router.push(`${catalogue}/curricula/${response.id}${query}`);
      return;
    }
    setMessage(path.endsWith("/publish") ? "تم نشر نسخة المنهج." : path.endsWith("/retire") ? "تم إيقاف النسخة وحفظ سجلها." : "تم حفظ المسودة.");
    setDraft(null);
    setRevision(value => value + 1);
  }, {announce:false});
  const failureStatus = command.failure?.status;
  const ambiguous = failureStatus === 0 || (failureStatus ?? 0) >= 500;
  const uncertainCreation = ambiguous && command.failure?.path === `${root}/curricula`;
  const mustRead = [401,403,404,409].some(status => status === failureStatus) || ambiguous && !uncertainCreation;
  const blocked = command.busy || levelBusy || mustRead || uncertainCreation || stale;
  const change = (patch: Partial<Draft>) => {setDraft({...form,...patch}); setValidation(""); setMessage("");};
  const updateRequirement = (key:string, patch:Partial<Requirement>) => change({skills:form.skills.map(skill => skill.id === key ? {...skill,...patch} : skill)});
  function submit(event:FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (blocked || !editable) return;
    const payload = draftBody(form);
    if (!payload.title || !payload.trainingLevelId || payload.requirements.some(skill => !skill.name || !skill.protocol)) {
      setValidation("أكمل اسم المنهج والمستوى واسم كل مهارة وطريقة ملاحظتها.");
      return;
    }
    if (new Set(payload.requirements.map(skill => skill.name.toLowerCase())).size !== payload.requirements.length) {
      setValidation("استخدم اسمًا مختلفًا لكل مهارة.");
      return;
    }
    setMessage("");
    void command.save(id ? `${root}/curricula/${id}` : `${root}/curricula`, {...payload,version:form.version}, id ? "PUT" : "POST");
  }
  async function applyAction(action: EditorAction) {
    if (!current || !details || blocked || dirty || (action === "copy" ? !details.canCopy : !details.canEdit)) return;
    setMessage("");
    if (action === "copy") await command.save(`${root}/curricula`, {...draftBody(saved),copiedFromId:current.id,sourceVersion:current.version});
    else await command.save(`${root}/curricula/${current.id}/${action}`, {version:current.version});
    setConfirmation(null);
  }
  async function createLevel(event:FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (blocked || !options?.canCreateLevel) return;
    const fields = new FormData(event.currentTarget);
    const body = {sportId:fields.get("sport"),arabicName:String(fields.get("name") ?? "").trim(),rank:Number(fields.get("rank"))};
    if (!body.arabicName) {setLevelError("أدخل اسم المستوى."); return;}
    setLevelBusy(true); setLevelError("");
    try {
      const response = await csrfRequest("/api/v1/manage/training-levels","POST",body,{}, {announce:false});
      const {id:levelId} = await response.json() as {id:string};
      change({level:levelId});
      setMessage("تمت إضافة المستوى واختياره للمنهج.");
      setRevision(value => value + 1);
    } catch (failure) {
      setLevelError(failure instanceof DashboardRequestError ? failure.message : "تعذر تأكيد إضافة المستوى. حدّث قائمة المستويات وتحقّق من ظهوره.");
    } finally {setLevelBusy(false);}
  }
  const publishing = confirmation?.action === "publish";
  const validConfirmation = !!confirmation && confirmation.version === current?.version;
  return <>
    <PageHeader title={current?.title ?? (id ? "نسخة المنهج" : "منهج مهارات جديد")} context="التدريب والتطور" back={{label:"العودة إلى المناهج",href:`${catalogue}${query}`}}/>
    {message && <p className={styles.curriculumStatus} role="status">{message}</p>}
    {result.error && <SkillReadError message={result.error} reload={result.reload}/>}
    {command.error && <ErrorState message={command.error}/>}
    {uncertainCreation && <Button variant="secondary" disabled={command.busy || !(options?.canCreate || details?.canCopy)} onClick={()=>void command.retry()}>إعادة محاولة الطلب نفسه</Button>}
    {mustRead && <Button variant="secondary" onClick={() => {command.clearError(); result.reload();}}>تحديث بيانات النسخة</Button>}
    {stale && !mustRead && <><ErrorState message="تغيّرت المسودة المحفوظة أثناء التحرير. اعرض النسخة الحالية قبل متابعة التعديل."/><Button variant="secondary" onClick={() => {setDraft(null);setValidation("");}}>استخدام النسخة الحالية</Button></>}
    {!result.data && !result.error && <LoadingState message={id ? "جارٍ تحميل نسخة المنهج…" : "جارٍ تحميل المستويات…"}/>}
    {current && <div className={styles.curriculumFacts}><span className="status-pill">{curriculumStates[current.state]}</span><span>نسخة {current.revisionNumber} · {current.levelNameSnapshot}</span></div>}
    {editable && <form className={`form-shell ${styles.curriculumForm}`} onSubmit={submit}>
      <label>اسم نسخة المنهج<input required maxLength={100} value={form.title} onChange={event => change({title:event.target.value})} disabled={blocked}/></label>
      {options && <label>المستوى الرياضي<select required value={form.level} onChange={event => change({level:event.target.value})} disabled={blocked || !options.levels.length}>
        <option value="">اختر المستوى</option>{options.levels.map(level => <option key={level.id} value={level.id}>{level.sport} · {level.arabicName}</option>)}
      </select></label>}
      {options && !options.levels.length && <p>أضف مستوى رياضيًا قبل حفظ المنهج.</p>}
      <h2>مهارات المنهج</h2>
      {form.skills.map((skill,index) => <fieldset key={skill.id} disabled={blocked} className={styles.curriculumSkill}>
        <legend>المهارة {index + 1}</legend>
        <label>اسم المهارة<input ref={skill.id === form.skills.at(-1)?.id ? newName : undefined} required maxLength={120} value={skill.name} onChange={event => updateRequirement(skill.id,{name:event.target.value})}/></label>
        <label>وحدة القياس<select value={skill.unit} onChange={event => updateRequirement(skill.id,{unit:event.target.value})}>{Object.entries(units).map(([value,label]) => <option key={value} value={value}>{label}</option>)}</select></label>
        <label>طريقة الملاحظة<textarea required maxLength={500} rows={2} value={skill.protocol} onChange={event => updateRequirement(skill.id,{protocol:event.target.value})}/></label>
        {form.skills.length > 1 && <Button variant="ghost" onClick={() => change({skills:form.skills.filter(item => item.id !== skill.id)})}>إزالة المهارة {index + 1}</Button>}
      </fieldset>)}
      {validation && <ErrorState message={validation}/>}
      <div className="form-actions">
        <Button type="submit" disabled={blocked || !form.level || !!current && !dirty}>حفظ المسودة</Button>
        {form.skills.length < 20 && <Button variant="secondary" disabled={blocked} onClick={() => {
          change({skills:[...form.skills,emptyRequirement(crypto.randomUUID())]});
          requestAnimationFrame(() => newName.current?.focus());
        }}>إضافة متطلب</Button>}
        {current && dirty && !stale && <Button variant="ghost" disabled={blocked} onClick={() => {setDraft(null);setValidation("");}}>استرجاع المسودة المحفوظة</Button>}
      </div>
    </form>}
    {options && !options.canCreate && <EmptyState title="إنشاء المناهج غير متاح لهذا الحساب"/>}
    {details && !editable && <SectionCard title="مهارات المنهج"><ol className={styles.curriculumRequirements}>
      {details.requirements.map(skill => <li key={skill.id}><h3>{skill.name}</h3><p className={styles.reviewMeta}>{units[skill.unit]}</p><p>{skill.protocol}</p></li>)}
    </ol></SectionCard>}
    {current && details && <div className={styles.curriculumActions}>
      {dirty && !stale && <p>احفظ تغييرات المسودة قبل النشر أو النسخ أو الإيقاف.</p>}
      <div className="form-actions">
        {details.canEdit && current.state === "Draft" && <Button disabled={blocked || dirty} onClick={() => setConfirmation({action:"publish",version:current.version})}>نشر نسخة المنهج</Button>}
        {details.canCopy && <Button variant="secondary" disabled={blocked || dirty} onClick={() => void applyAction("copy")}>نسخ إلى مسودة جديدة</Button>}
        {details.canEdit && current.state !== "Retired" && <Button variant="danger" disabled={blocked || dirty} onClick={() => setConfirmation({action:"retire",version:current.version})}>إيقاف النسخة</Button>}
      </div>
    </div>}
    {options?.canCreateLevel && <details className={styles.curriculumLevel}><summary>إضافة مستوى رياضي</summary>
      <form className="form-shell" onSubmit={createLevel}>
        <label>رياضة المستوى<select name="sport" required disabled={blocked}><option value="">اختر الرياضة</option>{options.sports.map(sport => <option key={sport.id} value={sport.id}>{sport.name}</option>)}</select></label>
        <label>اسم المستوى<input name="name" required maxLength={100} disabled={blocked}/></label>
        <label>ترتيب المستوى<input name="rank" type="number" required min="0" max="1000" defaultValue="1" disabled={blocked}/></label>
        {levelError && <><ErrorState message={levelError}/><Button variant="secondary" onClick={() => {setLevelError(""); result.reload();}}>تحديث المستويات</Button></>}
        <Button type="submit" disabled={blocked || !options.sports.length}>إضافة المستوى</Button>
      </form>
    </details>}
    <ConfirmationDialog open={validConfirmation && !dirty && !stale} busy={command.busy} title={publishing ? "نشر نسخة المنهج" : "إيقاف نسخة المنهج"}
      message={publishing ? "يثبّت النشر مهارات هذه النسخة وطريقة قياسها. تبقى خطط اللاعبين المسندة على نسخها الحالية." : current?.state === "Draft" ? "يتوقف تحرير هذه المسودة ونشرها، وتبقى في سجل المناهج." : "يتوقف إسناد هذه النسخة وتسجيل مراجعات جديدة عليها. تبقى المراجعات السابقة محفوظة."}
      confirmLabel={publishing ? "تأكيد النشر" : "تأكيد الإيقاف"} tone={publishing ? "primary" : "danger"}
      onConfirm={() => {if (confirmation && validConfirmation) void applyAction(confirmation.action);}} onCancel={() => setConfirmation(null)}/>
  </>;
}
function Progress({data, showEnrollment = true, action}:{data:PlanData; showEnrollment?:boolean; action?:ReactNode}) {
  const p = data.progress;
  const curriculum = data.plan.curriculum;
  const showTrack = !data.contextChanged && p.assessed > 0 && p.total > 0 && p.percentage !== null;
  return <SectionCard title={curriculum.title}>
    <p>{showEnrollment && <>{data.enrollment.player} · {data.enrollment.sport} · </>}{curriculum.levelNameSnapshot} · نسخة {curriculum.revisionNumber}</p>
    {data.contextChanged && <p className="status-pill">خطة غير سارية</p>}
    {p.assessed === 0 ? <p>لم تُراجع أي مهارة بعد.</p> : <>
      <p><strong>الإتقان <bdi>{p.mastered}</bdi> من <bdi>{p.total}</bdi></strong>{showTrack && <> · <bdi>{p.percentage}%</bdi></>}</p>
      {showTrack && <progress className={styles.progressTrack} max={p.total} value={p.mastered} aria-label="المهارات المتقنة" />}
      {data.contextChanged && p.sourcePercentage !== null && <p>الإتقان وقت الخطة <bdi>{p.sourcePercentage}%</bdi></p>}
      <p>تمت مراجعة <bdi>{p.assessed}</bdi> من <bdi>{p.total}</bdi></p>
    </>}
    {p.readyForTechnicalReview && <p>اكتملت مهارات الخطة وهي جاهزة للمراجعة الفنية.</p>}
    {action}
  </SectionCard>;
}
function ReviewText({review,unit}:{review:Review|null;unit:string}) {
  return review ? <>
    <strong>{states[review.state]}{review.measuredValue !== null && <> · {unit === "Count" ? <>العدد <bdi>{review.measuredValue}</bdi></> : <><bdi>{review.measuredValue}</bdi> {units[unit]}</>}</>}</strong>
    {review.note && <p>{review.note}</p>}
    <p className={styles.reviewMeta}>{review.actorName} · {formatDateAr(review.observedOn)}</p>
  </> : <strong>لم يُقيّم</strong>;
}
function SkillReadError({message,reload}:{message:string;reload:()=>void}) {
  return <><ErrorState message={message}/><Button variant="secondary" onClick={reload}>إعادة المحاولة</Button></>;
}
export function PlayerSkillJourney({enrollmentId}:{enrollmentId:string}) {
  const [revision,setRevision]=useState(0),[message,setMessage]=useState(""),[expectedPlan,setExpectedPlan]=useState<string|null>(null);const {data,error,reload}=useSkills<Journey>(`${root}/enrollments/${enrollmentId}/journey`,revision);const command=useCommand(result=>{setExpectedPlan(result.id);setMessage("تم إسناد الخطة مع حفظ السجل السابق.");setRevision(n=>n+1);},{announce:false});
  return <><PageHeader title="رحلة مهارات اللاعب" context="التدريب والتطور" back={data ? { label: "ملف اللاعب ومراجعة مستواه", href: `/dashboard/players/${data.enrollment.playerId}?tab=training` } : undefined}/>
    {error && <SkillReadError message={error} reload={reload}/>} {command.error && <ErrorState message={command.error}/>} {message&&<p role="status">{message}</p>}
    {!data?!error&&<LoadingState/>:<><SectionCard title={`${data.enrollment.player} · ${data.enrollment.sport}`}><p>{data.enrollment.level ? <>المستوى المسجل · {data.enrollment.level}</> : "لم يُحدد المستوى بعد."}</p></SectionCard>
      {data.active ? <Progress data={data.active} showEnrollment={false} action={!command.busy && (!expectedPlan || data.active.plan.id === expectedPlan) && <Link className="button button-primary" href={`/dashboard/evaluations/skills/plans/${data.active.plan.id}`}>فتح المهارات والملاحظات</Link>}/> : <p>لم تُسند خطة مهارات بعد.</p>}
      {data.canAssign&&<form className="form-shell" onSubmit={e=>{e.preventDefault();const f=new FormData(e.currentTarget);if(data.active&&!window.confirm("استبدال الخطة يبدأ متابعة نسخة جديدة ويحفظ القديمة. هل تتابع؟"))return;void command.save(`${root}/enrollments/${enrollmentId}/skill-plan`,{curriculumId:f.get("curriculum"),previousPlanId:data.active?.plan.id??null,previousVersion:data.active?.plan.version??0});}}><label>نسخة المستوى الحالي<select name="curriculum" required disabled={command.busy}><option value="">اختر أحدث نسخة منشورة</option>{data.availableCurricula.map(c=><option key={c.id} value={c.id}>{c.title} · نسخة{c.revisionNumber}</option>)}</select></label><Button type="submit" disabled={command.busy||!data.availableCurricula.length}>{data.active?"استبدال الخطة مع حفظ التاريخ":"إسناد خطة المهارات"}</Button>{!data.availableCurricula.length&&<p>راجع المستوى الفني من ملف اللاعب ثم جهز وانشر منهجًا لنفس المستوى.</p>}</form>}
      {data.history.length > 0 && <SectionCard title="الخطط السابقة">{data.history.map(p=><p key={p.id}><Link href={`/dashboard/evaluations/skills/plans/${p.id}`}>{p.curriculum.title} · نسخة {p.curriculum.revisionNumber} · {formatDateAr(p.assignedOn)}</Link></p>)}{data.historyHasMore&&<p>تظهر أحدث 20 خطة سابقة.</p>}</SectionCard>}
      {!!data.levelHistory?.items.length && <PlayerLevelHistory history={data.levelHistory}/>}
    </>}
  </>;
}
export function PlayerSkillPlan({id,playerId}:{id:string;playerId?:string}) {
  return <SkillPlan key={`${playerId ?? "staff"}:${id}`} id={id} playerId={playerId}/>;
}
function SkillPlan({id,playerId}:{id:string;playerId?:string}) {
  const [revision,setRevision] = useState(0), [message,setMessage] = useState("");
  const [requirement,setRequirement] = useState(""), [state,setState] = useState("Learning");
  const [measurement,setMeasurement] = useState(""), [note,setNote] = useState(""), [date,setDate] = useState<string|null>(null);
  const familyPath = playerId ? `/guardian/children/${playerId}/skills` : null;
  const planHref = `${familyPath ?? "/dashboard/evaluations/skills"}/plans/${id}`;
  const path = familyPath ? `/api/v1${planHref}` : `${root}/plans/${id}`;
  const {data,error,reload} = useSkills<PlanData>(path,revision);
  const command = useCommand(() => {
    setMessage("تم حفظ الملاحظة وأصبحت متاحة لولي الأمر.");
    setMeasurement(""); setNote(""); setDate(null); setRevision(n => n + 1);
  }, {announce:false});
  const chosen = data?.skills.find(s => s.requirement.id === requirement)?.requirement;
  const needsMeasurement = !!chosen && chosen.unit !== "Checklist" && state !== "NotAssessed";
  function saveReview(e:FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!data?.canReview || !chosen || !e.currentTarget.reportValidity() || (needsMeasurement && !measurement.trim())) return;
    setMessage("");
    void command.save(`${root}/plans/${id}/reviews`, {
      requirementId: chosen.id, state, measuredValue: needsMeasurement ? Number(measurement) : null,
      observedOn: date ?? data.asOfDate, note: note.trim() || null,
    });
  }
  const content = <>
    {error && <SkillReadError message={error} reload={reload}/>}
    {command.error && <ErrorState message={command.error}/>}
    {message && <p role="status">{message}</p>}
    {!data ? !error && <LoadingState/> : <>
      <Progress data={data} action={<Link className="button button-secondary" href={`${planHref}/technical-review`}>المراجعة الفنية للمستوى</Link>}/>
      {data.canReview && !playerId && <form className="form-shell" onSubmit={saveReview} aria-labelledby="skill-review-title">
        <h2 id="skill-review-title">تسجيل ملاحظة فنية</h2>
        <label>المهارة<select required value={chosen?.id ?? ""} onChange={e => { setRequirement(e.target.value); setMeasurement(""); }} disabled={command.busy}>
          <option value="">اختر المهارة</option>{data.skills.map(s => <option key={s.requirement.id} value={s.requirement.id}>{s.requirement.name}</option>)}
        </select></label>
        {chosen && <p>{chosen.protocol}</p>}
        <label>الحالة الفنية<select value={state} onChange={e => setState(e.target.value)} disabled={command.busy}>{Object.entries(states).map(([v,l]) => <option key={v} value={v}>{l}</option>)}</select></label>
        {needsMeasurement && chosen && <label>القياس · {units[chosen.unit]}<input name="value" type="number" min="0" max="1000000" step={chosen.unit === "Count" ? "1" : "0.001"} value={measurement} onChange={e => setMeasurement(e.target.value)} required disabled={command.busy}/></label>}
        <label>تاريخ الملاحظة<input name="date" type="date" min={data.plan.assignedOn} max={data.asOfDate} value={date ?? data.asOfDate} onChange={e => setDate(e.target.value)} required disabled={command.busy}/></label>
        <label>ملاحظة للأسرة (اختياري)<textarea name="note" maxLength={500} value={note} onChange={e => setNote(e.target.value)} aria-describedby="skill-review-audience" disabled={command.busy}/></label>
        <p id="skill-review-audience">تظهر الملاحظة لولي الأمر المرتبط باللاعب. يُسجّل أي تصحيح كملاحظة جديدة.</p>
        <Button type="submit" disabled={command.busy}>حفظ ونشر الملاحظة للأسرة</Button>
      </form>}
      {data.skills.map(s => <SectionCard key={s.requirement.id} title={s.requirement.name}>
        <ReviewText review={s.latest} unit={s.requirement.unit}/>
        {s.requirement.protocol && <details><summary>طريقة القياس</summary><p>{s.requirement.protocol}</p></details>}
        {s.baseline && s.baseline.id !== s.latest?.id && <details><summary>أول ملاحظة</summary><ReviewText review={s.baseline} unit={s.requirement.unit}/></details>}
      </SectionCard>)}
      {data.history.length > 0 && <SectionCard title="سجل الملاحظات الفنية"><details><summary>عرض السجل</summary>
        {data.history.map(r => { const skill = data.skills.find(s => s.requirement.id === r.requirementId)?.requirement;
          return <article key={r.id}><h3>{skill?.name ?? "ملاحظة فنية"}</h3><ReviewText review={r} unit={skill?.unit ?? "Checklist"}/></article>;
        })}
        {data.historyHasMore && <p>تظهر أحدث 20 ملاحظة.</p>}
      </details></SectionCard>}
    </>}
  </>;
  if (familyPath) return <GuardianShell><div className={styles.family}>
    <header className="guardian-page-header"><Link className="button" href={familyPath}>العودة إلى خطة المهارات</Link><h1>المهارات وملاحظات المدرب</h1></header>
    {content}
  </div></GuardianShell>;
  return <><PageHeader title="مهارات اللاعب" context="التدريب والتطور" back={data ? { label: "العودة إلى رحلة اللاعب", href: `/dashboard/evaluations/skills/journey?enrollmentId=${data.enrollment.id}` } : undefined}/>{content}</>;
}
export function GuardianPlayerSkills({playerId}:{playerId:string}) {
  const {data,error,reload} = useSkills<{items:Journey[]}>(`/api/v1/guardian/children/${playerId}/skills`);
  const path = `/guardian/children/${playerId}`;
  return <GuardianShell><div className={styles.family}>
    <header className="guardian-page-header"><Link className="button" href={path}>العودة إلى ملف اللاعب</Link><h1>خطط المهارات</h1></header>
    {error && <SkillReadError message={error} reload={reload}/>}
    {!data ? !error && <LoadingState/> : data.items.length === 0 ? <p className={styles.noPlan}>لا توجد خطط مهارات لهذا اللاعب بعد.</p> : data.items.map(j => <section key={j.enrollment.id}>
      {j.active ? <Progress data={j.active} action={<Link className="button button-secondary" href={`${path}/skills/plans/${j.active.plan.id}`}>مهارات {j.enrollment.sport} وملاحظات المدرب</Link>}/> : <p className={styles.noPlan}>{j.enrollment.sport} · لم تُنشر خطة مهارات بعد.</p>}
      {j.history.map(p => <p key={p.id}><Link className="button" href={`${path}/skills/plans/${p.id}`}>خطة سابقة · {p.curriculum.title} · نسخة {p.curriculum.revisionNumber}</Link></p>)}
      {!!j.levelHistory?.items.length && <PlayerLevelHistory history={j.levelHistory}/>}
    </section>)}
  </div></GuardianShell>;
}
