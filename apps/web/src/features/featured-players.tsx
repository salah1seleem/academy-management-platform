"use client";
import Link from "next/link";
import {useSearchParams} from "next/navigation";
import {useEffect,useState} from "react";
import {AdminDataTable,ErrorState,LoadingState,PageHeader} from "../components/dashboard-shell";
import {Star,UserRound} from "lucide-react";
type Player={id:string;name:string;branch:string;photoReference?:string;score:number;stars:number};
export function FeaturedPlayerList(){
 const query=useSearchParams(),branchId=query.get("branchId")??"",asOf=query.get("asOf")??"";
 const [page,setPage]=useState(1),[data,setData]=useState<{items:Player[];total:number;month:string}|null>(null),[error,setError]=useState("");
 useEffect(()=>{const c=new AbortController();fetch(`/api/v1/reports/featured-players?${new URLSearchParams({page:String(page),...branchId?{branchId}:{},...asOf?{asOf}:{}})}`,{signal:c.signal,cache:"no-store"}).then(async r=>{if(!r.ok)throw Error();return r.json();}).then(d=>{if(!c.signal.aborted){setData(d);setError("");}}).catch(()=>{if(!c.signal.aborted)setError("تعذر تحميل أبرز اللاعبين.");});return()=>c.abort();},[page,branchId,asOf]);
 return <><PageHeader title="أبرز اللاعبين" context="تطور اللاعبين"/>{error?<ErrorState message={error}/>:!data?<LoadingState/>:<section className="list-card"><p>نجوم شهر {data.month.slice(0,7)} · التقييم الظاهر هو الدرجة المنشورة، وليس مؤشر الترتيب المركب.</p>{!data.items.length&&<p>لا توجد تقييمات منشورة مكتملة وقابلة للمقارنة.</p>}<AdminDataTable label="أبرز اللاعبين" rows={data.items} rowKey={p=>p.id} paginate={false} columns={[
 {key:"name",header:"اللاعب",primary:true,render:p=><div className="content-cell">{p.photoReference&&(/^\/demo-assets\/portraits\/child-0[12]\.jpg$/.test(p.photoReference)||/^\/api\/v1\/assets\/[a-fA-F0-9-]{36}$/.test(p.photoReference))?
 // eslint-disable-next-line @next/next/no-img-element -- authorized same-origin or synthetic bundled image
 <img src={p.photoReference} alt={`صورة ${p.name}`} width={48} height={48}/>:<UserRound aria-hidden="true"/>}<strong>{p.name}</strong></div>},
 {key:"branch",header:"الفرع",render:p=>p.branch},
 {key:"score",header:"التقييم المنشور",render:p=><strong>{p.score}</strong>},
 {key:"stars",header:"النجوم",render:p=><span className="cell-stars"><Star aria-hidden="true"/>{p.stars}</span>},
 {key:"actions",header:"الإجراءات",render:p=><Link className="row-primary-action" href={`/dashboard/players/${p.id}`}>عرض اللاعب</Link>},
 ]}/><nav className="row-actions"><button disabled={page===1} onClick={()=>setPage(p=>p-1)}>السابق</button><span>{page}</span><button disabled={page*25>=data.total} onClick={()=>setPage(p=>p+1)}>التالي</button></nav></section>}</>;
}
