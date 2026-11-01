"use client";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { AdminDataTable, ErrorState, LoadingState, PageHeader, StatusBadge } from "../components/dashboard-shell";
import { formatDateAr } from "../lib/formatters";

type Row = { id: string; playerId: string; name: string; photoReference: string | null; sport: string; branch: string; group: string | null; active: boolean; endDate: string | null };
type Snapshot = { items: Row[]; canOpenPlayer?: boolean; total: number; asOf: string };

export function SubscriptionSnapshot() {
  const search = useSearchParams();
  const query = new URLSearchParams([...search.entries()].filter(([key, value]) => key !== "page" && value !== "")).toString();
  return <ScopedSnapshot key={query} query={query} />;
}

function ScopedSnapshot({ query }: { query: string }) {
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<{ key: string; data: Snapshot } | null>(null);
  const [error, setError] = useState<{ key: string; message: string } | null>(null);
  const params = new URLSearchParams(query); params.set("page", String(page));
  const requestKey = params.toString();
  useEffect(() => {
    const controller = new AbortController();
    fetch(`/api/v1/subscriptions/home/items?${requestKey}`, { signal: controller.signal, cache: "no-store" })
      .then(async response => { if (!response.ok) throw Error(); return response.json() as Promise<Snapshot>; })
      .then(data => { if (!controller.signal.aborted) { setResult({ key: requestKey, data }); setError(null); } })
      .catch(() => { if (!controller.signal.aborted) setError({ key: requestKey, message: "تعذر تحميل الاشتراكات." }); });
    return () => controller.abort();
  }, [requestKey]);
  if (error?.key === requestKey) return <ErrorState message={error.message} />;
  if (result?.key !== requestKey) return <LoadingState />;
  const data = result.data;
  return <><PageHeader title="الاشتراكات" context="الأعضاء والاشتراكات"/>
    <section className="list-card"><p className="section-intro">حالة الاشتراكات بتاريخ <time dateTime={data.asOf}>{data.asOf}</time> · {data.total} تسجيل</p>
    <AdminDataTable label="قائمة الاشتراكات حسب التاريخ" rows={data.items} rowKey={row=>row.id} paginate={false} columns={[
      {key:"player",header:"اللاعب",primary:true,render:player=>data.canOpenPlayer?<Link href={`/dashboard/players/${player.playerId}`}>{player.photoReference && /^\/demo-assets\/portraits\/child-0[12]\.jpg$|^\/api\/v1\/assets\/[a-fA-F0-9-]{36}$/.test(player.photoReference) &&
        // eslint-disable-next-line @next/next/no-img-element -- authenticated image or bundled synthetic portrait
        <img src={player.photoReference} alt={`صورة ${player.name}`} width={40} height={40} style={{borderRadius:4,objectFit:"cover"}}/>}<strong>{player.name}</strong></Link>:<strong>{player.name}</strong>},
      {key:"sport",header:"الرياضة",render:player=>player.sport},
      {key:"branch",header:"الفرع والمجموعة",render:player=><>{player.branch}<small>{player.group??"دون مجموعة"}</small></>},
      {key:"status",header:"الحالة",render:player=><StatusBadge status={player.active?"Active":"Inactive"}/>},
      {key:"date",header:"نهاية الاشتراك",render:player=>player.endDate?formatDateAr(player.endDate):"دون تاريخ نهاية"},
      {key:"action",header:"الإجراء",render:player=>data.canOpenPlayer?<Link className="row-primary-action" href={`/dashboard/players/${player.playerId}`}>عرض الملف</Link>:"—"},
    ]}/>
    <nav className="table-pagination" aria-label="صفحات الاشتراكات"><button disabled={page === 1} onClick={() => setPage(page - 1)}>السابق</button><span>صفحة {page}</span><button disabled={page * 50 >= data.total} onClick={() => setPage(page + 1)}>التالي</button></nav>
  </section></>;

}
