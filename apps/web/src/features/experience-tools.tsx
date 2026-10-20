"use client";
import Link from "next/link";
import {useEffect,useState} from "react";
import {Bell,PlayCircle} from "lucide-react";
import {PageVideoSettings} from "./page-video-settings";
import {NotificationTemplates} from "./notification-center";

export function OwnerSettingsExtras(){
  const [owner,setOwner]=useState(false);
  useEffect(()=>{const c=new AbortController();fetch('/api/v1/me',{signal:c.signal,cache:'no-store'}).then(r=>r.ok?r.json():null).then(d=>{if(!c.signal.aborted)setOwner(d?.role==='AcademyOwner');}).catch(()=>{});return()=>c.abort();},[]);
  return owner?<><PageVideoSettings/><NotificationTemplates/></>:null;
}
export function ExperienceTools(){
  const [unread,setUnread]=useState<number|null>(null),[video,setVideo]=useState<{title:string;url:string}|null>(null),[message,setMessage]=useState('');
  useEffect(()=>{const c=new AbortController();async function refresh(){try{const r=await fetch('/api/v1/notifications',{signal:c.signal,cache:'no-store'});if(r.ok){const d=await r.json();if(!c.signal.aborted)setUnread(d.unread);}}catch{}}void refresh();const timer=setInterval(()=>{if(document.visibilityState==='visible')void refresh();},30000);return()=>{c.abort();clearInterval(timer);};},[]);
  async function help(){try{const r=await fetch('/api/v1/page-help/home',{cache:'no-store'});if(!r.ok)throw Error();const d=await r.json();if(d.available&&d.url)setVideo({title:d.title,url:d.url});else setMessage('لم يضف مالك الأكاديمية فيديو شرح لهذه الصفحة بعد.');}catch{setMessage('تعذر تحميل فيديو الشرح. حاول مرة أخرى.');}}
  return <><Link href="/dashboard/notifications"><Bell size={19}/><span>التنبيهات{unread!=null&&unread>0?` · ${unread}`:''}</span></Link><button type="button" onClick={()=>void help()}><PlayCircle size={19}/><span>شرح الصفحة</span></button>{(video||message)&&<div role="dialog" aria-modal="true" aria-label="شرح الصفحة" className="detail-card"><button onClick={()=>{setVideo(null);setMessage('');}}>إغلاق</button>{video?<><h2>{video.title}</h2><video controls autoPlay src={video.url} style={{width:'100%',maxHeight:'60vh'}}/></>:<p>{message}</p>}</div>}</>;
}
