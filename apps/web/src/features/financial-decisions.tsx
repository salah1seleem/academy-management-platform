"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { monthToDate } from "../lib/date-range";
import { formatMoneyAr } from "../lib/formatters";

type Row = { sourceId: string; branchId: string; branchName: string | null; kind: "Expense" | "RenewalProof"; state: string;
  actorName: string | null; occurredAtUtc: string; amount: number; currency: string };
const actions: Record<string, string> = { Created: "تسجيل مصروف", Approved: "اعتماد مصروف", Posted: "ترحيل مصروف", Accepted: "قبول إثبات تجديد", Rejected: "رفض إثبات تجديد" };

export function FinancialDecisions({ initialFrom, initialTo, initialBranchId, initialAllDates = false }: { initialFrom?: string; initialTo?: string; initialBranchId?: string; initialAllDates?: boolean }) {
  const defaults = monthToDate();
  const [from, setFrom] = useState(initialFrom ?? (initialAllDates ? "" : defaults.from));
  const [to, setTo] = useState(initialTo ?? (initialAllDates ? "" : defaults.to));
  const [branchId, setBranchId] = useState(initialBranchId ?? "");
  const [branches, setBranches] = useState<{ id: string; arabicName: string }[]>([]);
  const [page, setPage] = useState(1);
  const [data, setData] = useState<{ count: number; items: Row[] } | null>(null);
  const [error, setError] = useState("");
  useEffect(() => { fetch("/api/v1/manage/structure/options").then(r => r.ok ? r.json() : null).then(x => setBranches(x?.branches ?? [])).catch(() => setBranches([])); }, []);
  useEffect(() => {
    const controller = new AbortController(); const query = new URLSearchParams({ page: String(page) });
    if (from) query.set("from", from); if (to) query.set("to", to); if (!from && !to) query.set("allDates", "true");
    if (branchId) query.set("branchId", branchId);
    fetch(`/api/v1/reports/financial-decisions?${query}`, { signal: controller.signal }).then(async r => { if (!r.ok) throw new Error(); return r.json(); })
      .then(x => { setData(x); setError(""); }).catch(() => { if (!controller.signal.aborted) setError("تعذر تحميل القرارات المالية."); });
    return () => controller.abort();
  }, [from, to, branchId, page]);
  return <><PageHeader title="القرارات المالية المحفوظة" context="المالك / الرقابة" />
    <p>يعرض قرارات المصروفات التشغيلية ومراجعة إثباتات تجديد الاشتراك فقط، من سجلاتها الأصلية. ليس سجلًا شاملًا لكل أعمال الإدارة، ولا إثبات تسوية بنكية.</p>
    <div className="report-filters"><label>من تاريخ<input aria-label="من تاريخ" type="date" value={from} onChange={e => { setFrom(e.target.value); setPage(1); }} /></label>
      <label>إلى تاريخ<input aria-label="إلى تاريخ" type="date" value={to} onChange={e => { setTo(e.target.value); setPage(1); }} /></label>
      <label>الفرع<select aria-label="الفرع" value={branchId} onChange={e => { setBranchId(e.target.value); setPage(1); }}><option value="">كل الفروع</option>{branches.map(b => <option key={b.id} value={b.id}>{b.arabicName}</option>)}</select></label>
      <button type="button" onClick={() => { setFrom(""); setTo(""); setBranchId(""); setPage(1); }}>مسح</button></div>
    {error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : <section className="list-card">{!data?.items.length ? <EmptyState message="لا توجد قرارات موثقة ضمن الفلاتر." /> :
      <AdminDataTable label="قرارات مالية" rows={data.items} rowKey={x => `${x.kind}-${x.sourceId}-${x.state}`} columns={[
        { key: "action", header: "الإجراء", primary: true, render: x => actions[x.state] ?? "قرار مالي" },
        { key: "actor", header: "المسؤول", render: x => x.actorName ?? "حساب غير متاح" },
        { key: "branch", header: "الفرع", render: x => x.branchName ?? "فرع غير متاح" },
        { key: "amount", header: "المبلغ المرتبط", render: x => formatMoneyAr(x.amount, x.currency) },
        { key: "time", header: "الوقت", render: x => new Date(x.occurredAtUtc).toLocaleString("ar-EG-u-nu-latn") },
        { key: "details", header: "المصدر", render: x => <Link href={x.kind === "Expense" ? `/dashboard/finance/operating-expenses?expenseId=${x.sourceId}` : `/dashboard/subscriptions/payments/${x.sourceId}`}>فتح السجل</Link> },
      ]} />}{data && <nav className="form-actions" aria-label="صفحات القرارات المالية"><button disabled={page === 1} onClick={() => setPage(page - 1)}>السابق</button><span>{page} / {Math.max(1, Math.ceil(data.count / 50))}</span><button disabled={page * 50 >= data.count} onClick={() => setPage(page + 1)}>التالي</button></nav>}</section>}</>;
}
