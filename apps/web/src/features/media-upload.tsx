"use client";
import Image from "next/image";
import Link from "next/link";
import {FormEvent,useEffect,useState} from "react";
import {ErrorState,PageHeader} from "../components/dashboard-shell";
import {monthToDate} from "../lib/date-range";
import {uploadAsset} from "./dashboard-api";
import {GroupMediaAudienceFields,MediaWithdrawal} from "./group-media-privacy";
import {useGoalData} from "./player-goals";
import styles from "./group-media-privacy.module.css";
type Target={id:string;arabicName:string;playerCode?:string};
type Asset={id:string;url:string;contentType:string;caption?:string;playerId?:string;trainingGroupId?:string;trainingSessionId?:string;mediaEventOn?:string;withdrawn:boolean;canWithdraw:boolean;canPublish:boolean;withdrawalNote?:string;withdrawnAtUtc?:string};
export function MediaUpload({gallery=false}:{gallery?:boolean}){
 const[players,setPlayers]=useState<Target[]>([]),[groups,setGroups]=useState<Target[]>([]);
 const[revision,setRevision]=useState(0),[reviewed,setReviewed]=useState(false);
 const[scope,setScope]=useState("player"),[target,setTarget]=useState(""),[error,setError]=useState(""),[message,setMessage]=useState(""),[busy,setBusy]=useState(false);
 useEffect(()=>{const abort=new AbortController();const options={signal:abort.signal,cache:"no-store" as const};fetch("/api/v1/people/players",options).then(r=>r.ok?r.json():[]).then(setPlayers).catch(()=>{});fetch("/api/v1/manage/structure/options",options).then(r=>r.ok?r.json():{groups:[]}).then(x=>setGroups(x.groups)).catch(()=>{});return()=>abort.abort();},[]);
 async function upload(e:FormEvent<HTMLFormElement>){e.preventDefault();if(busy)return;const form=e.currentTarget,f=new FormData(form),file=form.querySelector<HTMLInputElement>('input[name="file"]')?.files?.[0];if(!(file instanceof File)||!file.size)return;
  if(!f.get("eventOn")||scope==="group"&&!f.get("noRecognizableChildren")&&!f.get("subjectPlayerIds")){setError("راجع تاريخ الواقعة وحدد الأطفال الظاهرين، أو أكد عدم وجود طفل يمكن التعرف عليه.");return;}
  setBusy(true);setMessage("");try{await uploadAsset(file,"Media",{...(scope==="player"?{playerId:target}:{groupId:target}),caption:String(f.get("caption")??""),...(f.get("sessionId")?{sessionId:String(f.get("sessionId"))}:{}),eventOn:String(f.get("eventOn")),privacyConfirmed:String(f.get("privacyConfirmed")),subjectPlayerIds:String(f.get("subjectPlayerIds")??""),noRecognizableChildren:f.get("noRecognizableChildren")==="true"?"true":"false"});setMessage("تم رفع ونشر النسخة للمخولين وفق جمهور الواقعة والموافقات؛ لا تورث للأعضاء الجدد.");setError("");form.reset();setReviewed(false);setTarget("");setRevision(n=>n+1);}catch(e){setError(e instanceof Error?e.message:"تعذر رفع الوسائط.");}finally{setBusy(false);}}
 return <><PageHeader title={gallery?"معرض اللاعبين والمجموعات":"رفع صور وفيديوهات"} context="اللاعبون / المعرض"/>{error&&<ErrorState message={error}/>} {message&&<p role="status">{message}</p>}
  <form className="form-shell" onSubmit={e=>void upload(e)}><label>رفع إلى<select value={scope} disabled={busy} onChange={e=>{setScope(e.target.value);setTarget("");setReviewed(false);}}><option value="player">لاعب محدد — أسرته فقط</option><option value="group">مجموعة — جمهور مؤرخ وموافقات</option></select></label>
  <label>{scope==="player"?"اللاعب":"المجموعة"}<select name="target" value={target} disabled={busy} required onChange={e=>{setTarget(e.target.value);setReviewed(false);}}><option value="">اختر</option>{(scope==="player"?players:groups).map(x=><option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
  {scope==="group"?(target&&<GroupMediaAudienceFields key={`${target}:${revision}`} groupId={target} onReviewChanged={()=>setReviewed(false)}/>):<label>تاريخ الواقعة<input type="date" name="eventOn" required onChange={()=>setReviewed(false)} defaultValue={monthToDate().to}/></label>}
  <label>الصورة أو الفيديو<input aria-label="الصورة أو الفيديو" required type="file" name="file" accept="image/jpeg,image/png,image/webp,video/mp4,video/quicktime"/><small>صور حتى8 ميجابايت، فيديو حتى50 ميجابايت. لا ترفع ملفًا قبل مراجعته.</small></label>
  <label>الوصف (اختياري)<input name="caption" maxLength={300}/></label>
  <label className={styles.confirm}><input type="checkbox" name="privacyConfirmed" value="true" required checked={reviewed} onChange={e=>setReviewed(e.target.checked)}/>راجعت الملف: لا يظهر طفل يمكن التعرف عليه خارج القائمة المحددة؛ وللاعب الفردي لا يظهر طفل آخر. اللقطة ليست تصريح تسويق أو إعلان.</label>
  <button className="primary-button" disabled={busy||!target}>{busy?"جارٍ الرفع…":"رفع ونشر بعد مراجعة الخصوصية"}</button></form>
  {gallery&&<StaffMediaGallery revision={revision} onReview={x=>{setReviewed(false);setScope(x.trainingGroupId?"group":"player");setTarget(x.trainingGroupId??x.playerId??"");setRevision(n=>n+1);setMessage("اختر تاريخ الواقعة والحصة، وأعد مراجعة الجمهور والملف؛ لن يعاد نشر الأصل المسحوب.");}}/>}</>;
}

function StaffMediaGallery({revision,onReview}:{revision:number;onReview:(asset:Asset)=>void}){
 const[refresh,setRefresh]=useState(0);
 const{data,error}=useGoalData<Asset[]>("/api/v1/assets/gallery?includeWithdrawn=true",revision+refresh,"تعذر فتح المعرض المخول؛ أعد المحاولة.");
 return <section><h2>أحدث100 نسخة · المنشورة والمسحوبة</h2>{error&&<ErrorState message={error}/>}<section className="private-gallery">{data?.map(x=><article className="detail-card" key={x.id}>
 {x.contentType.startsWith("video/")?<video src={x.url} controls preload="metadata"/>:<Image unoptimized src={x.url} width={380} height={260} alt={x.caption??"وسائط تدريب"}/>}
 <p>{x.caption}</p><p>{x.trainingSessionId?"مرتبطة بحصة فعلية":"وسائط دون حصة"} · <bdi>{x.mediaEventOn}</bdi></p>
 <p role="status">{x.withdrawn?"مسحوبة من الأسر والمدرب · الأصل للمراجعة الإدارية فقط":"نسخة محفوظة · وصول الأسرة مشروط بالتفويض والموافقة"}</p>
 {x.withdrawalNote&&<p>ملاحظة إدارية: {x.withdrawalNote}</p>}
 <div className="row-actions"><Link className="button button-secondary" href={x.url} target="_blank">فتح الأصل للمراجعة</Link>
 {x.canWithdraw&&!x.withdrawn&&<MediaWithdrawal id={x.id} onSaved={()=>setRefresh(n=>n+1)}/>}
 {x.canPublish&&x.withdrawn&&<button type="button" className="button button-secondary" onClick={()=>onReview(x)}>مراجعة نسخة جديدة</button>}</div>
 </article>)}</section></section>;
}
