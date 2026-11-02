"use client";
import { useEffect, useRef, useState } from "react";
import Link from "next/link";

type Contact={id:string;name:string;relationship?:string;phone:string|null;url:string|null};
export function validGuardianChat(value:string|null):string|null{
 if(!value)return null;
 try{const url=new URL(value);return url.protocol==="https:"&&url.hostname==="wa.me"&&!url.username&&!url.password&&!url.port&&/^\/201\d{9}$/.test(url.pathname)?url.href:null;}catch{return null;}
}
export function GuardianContact({playerId,guardianId}:{playerId?:string;guardianId?:string}){
 const [contacts,setContacts]=useState<Contact[]|null>(null),[error,setError]=useState(""),[busy,setBusy]=useState(false),[show,setShow]=useState(false);const request=useRef<AbortController|null>(null);
 useEffect(()=>()=>request.current?.abort(),[]);
 async function load(){if(busy)return;setBusy(true);setError("");const controller=new AbortController();request.current=controller;
  try{const path=playerId?`/api/v1/people/players/${playerId}/guardian-contacts`:`/api/v1/people/guardians/${guardianId}/contact`;const response=await fetch(path,{cache:"no-store",signal:controller.signal});if(!response.ok)throw new Error();const value=await response.json();if(!controller.signal.aborted){setContacts(Array.isArray(value.contacts)?value.contacts.filter((c:Contact)=>validGuardianChat(c.url)):[]);setShow(true);}}
  catch{if(!controller.signal.aborted)setError("تعذر تحميل جهة التواصل المسموح بها. تحقق من الاتصال والصلاحية ثم حاول مجددًا.");}finally{if(!controller.signal.aborted)setBusy(false);}
 }
 return <div className="guardian-contact"><button type="button" className="secondary-button guardian-contact-button" disabled={busy} onClick={()=>void load()}><svg aria-hidden="true" width="21" height="21" viewBox="0 0 24 24" fill="none"><path d="M20 11.5a8 8 0 0 1-12 7L3 20l1.5-5A8 8 0 1 1 20 11.5Z" stroke="currentColor" strokeWidth="1.8"/><path d="M8 7.5c0 4 2 6.5 6 7l1.5-2-2-1-1 1a8 8 0 0 1-3-3l1-1-1-2Z" fill="currentColor"/></svg>{busy?"جارٍ التحقق من جهة التواصل…":playerId?"التواصل مع ولي الأمر عبر واتساب":"التواصل عبر واتساب"}</button>
 {error&&<p role="alert">{error}</p>}
 {show&&contacts&&<section aria-label="جهات التواصل عبر واتساب"><h3>{contacts.length>1?"اختر ولي الأمر للتواصل":"جهة التواصل"}</h3>{contacts.length===0?<p role="status">لا يوجد رقم صحيح لولي أمر مرتبط. راجع بيانات التواصل في ملف ولي الأمر.</p>:<ul>{contacts.map(c=><li key={c.id}><a className="guardian-contact-recipient" href={validGuardianChat(c.url)!} target="_blank" rel="noopener noreferrer"><strong>{c.name}</strong><span>{c.relationship??"ولي الأمر"}</span><span dir="ltr">{c.phone}</span><span>فتح المحادثة</span></a>{playerId&&<Link href={`/dashboard/guardians/${c.id}`}>بيانات ولي الأمر</Link>}</li>)}</ul>}<button type="button" className="secondary-button" onClick={()=>{setShow(false);setContacts(null);}}>إغلاق</button></section>}
 </div>;
}
