"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { monthToDate } from "../lib/date-range";

type Row = { id: string; membershipId: string; createdAtUtc: string; actorName: string | null; staffName: string | null; change: string };

export function StaffAccessAudit({ initialFrom, initialTo, initialAllDates = false }: { initialFrom?: string; initialTo?: string; initialAllDates?: boolean }) {
  const [from, setFrom] = useState(initialFrom ?? (initialAllDates ? "" : monthToDate().from));
  const [to, setTo] = useState(initialTo ?? (initialAllDates ? "" : monthToDate().to));
  const [page, setPage] = useState(1);
  const [data, setData] = useState<{ count: number; items: Row[] } | null>(null);
  const [error, setError] = useState("");
  useEffect(() => {
    const controller = new AbortController(); const query = new URLSearchParams({ page: String(page) });
    if (from) query.set("from", from); if (to) query.set("to", to); if (!from && !to) query.set("allDates", "true");
    fetch(`/api/v1/reports/staff-access-audit?${query}`, { signal: controller.signal }).then(async r => { if (!r.ok) throw new Error(); return r.json(); })
      .then(x => { setData(x); setError(""); }).catch(() => { if (!controller.signal.aborted) setError("تعذر تحميل سجل تغييرات الصلاحيات."); });
    return () => controller.abort();
  }, [from, to, page]);
  return <><PageHeader title="سجل تغييرات الصلاحيات" context="المتابعة التشغيلية" /><p>يعرض تغييرات صلاحيات الموظفين ونطاق فروعهم والتحصيل النقدي فقط، وليس كل نشاط الإداريين.</p>
    <div className="report-filters"><label>من تاريخ<input aria-label="من تاريخ" type="date" value={from} onChange={e => { setFrom(e.target.value); setPage(1); }} /></label><label>إلى تاريخ<input aria-label="إلى تاريخ" type="date" value={to} onChange={e => { setTo(e.target.value); setPage(1); }} /></label><button onClick={() => { setFrom(""); setTo(""); setPage(1); }}>مسح</button></div>
    {error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : <section className="list-card">{!data?.items.length ? <EmptyState message="لا توجد تغييرات صلاحيات في الفترة." /> : <AdminDataTable label="أحداث الصلاحيات" rows={data.items} rowKey={x => x.id} columns={[
      { key: "time", header: "الوقت", render: x => new Date(x.createdAtUtc).toLocaleString("ar-EG-u-nu-latn") },
      { key: "actor", header: "نفذ التغيير", primary: true, render: x => x.actorName ?? "حساب غير متاح" },
      { key: "target", header: "حساب الموظف", render: x => x.staffName ?? "حساب غير متاح" },
      { key: "change", header: "نوع التغيير", render: x => x.change },
    ]} />}{data && <nav className="form-actions" aria-label="صفحات سجل الصلاحيات"><button disabled={page === 1} onClick={() => setPage(page - 1)}>السابق</button><span>{page} / {Math.max(1, Math.ceil(data.count / 50))}</span><button disabled={page * 50 >= data.count} onClick={() => setPage(page + 1)}>التالي</button></nav>}</section>}
    <Link href="/dashboard/staff">إدارة صلاحيات الموظفين</Link></>;
}
