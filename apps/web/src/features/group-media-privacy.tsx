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
type Audience={eventOn:string;asOfDate:string;players:{id:string;name:string;consented:boolean}[];notice:string};
export function GroupMediaAudienceFields({groupId}:{groupId:string}){
 const[day,setDay]=useState(""),[selected,setSelected]=useState<string[]>([]),[noChildren,setNoChildren]=useState(false);
 const{data,error}=useGoalData<Audience>(`/api/v1/assets/media-audience?groupId=${groupId}${day?`&eventOn=${day}`:""}`,0,"تعذر مراجعة جمهور المجموعة؛ لا تنشر قبل إعادة المراجعة.");
 return <fieldset className={styles.review}><legend>جمهور الواقعة وخصوصية الأطفال</legend>{error&&<ErrorState message={error}/>} {!data?!error&&<LoadingState/>:<><label>تاريخ الواقعة<input type="date" name="eventOn" value={day||data.eventOn} max={data.asOfDate} required onChange={e=>{setDay(e.target.value);setSelected([]);setNoChildren(false);}}/></label><p>{data.notice}</p><label className={styles.choice}><input type="checkbox" checked={noChildren} name="noRecognizableChildren" value="true" onChange={e=>{setNoChildren(e.target.checked);setSelected([]);}}/>لا يوجد طفل يمكن التعرف عليه في هذه اللقطة</label><div className={styles.choices}>{data.players.map(p=><label key={p.id} className={styles.choice}><input type="checkbox" checked={selected.includes(p.id)} disabled={noChildren||!p.consented} onChange={e=>setSelected(old=>e.target.checked?[...old,p.id]:old.filter(id=>id!==p.id))}/>{p.name} · {p.consented?"موافقة مستوفاة":"الموافقة غير مستوفاة — لا تنشر ظهوره"}</label>)}</div><input type="hidden" name="subjectPlayerIds" value={selected.join(",")}/>{!noChildren&&!selected.length&&<p>حدد طفلًا ظاهرًا على الأقل، أو أكد صراحة عدم ظهور أطفال.</p>}</>}</fieldset>;
}
