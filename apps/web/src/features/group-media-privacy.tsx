"use client";
import {useRef,useState} from "react";
import {ShieldCheck} from "lucide-react";
import {Button,ErrorState,LoadingState,SectionCard} from "../components/dashboard-shell";
import {csrfRequest} from "./dashboard-api";
import {useGoalData} from "./player-goals";
import styles from "./group-media-privacy.module.css";
type Choice={player:string;allowed:boolean;revision:number;changedAtUtc:string|null;notice:string};
export function GroupMediaConsent({playerId,onSaved}:{playerId:string;onSaved?:()=>void}){
 const[revision,setRevision]=useState(0),[busy,setBusy]=useState(false),[error,setError]=useState("");
 const path=`/api/v1/guardian/children/${playerId}/group-media-consent`;
 const{data,error:readError}=useGoalData<Choice>(path,revision,"اختيار مشاركة الطفل غير متاح الآن.");
 const intent=useRef<{signature:string;id:string}|null>(null);
 async function save(allowed:boolean){if(!data||busy)return;const signature=JSON.stringify({path,revision:data.revision,allowed});if(intent.current?.signature!==signature)intent.current={signature,id:crypto.randomUUID()};setBusy(true);setError("");try{await csrfRequest(path,"POST",{allowed,revision:data.revision,requestId:intent.current.id});intent.current=null;setRevision(n=>n+1);onSaved?.();}catch{setError("لم نؤكد حفظ اختيار المشاركة. أعد فتحه ثم حاول مرة أخرى.");}finally{setBusy(false);}}
 return <div className={styles.consent}><SectionCard title="خصوصية صور المجموعة"><ShieldCheck aria-hidden/>{(error||readError)&&<ErrorState message={error||readError}/>} {!data?!readError&&<LoadingState/>:<><p>{data.notice}</p><p role="status">{data.player} · {data.allowed?"المشاركة مسموحة باختيارك":"المشاركة غير مسموحة"}</p><p>إعادة السماح لاحقًا لا تعيد نشر النسخ القديمة تلقائيًا. لا يؤثر الرفض على صورة طفلك الخاصة أو اشتراكه.</p><Button disabled={busy} onClick={()=>void save(!data.allowed)}>{data.allowed?"سحب موافقة المشاركة":"السماح بالمشاركة داخل المجموعة"}</Button></>}</SectionCard></div>;
}
type Audience={eventOn:string;asOfDate:string;sessions?:{id:string;startTime:string;endTime:string}[];players:{id:string;name:string;consented:boolean}[];notice:string};
export function GroupMediaAudienceFields({groupId,onReviewChanged,disabled=false}:{groupId:string;onReviewChanged?:()=>void;disabled?:boolean}) {
  const [day,setDay] = useState("");
  const [session,setSession] = useState("");
  const [selected,setSelected] = useState<string[]>([]);
  const [noChildren,setNoChildren] = useState(false);
  const {data,error,reload} = useGoalData<Audience>(`/api/v1/assets/media-audience?groupId=${groupId}${day?`&eventOn=${day}`:""}${session?`&sessionId=${session}`:""}`,0,"تعذر تحميل جمهور المجموعة.");
  const lostConsent = data && selected.some(id => !data.players.some(player => player.id === id && player.consented));
  function clearReview() { setSelected([]); setNoChildren(false); onReviewChanged?.(); }
  return <fieldset className={styles.review} disabled={disabled}>
    <legend>جمهور المجموعة</legend>
    <label>تاريخ التصوير<input type="date" name="eventOn" value={day || data?.eventOn || ""} max={data?.asOfDate} required onChange={event => {
      setDay(event.target.value); setSession(""); clearReview();
    }} /></label>
    {error ? <><ErrorState message={error} /><Button variant="secondary" onClick={() => { clearReview(); reload(); }}>إعادة المحاولة</Button></> : !data ? <LoadingState /> : <>
      <label>الحصة · اختياري<select name="sessionId" value={session} onChange={event => { setSession(event.target.value); clearReview(); }}>
        <option value="">دون حصة محددة</option>{data.sessions?.map(item => <option key={item.id} value={item.id}>{item.startTime.slice(0,5)} — {item.endTime.slice(0,5)}</option>)}
      </select></label>
      <details><summary>من يشاهد وسائط المجموعة؟</summary><p>{data.notice}</p></details>
      <label className={styles.choice}><input type="checkbox" checked={noChildren} name="noRecognizableChildren" value="true" onChange={event => {
        setNoChildren(event.target.checked); setSelected([]); onReviewChanged?.();
      }} />لا يوجد طفل يمكن التعرف عليه في اللقطة</label>
      <div className={styles.choices}>{data.players.map(player => <label key={player.id} className={styles.choice}>
        <input type="checkbox" checked={player.consented && selected.includes(player.id)} disabled={noChildren || !player.consented} onChange={event => {
          setSelected(previous => event.target.checked ? [...previous,player.id] : previous.filter(id => id !== player.id)); onReviewChanged?.();
        }} />{player.name} · {player.consented ? "الموافقة مستوفاة" : "الموافقة غير مستوفاة"}
      </label>)}</div>
      <input type="hidden" name="subjectPlayerIds" value={lostConsent ? "" : selected.join(",")} />
      {lostConsent ? <><ErrorState message="تغيّرت موافقات المشاركة. راجع الأطفال الظاهرين في اللقطة." /><Button variant="secondary" onClick={clearReview}>إعادة تحديد الأطفال</Button></> :
        !noChildren && !selected.length && <p>حدد كل طفل يمكن التعرف عليه، أو أكد خلو اللقطة من الأطفال الذين يمكن التعرف عليهم.</p>}
    </>}
  </fieldset>;
}
export function MediaWithdrawal({id,onSaved}:{id:string;onSaved:()=>void}){
 const[open,setOpen]=useState(false),[note,setNote]=useState(""),[busy,setBusy]=useState(false),[error,setError]=useState("");const intent=useRef<{note:string;id:string}|null>(null);
 async function save(){if(busy)return;const n=note.trim();if(intent.current?.note!==n)intent.current={note:n,id:crypto.randomUUID()};setBusy(true);setError("");try{await csrfRequest(`/api/v1/assets/${id}/withdraw`,"POST",{requestId:intent.current.id,note:n||null});onSaved();}catch{setError("لم نؤكد حفظ السحب. أعد فتح المعرض، أو أعد المحاولة بنفس الطلب.");}finally{setBusy(false);}}
 return !open?<Button onClick={()=>setOpen(true)}>سحب النشر</Button>:<div className={styles.withdrawal}><p>تتوقف الأسرة والمدرب عن فتح هذه النسخة. يبقى الأصل للإدارة المخولة؛ لا يمكن إحياؤه، وإعادة النشر تحتاج رفعًا ومراجعة جديدين.</p><label>ملاحظة إدارية (اختياري)<textarea value={note} maxLength={500} disabled={busy} onChange={e=>setNote(e.target.value)}/></label>{error&&<ErrorState message={error}/>}<div className="row-actions"><Button disabled={busy} onClick={()=>void save()}>تأكيد سحب النشر</Button><Button disabled={busy} onClick={()=>setOpen(false)}>رجوع</Button></div></div>;
}
