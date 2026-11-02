"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { GuardianShell } from "../components/guardian-shell";
import { EmptyState, ErrorState, LoadingState } from "../components/dashboard-shell";
type Visit = { id: string; enteredAtUtc: string; exitedAtUtc?: string; facility: string; resource: string; staleOpen: boolean };
export function GuardianVisits({ playerId }: { playerId: string }) {
  const [visits,setVisits]=useState<Visit[]|null>(null),[error,setError]=useState("");
  useEffect(()=>{const abort=new AbortController();void fetch(`/api/v1/guardian/children/${playerId}/visits`,{signal:abort.signal,cache:"no-store"}).then(async r=>{if(!r.ok)throw new Error("تعذر تحميل سجل زيارات الطفل.");return await r.json() as {visits:Visit[]};}).then(v=>setVisits(v.visits)).catch(e=>{if(e.name!=="AbortError")setError(e.message);});return()=>abort.abort();},[playerId]);
  const stamp=(v:string)=>new Date(v).toLocaleString("ar-EG-u-nu-latn");
  return <GuardianShell><Link className="back-link guardian-visits-back" href={`/guardian/children/${playerId}`}>العودة لملف الطفل</Link><header className="guardian-page-header"><span>أحدث 100 زيارة للطفل</span><h1>الدخول والخروج</h1><p>سجل الزيارة مستقل عن حضور الحصة. التوقيت بتوقيت الجهاز.</p></header>{error&&<ErrorState message={error} />}{!visits&&!error?<LoadingState/>:visits?.length===0?<EmptyState message="لا توجد زيارات مسجلة للطفل."/>:visits?.map(v=><article key={v.id} className="profile-section"><h2>{v.facility} · {v.resource}</h2><p>الدخول: {stamp(v.enteredAtUtc)}</p><p>{v.exitedAtUtc?`الخروج: ${stamp(v.exitedAtUtc)}`:v.staleOpen?"لم يسجل خروج بعد انتهاء الموعد · بانتظار مراجعة الأكاديمية":"زيارة مفتوحة وفق السجل · ليست عدًا فعليًا"}</p></article>)}</GuardianShell>;
}
