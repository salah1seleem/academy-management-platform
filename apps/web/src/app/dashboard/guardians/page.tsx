"use client";

import { useEffect, useState } from "react";
import { EmptyState, ErrorState, ListSearch, LoadingState, PageHeader, RowActions } from "../../../components/dashboard-shell";
type Guardian = { id: string; displayName: string; contactPhone: string };

export default function GuardiansPage() {
  const [items, setItems] = useState<Guardian[]>([]); const [search, setSearch] = useState(""); const [loading, setLoading] = useState(true); const [error, setError] = useState("");
  useEffect(() => { const timer = window.setTimeout(() => { setLoading(true); fetch(`/api/v1/people/guardians?search=${encodeURIComponent(search)}`).then(async response => { if (!response.ok) throw new Error(); setItems(await response.json() as Guardian[]); }).catch(() => setError("تعذر تحميل أولياء الأمور.")).finally(() => setLoading(false)); }, 150); return () => window.clearTimeout(timer); }, [search]);
  return <><PageHeader title="قائمة أولياء الأمور" context="أولياء الأمور / القائمة" /><section className="list-card"><ListSearch value={search} onChange={setSearch} label="بحث أولياء الأمور" placeholder="الاسم أو رقم الهاتف" />{error && <ErrorState message={error} />}{loading ? <LoadingState /> : items.length === 0 ? <EmptyState /> : <div className="data-list">{items.map(item => <article key={item.id}><div><strong>{item.displayName}</strong><small dir="ltr">{item.contactPhone}</small></div><RowActions actions={[{ label: "عرض الأبناء", href: `/dashboard/guardians/${item.id}` }, { label: "تعديل البيانات", href: `/dashboard/guardians/${item.id}/edit` }]} /></article>)}</div>}</section></>;
}
