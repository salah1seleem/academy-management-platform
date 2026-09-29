"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { GuardianShell } from "../../../components/guardian-shell";
import { formatDateAr, formatMoneyAr } from "../../../lib/formatters";
import { EmptyState, ErrorState, LoadingState } from "../../../components/dashboard-shell";
type Row = { id: string; receiptNumber: string; player: string; sport: string; plan: string; amount: number; currency: string; paidAtUtc: string };
export default function Page() { const [items, setItems] = useState<Row[] | null>(null); const [error, setError] = useState(""); useEffect(() => { fetch("/api/v1/guardian/subscriptions/receipts").then(r => { if (!r.ok) throw new Error(); return r.json(); }).then((x: { items: Row[] }) => setItems(x.items)).catch(() => setError("تعذر تحميل إيصالات الدفع.")); }, []); return <GuardianShell><header className="guardian-page-header"><span>سجل المدفوعات</span><h1>إيصالات الدفع</h1></header>{error ? <ErrorState message={error} /> : !items ? <LoadingState /> : items.length === 0 ? <EmptyState message="لا توجد إيصالات دفع." /> : <section className="guardian-list">{items.map(x => <Link key={x.id} href={`/guardian/receipts/${x.id}`}><strong>{x.receiptNumber} — {x.player}</strong><span>{x.sport} · {x.plan}</span><b>{formatMoneyAr(x.amount, x.currency)} · {formatDateAr(x.paidAtUtc)}</b></Link>)}</section>}</GuardianShell>; }
