"use client";
import Link from "next/link";
import {useSearchParams} from "next/navigation";
import {useEffect,useState} from "react";
import {ErrorState,LoadingState,PageHeader} from "../components/dashboard-shell";
import {Star,UserRound} from "lucide-react";
type Player={id:string;name:string;branch:string;photoReference?:string;score:number;stars:number};
export function FeaturedPlayerList(){
 const query=useSearchParams(),branchId=query.get("branchId")??"",asOf=query.get("asOf")??"";
 const [page,setPage]=useState(1),[data,setData]=useState<{items:Player[];total:number;month:string}|null>(null),[error,setError]=useState("");
 useEffect(()=>{const c=new AbortController();fetch(`/api/v1/reports/featured-players?${new URLSearchParams({page:String(page),...branchId?{branchId}:{},...asOf?{asOf}:{}})}`,{signal:c.signal,cache:"no-store"}).then(async r=>{if(!r.ok)throw Error();return r.json();}).then(d=>{if(!c.signal.aborted){setData(d);setError("");}}).catch(()=>{if(!c.signal.aborted)setError("تعذر تحميل أبرز اللاعبين.");});return()=>c.abort();},[page,branchId,asOf]);
 return <><PageHeader title="أبرز اللاعبين" context="تطور اللاعبين"/><Link href="/dashboard">← العودة للرئيسية</Link>{error?<ErrorState message={error}/>:!data?<LoadingState/>:<section className="list-card"><p>نجوم شهر {data.month.slice(0,7)} · التقييم الظاهر هو الدرجة المنشورة، وليس مؤشر الترتيب المركب.</p>{!data.items.length&&<p>لا توجد تقييمات منشورة مكتملة وقابلة للمقارنة.</p>}{data.items.map(p=><article className="profile-row" key={p.id}>{p.photoReference&&(/^\/demo-assets\/portraits\/child-0[12]\.jpg$/.test(p.photoReference)||/^\/api\/v1\/assets\/[a-fA-F0-9-]{36}$/.test(p.photoReference))?
 // eslint-disable-next-line @next/next/no-img-element -- same-origin authorized or bundled synthetic profile
 <img src={p.photoReference} alt={`صورة ${p.name}`} width={56} height={56} style={{borderRadius:"50%",objectFit:"cover"}}/>:<UserRound/>}<div><Link href={`/dashboard/players/${p.id}`}><strong>{p.name}</strong></Link><small>{p.branch}</small></div><strong>{p.score}</strong><span><Star size={16}/>{p.stars}</span></article>)}<nav className="row-actions"><button disabled={page===1} onClick={()=>setPage(p=>p-1)}>السابق</button><span>{page}</span><button disabled={page*25>=data.total} onClick={()=>setPage(p=>p+1)}>التالي</button></nav></section>}</>;
}
