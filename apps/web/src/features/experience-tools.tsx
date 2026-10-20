"use client";
import Link from "next/link";
import {useEffect,useRef,useState} from "react";
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
  const helpRef=useRef<HTMLButtonElement>(null),closeRef=useRef<HTMLButtonElement>(null),showing=Boolean(video||message);
  useEffect(()=>{if(!showing)return;const trigger=helpRef.current;closeRef.current?.focus();function escape(e:KeyboardEvent){if(e.key==='Escape'){setVideo(null);setMessage('');}}document.addEventListener('keydown',escape);return()=>{document.removeEventListener('keydown',escape);trigger?.focus();};},[showing]);
  useEffect(()=>{const c=new AbortController();async function refresh(){try{const r=await fetch('/api/v1/notifications',{signal:c.signal,cache:'no-store'});if(r.ok){const d=await r.json();if(!c.signal.aborted)setUnread(d.unread);}}catch{}}void refresh();const timer=setInterval(()=>{if(document.visibilityState==='visible')void refresh();},30000);return()=>{c.abort();clearInterval(timer);};},[]);
  async function help(){try{const r=await fetch('/api/v1/page-help/home',{cache:'no-store'});if(!r.ok)throw Error();const d=await r.json();if(d.available&&d.url)setVideo({title:d.title,url:d.url});else setMessage('لم يضف مالك الأكاديمية فيديو شرح لهذه الصفحة بعد.');}catch{setMessage('تعذر تحميل فيديو الشرح. حاول مرة أخرى.');}}
  return <><Link href="/dashboard/notifications" aria-label="التنبيهات" title={unread?`${unread} تنبيهات غير مقروءة`:"التنبيهات"}><Bell size={19}/><span>التنبيهات</span>{unread!=null&&unread>0&&<small aria-label={`${unread} غير مقروءة`}>{unread}</small>}</Link><button ref={helpRef} type="button" aria-label="شرح الصفحة" onClick={()=>void help()}><PlayCircle size={19}/><span>شرح الصفحة</span></button>{showing&&<div className="dialog-backdrop" role="presentation" onMouseDown={e=>{if(e.target===e.currentTarget){setVideo(null);setMessage('');}}}><section role="dialog" aria-modal="true" aria-label="شرح الصفحة" className="confirmation-dialog" style={{color:'var(--staff-text)',display:'block'}}><button ref={closeRef} className="button button-secondary" onClick={()=>{setVideo(null);setMessage('');}}>إغلاق</button>{video?<><h2>{video.title}</h2><video controls autoPlay src={video.url} style={{width:'100%',maxHeight:'60vh'}}/></>:<p style={{color:"var(--staff-text-secondary)"}}>{message}</p>}</section></div>}</>;
}
