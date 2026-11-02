"use client";
import Image from "next/image";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { GuardianShell } from "../components/guardian-shell";
import { csrfRequest } from "./dashboard-api";
import "./community-ads.css";
type Ad={id:string;title:string;body:string;advertiser:string;buttonText:string;imageUrl:string|null;imageAlt:string;endsAtUtc:string;destination:string};
async function read<T>(path:string):Promise<T>{const r=await fetch(path,{cache:"no-store"});if(!r.ok)throw new Error("المحتوى غير متاح أو سُحبت الموافقة عليه.");return r.json();}
export function GuardianSponsoredCard(){
  const [ad,setAd]=useState<Ad|null>(null);const [notice,setNotice]=useState("");const card=useRef<HTMLElement|null>(null),timer=useRef<ReturnType<typeof setTimeout>|null>(null);const measured=useRef(false);const router=useRouter();
  useEffect(()=>{let live=true;read<{item:Ad|null}>("/api/v1/guardian/community-ads/feed").then(d=>{if(live)setAd(d.item);}).catch(()=>{});return()=>{live=false;};},[]);
  useEffect(()=>{if(!ad||!card.current||typeof IntersectionObserver==="undefined")return;let visible=false,ended=false;const cancel=()=>{if(timer.current)clearTimeout(timer.current);timer.current=null;};const check=()=>{cancel();if(visible&&!measured.current&&document.visibilityState==="visible")timer.current=setTimeout(async()=>{try{await csrfRequest(`/api/v1/guardian/community-ads/${ad.id}/events`,"POST",{kind:"Impression"},{},{announce:false});measured.current=true;}catch{if(!ended)setAd(null);}},1000);};const observer=new IntersectionObserver(entries=>{visible=entries[0]?.intersectionRatio>=.5;check();},{threshold:[0,.5,1]});observer.observe(card.current);const visibility=()=>{if(document.visibilityState!=="visible")setAd(null);check();};document.addEventListener("visibilitychange",visibility);
    // A bounded display lease, not a permanent cached approval. Live authorization also precedes every action.
    const lease=setTimeout(()=>{if(!ended)setAd(null);},Math.max(0,Math.min(30_000,new Date(ad.endsAtUtc).getTime()-Date.now())));
    return()=>{ended=true;cancel();clearTimeout(lease);observer.disconnect();document.removeEventListener("visibilitychange",visibility);};},[ad]);
  async function hide(){if(!ad)return;const id=ad.id;setAd(null);try{await csrfRequest(`/api/v1/guardian/community-ads/${id}/events`,"POST",{kind:"Hide"},{},{announce:false});}catch{setNotice("أُخفي المحتوى هنا، وتعذر حفظ إخفائه لبقية اليوم. يمكنك إيقاف المحتوى من الإعدادات.");}}
  async function open(){if(!ad)return;try{await read(`/api/v1/guardian/community-ads/${ad.id}`);await csrfRequest(`/api/v1/guardian/community-ads/${ad.id}/events`,"POST",{kind:"Click"},{},{announce:false});router.push(`/guardian/community/${encodeURIComponent(ad.id)}`);}catch{setAd(null);}}
  if(!ad)return notice?<p role="status">{notice}</p>:null;return <section className="sponsored-card" ref={card} aria-label="محتوى برعاية"><header><small>محتوى برعاية · {ad.advertiser}</small><button type="button" onClick={hide} aria-label="إخفاء الإعلان اليوم">إخفاء اليوم</button></header>{ad.imageUrl&&<Image unoptimized src={ad.imageUrl} width={560} height={180} alt={ad.imageAlt}/>}<h2>{ad.title}</h2><p>{ad.body.length>180?`${ad.body.slice(0,180)}…`:ad.body}</p><button type="button" onClick={open}>{ad.buttonText}</button></section>;
}
export function GuardianSponsorSettingsLink(){const [available,setAvailable]=useState(false);useEffect(()=>{let live=true;read<{available:boolean}>("/api/v1/guardian/community-ads/preference").then(p=>{if(live)setAvailable(p.available || (p as {inAppOptIn?:boolean}).inAppOptIn === true);}).catch(()=>{});return()=>{live=false;};},[]);return available?<p><Link href="/guardian/community/preferences">اختيار المحتوى برعاية داخل التطبيق</Link></p>:null;}
export function GuardianSponsorSettings(){const [data,setData]=useState<{available:boolean;inAppOptIn:boolean;adultDeclared:boolean;notice:string}|null>(null),[error,setError]=useState(""),[busy,setBusy]=useState(false);useEffect(()=>{read<typeof data>("/api/v1/guardian/community-ads/preference").then(setData).catch(e=>setError(e.message));},[]);async function save(e:React.FormEvent){e.preventDefault();if(!data||busy)return;setBusy(true);try{await csrfRequest("/api/v1/guardian/community-ads/preference","PUT",{inAppOptIn:data.inAppOptIn,adultDeclared:data.adultDeclared},{},{announce:false});setError("تم حفظ اختيارك. لا يؤثر على إشعارات مواعيد أبنائك.");}catch(e){setError(e instanceof Error?e.message:"تعذر حفظ اختيارك.");}finally{setBusy(false);}}
  return <GuardianShell><h1>اختيار المحتوى برعاية</h1><Link href="/guardian">العودة للرئيسية</Link>{error&&<p role="status">{error}</p>}{data&&<form className="sponsor-form" onSubmit={save}><p>{data.notice}</p>{!data.available&&<p>المحتوى برعاية غير متاح حاليًا في الأكاديمية. لا توجد موافقة تلقائية.</p>}<label className="sponsor-check"><input type="checkbox" disabled={busy||(!data.available&&!data.inAppOptIn)} checked={data.inAppOptIn} onChange={e=>setData({...data,inAppOptIn:e.target.checked})}/>أختار مشاهدة محتوى برعاية داخل التطبيق</label><label className="sponsor-check"><input type="checkbox" disabled={busy} required={data.inAppOptIn} checked={data.adultDeclared} onChange={e=>setData({...data,adultDeclared:e.target.checked})}/>أقر بأنني صاحب حساب بالغ</label><button className="primary-button" disabled={busy}>حفظ الاختيار</button></form>}</GuardianShell>;
}
export function GuardianSponsorDetail({id}:{id:string}){
  const [ad,setAd]=useState<Ad|null>(null),[error,setError]=useState("");
  useEffect(()=>{
    let live=true, generation=0;let lease:ReturnType<typeof setTimeout>|undefined;
    const load=()=>{const request=++generation;clearTimeout(lease);read<Ad>(`/api/v1/guardian/community-ads/${encodeURIComponent(id)}`).then(d=>{if(!live||request!==generation)return;setAd(d);setError("");lease=setTimeout(()=>{setAd(null);load();},Math.max(0,Math.min(30_000,new Date(d.endsAtUtc).getTime()-Date.now())));}).catch(e=>{if(live&&request===generation){setAd(null);setError(e.message);}});};
    const visibility=()=>{++generation;clearTimeout(lease);setAd(null);if(document.visibilityState==="visible")load();};
    load();document.addEventListener("visibilitychange",visibility);
    return()=>{live=false;++generation;clearTimeout(lease);document.removeEventListener("visibilitychange",visibility);};
  },[id]);
  return <GuardianShell><Link href="/guardian">العودة للرئيسية</Link>{error&&<p role="alert">{error}</p>}{ad&&<article className="sponsored-card"><small>محتوى برعاية · {ad.advertiser}</small><h1>{ad.title}</h1>{ad.imageUrl&&<Image unoptimized src={ad.imageUrl} width={560} height={200} alt={ad.imageAlt}/>}<p>{ad.body}</p><p>هذه صفحة معلومات، وليست دفعًا أو موافقة على مشاركة بياناتك.</p></article>}</GuardianShell>;
}
