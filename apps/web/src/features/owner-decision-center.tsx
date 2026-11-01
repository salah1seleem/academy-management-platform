"use client";
import Link from 'next/link';
import {useEffect,useState} from 'react';
import {useSearchParams} from 'next/navigation';
import {ErrorState,LoadingState,PageHeader} from '../components/dashboard-shell';
import {ApprovedWorkspace} from '../components/approved-workspace';
import {ownerDecisionRecords,type DecisionSources,type HomeDecision} from './owner-home-decisions';
export function OwnerDecisionCenter(){
 const incoming=useSearchParams(),q=incoming.toString(),[records,setRecords]=useState<HomeDecision[]|null>(null),[error,setError]=useState('');
 useEffect(()=>{const c=new AbortController();async function read(path:string){const r=await fetch(path,{signal:c.signal,cache:'no-store'});if(!r.ok)throw Error();return r.json();}
 Promise.all([read(`/api/v1/reports/owner-cockpit?${q}`),read(`/api/v1/reports/owner-summary?${q}`)]).then(([cockpit,summary])=>{if(!c.signal.aborted){setRecords(ownerDecisionRecords({...cockpit,groupAttention:summary.operations.groupAttention,underAssignedCoaches:summary.operations.underAssignedCoaches} as DecisionSources,{from:summary.from,to:summary.to,branchId:incoming.get('branchId')??''}));setError('');}}).catch(()=>{if(!c.signal.aborted)setError('تعذر تحميل القرارات ضمن صلاحياتك.');});return()=>c.abort();
 },[q,incoming]);
 return <ApprovedWorkspace><PageHeader title="مركز القرارات" context="الأكاديمية" secondaryActions={[{label:"المتابعات والإسناد",href:`/dashboard/operations/follow-ups?${q}`}]} />{error?<ErrorState message={error}/>:!records?<LoadingState/>:<>{records.length===0?<p>لا توجد قرارات في النطاق الحالي.</p>:<div className="decision-grid">{records.map((d,i)=><section className="detail-card" key={`${i}:${d.href}`}><h2>{d.title}</h2><p>{d.line}</p><Link href={d.href}>{d.action} ←</Link></section>)}</div>}</>}</ApprovedWorkspace>;
}
