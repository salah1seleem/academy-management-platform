"use client";
import {FormEvent,useEffect,useRef,useState} from "react";
import {TimeField} from "../components/time-field";
import {ErrorState,LoadingState} from "../components/dashboard-shell";
import {csrfRequest,DashboardRequestError} from "./dashboard-api";

export type MatchEditValue={id:string;tournamentId:string;teamId:string;opponent:string;matchDate:string;startTime:string;venue:string;notes?:string;ourScore?:number;opponentScore?:number;publishedAtUtc?:string;isActive:boolean;copiedFromMatchId?:string;players:{playerId:string;isBench:boolean}[]};
type Roster={items:{enrollmentId:string;playerId:string;name:string}[];quoteHash:string};
type Preview={teamName:string;opponent:string;venue:string;roster:Roster["items"];quoteHash:string};

export function MatchCopyDialog({id,onCancel,onCreated}:{id:string;onCancel:()=>void;onCreated:(id:string)=>Promise<void>}){
  const dialog=useRef<HTMLDialogElement>(null),keys=useRef(new Map<string,string>());const [data,setData]=useState<Preview|null>(null),[error,setError]=useState(""),[busy,setBusy]=useState(false),[revision,setRevision]=useState(0);
  useEffect(()=>{const element=dialog.current;element?.showModal();return()=>element?.close();},[]);
  useEffect(()=>{const c=new AbortController();void fetch(`/api/v1/manage/matches/${id}/copy-preview`,{cache:"no-store",signal:c.signal}).then(async r=>{if(!r.ok)throw new Error();const v=await r.json();if(!c.signal.aborted){setData(v);setError("");}}).catch(()=>{if(!c.signal.aborted){setData(null);setError("تعذر معاينة المباراة أو الفريق غير متاح. لم تُنشأ نسخة.");}});return()=>c.abort();},[id,revision]);
  async function save(e:FormEvent<HTMLFormElement>){e.preventDefault();if(!data||busy)return;const f=new FormData(e.currentTarget),body={matchDate:f.get("date"),startTime:`${f.get("time")}:00`,quoteHash:data.quoteHash},signature=JSON.stringify(body);if(!keys.current.has(signature))keys.current.set(signature,crypto.randomUUID());setBusy(true);setError("");try{const response=await csrfRequest(`/api/v1/manage/matches/${id}/copy`,"POST",body,{"Idempotency-Key":keys.current.get(signature)!});const result=await response.json() as {id:string};await onCreated(result.id);}catch(e){setError(e instanceof DashboardRequestError?e.message:"تعذر تأكيد النسخ؛ حدّث المعاينة أو أعد نفس المحاولة دون إنشاء نسخة أخرى.");}finally{setBusy(false);}}
  return <dialog ref={dialog} className="confirmation-dialog match-copy-dialog" aria-labelledby="copy-match-title" onCancel={e=>{if(busy)e.preventDefault();else onCancel();}}>
    <h2 id="copy-match-title">نسخ مباراة كمسودة</h2><p>نسخة مستقلة بلا نتيجة أو حضور أو مستندات دفع. لن تظهر في تطبيق الأسرة أو المدرب قبل النشر.</p>
    {error&&<ErrorState message={error}/>} {!data?<><LoadingState/><button type="button" onClick={()=>setRevision(v=>v+1)}>إعادة المعاينة</button></>:<form className="form-shell match-draft-form" onSubmit={e=>void save(e)}>
      <p>{data.teamName} × {data.opponent} · {data.venue}</p>
      <label>تاريخ النسخة الجديدة<input name="date" type="date" min="2000-01-01" max="2100-12-31" required/></label><TimeField name="time" label="توقيت النسخة الجديدة" required/>
      <fieldset className="match-roster-review"><legend>قائمة الفريق الحالية — {data.roster.length} لاعب</legend>{data.roster.map(p=><span key={p.enrollmentId}>{p.name}</span>)}{!data.roster.length&&<p>القائمة فارغة؛ يمكن حفظ المسودة، لكن النشر يحتاج لاعبين.</p>}<label className="match-review-check"><input type="checkbox" required/>راجعت الموعد والمشاركين؛ سأتأكد منهم مجددًا قبل النشر</label></fieldset>
      <div className="form-actions"><button className="primary-button" disabled={busy}>{busy?"جارٍ إنشاء المسودة…":"إنشاء المسودة المستقلة"}</button><button type="button" disabled={busy} onClick={()=>setRevision(v=>v+1)}>تحديث المعاينة</button></div>
    </form>}
    <button type="button" className="secondary-button" disabled={busy} onClick={onCancel}>إلغاء النسخ</button>
  </dialog>;
}

export function MatchEditor({match,template,options,busy,onSubmit,onNew}:{match:MatchEditValue|null;template:{tournamentId:string;teamId:string;venue:string}|null;options:{tournaments:{id:string;name:string;isActive:boolean}[];teams:{id:string;name:string;isActive:boolean}[];players:{playerId:string;name:string}[]};busy:boolean;onSubmit:(e:FormEvent<HTMLFormElement>)=>void;onNew:()=>void}){
  const [teamId,setTeamId]=useState(match?.teamId??template?.teamId??""),[roster,setRoster]=useState<Roster|null>(null),[error,setError]=useState(""),[revision,setRevision]=useState(0);
  useEffect(()=>{if(!teamId)return;const c=new AbortController();void fetch(`/api/v1/manage/matches/teams/${teamId}/roster`,{cache:"no-store",signal:c.signal}).then(async r=>{if(!r.ok)throw new Error();const v=await r.json();if(!c.signal.aborted){setRoster(v);setError("");}}).catch(()=>{if(!c.signal.aborted){setRoster(null);setError("تعذر تحميل قائمة الفريق؛ حدّثها قبل النشر.");}});return()=>c.abort();},[teamId,revision]);
  const unpublished=!match?.publishedAtUtc;
  return <section className="detail-card" id="match-editor"><form className="form-shell match-draft-form" aria-label="محرر المباراة" onSubmit={onSubmit}>
    <h2>{match?"تعديل المباراة / النتيجة":template?"إضافة مباراة أخرى":"إنشاء مباراة"}</h2>{match?.copiedFromMatchId&&<p role="note">{unpublished?"مسودة منسوخة مستقلة. راجع الموعد وقائمة الفريق الحالية؛ لا نتيجة أو جمهور منشور موروث.":"مباراة منسوخة مستقلة؛ تعديلها لا يغير المباراة الأصلية."}</p>}
    <label>البطولة<select name="tournament" required defaultValue={match?.tournamentId??template?.tournamentId??""}>{<option value="">اختر البطولة</option>}{options.tournaments.filter(x=>x.isActive).map(x=><option value={x.id} key={x.id}>{x.name}</option>)}</select></label>
    <label>فريق الأكاديمية<select name="team" required value={teamId} onChange={e=>{setTeamId(e.target.value);setRoster(null);setError("");}}><option value="">اختر الفريق</option>{options.teams.filter(x=>x.isActive).map(x=><option value={x.id} key={x.id}>{x.name}</option>)}</select></label>
    <label>الفريق المنافس<input name="opponent" required maxLength={120} defaultValue={match?.opponent??""} autoFocus={!!template}/></label>
    <label>تاريخ المباراة<input name="date" type="date" required defaultValue={match?.matchDate??""}/></label><TimeField name="time" label="التوقيت" required defaultValue={match?.startTime}/>
    <label>الملعب / المكان<input name="venue" required maxLength={200} defaultValue={match?.venue??template?.venue??""}/></label><label>ملاحظات الملعب والتجمع<textarea name="notes" maxLength={500} defaultValue={match?.notes??""}/></label>
    {unpublished&&teamId&&<fieldset className="match-roster-review"><legend>المشاركون عند النشر</legend>{error?<><ErrorState message={error}/><button type="button" onClick={()=>setRevision(v=>v+1)}>تحديث قائمة الفريق</button></>:!roster?<LoadingState/>:<><input type="hidden" name="rosterQuoteHash" value={roster.quoteHash}/>{roster.items.map(p=><span key={p.enrollmentId}>{p.name}</span>)}{!roster.items.length&&<p>لا يوجد لاعبون في الفريق؛ النشر غير متاح.</p>}{match?.copiedFromMatchId&&<label className="match-review-check"><input name="rosterReviewed" type="checkbox" required/>راجعت موعد المسودة وقائمة المشاركين الحالية</label>}</>}</fieldset>}
    {!!match?.players.length&&<details><summary>البدلاء</summary>{match.players.map(p=><label key={p.playerId}><input type="checkbox" name="bench" value={p.playerId} defaultChecked={p.isBench}/>{options.players.find(x=>x.playerId===p.playerId)?.name??"لاعب في القائمة"}</label>)}</details>}
    <div className="form-grid"><label>أهداف الأكاديمية<input name="ourScore" type="number" min="0" max="100" defaultValue={match?.ourScore??""}/></label><label>أهداف المنافس<input name="opponentScore" type="number" min="0" max="100" defaultValue={match?.opponentScore??""}/></label></div>
    <label className="match-review-check"><input name="active" type="checkbox" defaultChecked={match?.isActive??true}/>مباراة قائمة (ألغِ لتسجيل الإلغاء وإبلاغ المعنيين)</label>
    <div className="form-actions"><button name="action" value="draft" disabled={busy}>حفظ{match?.publishedAtUtc?" التعديل":" كمسودة"}</button><button name="action" value="another" disabled={busy}>حفظ وإضافة أخرى</button><button name="action" value="publish" className="primary-button" disabled={busy||(unpublished&&(!roster?.items.length||!!error))}>نشر وإبلاغ أولياء الأمور والمدرب</button>{(match||template)&&<button type="button" onClick={onNew}>مباراة جديدة</button>}</div>
  </form></section>;
}
