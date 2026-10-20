"use client";
import Link from 'next/link';
import {useSearchParams} from 'next/navigation';
import {useEffect,useState} from 'react';
import {ErrorState,LoadingState} from '../components/dashboard-shell';
type Row={id:string;playerId:string;name:string;photoReference:string|null;sport:string;branch:string;group:string|null;active:boolean;endDate:string|null};
export function SubscriptionSnapshot(){const search=useSearchParams();const [page,setPage]=useState(1),[data,setData]=useState<{items:Row[];total:number}|null>(null),[error,setError]=useState('');
  useEffect(()=>{const c=new AbortController(),q=new URLSearchParams([...search.entries()].filter(([,value])=>value!==''));q.set('page',String(page));fetch(`/api/v1/reports/subscription-snapshot?${q}`,{signal:c.signal,cache:'no-store'}).then(async r=>{if(!r.ok)throw Error();return r.json();}).then(d=>{if(!c.signal.aborted){setData(d);setError('');}}).catch(()=>{if(!c.signal.aborted)setError('تعذر تحميل الاشتراكات.');});return()=>c.abort();},[page,search]);
  if(error)return <ErrorState message={error}/>;if(!data)return <LoadingState/>;
  return <section className="detail-card"><h1>الاشتراكات · {data.total}</h1>{data.items.length===0&&<p>لا توجد اشتراكات مطابقة.</p>}{data.items.map(p=><Link className="profile-row" href={`/dashboard/players/${p.playerId}`} key={p.id}>{p.photoReference&&/^\/demo-assets\/portraits\/child-0[12]\.jpg$|^\/api\/v1\/assets\/[a-fA-F0-9-]{36}$/.test(p.photoReference)&&
      // eslint-disable-next-line @next/next/no-img-element -- authenticated image or bundled synthetic portrait
      <img src={p.photoReference} alt={`صورة ${p.name}`} width={48} height={48} style={{borderRadius:'50%',objectFit:'cover'}}/>}<strong>{p.name}</strong><span>{p.sport} · {p.branch} · {p.group}</span><small>{p.active?'فعال':'غير فعال'} · {p.endDate??'دون تاريخ نهاية'}</small></Link>)}<div className="filter-actions"><button disabled={page===1} onClick={()=>setPage(page-1)}>السابق</button><span>{page}</span><button disabled={page*50>=data.total} onClick={()=>setPage(page+1)}>التالي</button></div></section>;
}
