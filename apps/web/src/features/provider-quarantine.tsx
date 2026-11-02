"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { ShieldAlert } from "lucide-react";
import { AdminDataTable as DataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { monthToDate } from "../lib/date-range";
import { formatDateTimeAr, formatMoneyAr } from "../lib/formatters";

const root = "/api/v1/finance/provider-quarantine", route = "/dashboard/finance/provider-quarantine";
export type ProviderIssue = { id: string; paymentRequestId: string; provider: string; providerEventId: string; reasonCode: string; receivedStatus: string; receivedReference: string; expectedReference: string; receivedAmount: string; expectedAmount: string; receivedCurrency: string; expectedCurrency: string; createdAtUtc: string; paymentStatus: string; player: string; branch: string; receiptId?: string };
const reasons: Record<string, string> = { "amount-mismatch": "المبلغ مختلف", "currency-mismatch": "العملة مختلفة", "reference-mismatch": "مرجع الدفع مختلف", "provider-mismatch": "مصدر الدفع مختلف", "request-terms-mismatch": "تفاصيل الطلب غير متطابقة", "event-reuse": "رقم الحدث مستخدم بتفاصيل أخرى", "unsupported-status": "حالة الدفع غير معروفة", "registration-format-conflict": "نوع التسجيل غير متوافق" };
const states: Record<string, string> = { Pending: "بانتظار تأكيد صحيح", Confirmed: "مؤكد بإشارة أخرى صحيحة", Failed: "لم يتم الدفع", Cancelled: "ملغى", Expired: "انتهت صلاحية الطلب", Created: "طلب جديد" };
export const issueReason = (code: string) => reasons[code] ?? "اختلاف يتطلب المتابعة";
function useRead<T>(path: string) {
  const [data, setData] = useState<T | null>(null), [error, setError] = useState("");
  useEffect(() => { let active = true; const abort = new AbortController(); void fetch(path, { cache: "no-store", signal: abort.signal }).then(async response => { if (!response.ok) throw new Error(response.status === 403 ? "هذه المتابعة متاحة للمالك فقط." : "تعذر تحميل سجل المتابعة."); return response.json(); }).then(value => { if (active) setData(value); }).catch(e => { if (active && e.name !== "AbortError") setError(e.message); }); return () => { active = false; abort.abort(); }; }, [path]);
  return { data, error };
}
function Notice() { return <div className="detail-card"><ShieldAlert aria-hidden="true" /><strong>اختلافات تحتاج متابعة، وليست تحصيلات ناجحة</strong><p>لا تفعّل هذه الإشارة اشتراكًا ولا تنشئ قبضًا أو رصيدًا زائدًا. راجع تفاصيل الطلب وسجل المزود؛ لا تعتمد صورة أو رابط العودة كتأكيد دفع.</p></div>; }
export function ProviderQuarantine() {
  const [range, setRange] = useState(() => monthToDate()), [page, setPage] = useState(1);
  const query = new URLSearchParams({ page: String(page), ...(range.from ? { from: range.from } : {}), ...(range.to ? { to: range.to } : {}) }).toString();
  return <><PageHeader title="متابعة اختلافات الدفع" context="المالية / متابعة المالك" /><Notice /><section className="filter-bar" aria-label="فلاتر اختلافات الدفع">
    <label>من تاريخ<input type="date" value={range.from} onChange={e => { setRange({ ...range, from: e.target.value }); setPage(1); }} /></label>
    <label>إلى تاريخ<input type="date" value={range.to} onChange={e => { setRange({ ...range, to: e.target.value }); setPage(1); }} /></label>
    <button className="secondary-button" onClick={() => { setRange({ from: "", to: "" }); setPage(1); }}>مسح الفلاتر</button>
  </section><IssueRows key={query} query={query} onPage={setPage} /></>;
}
function IssueRows({ query, onPage }: { query: string; onPage: (page: number) => void }) {
  const { data, error } = useRead<{ total: number; page: number; pageSize: number; items: ProviderIssue[] }>(`${root}?${query}`);
  if (error) return <ErrorState message={error} />; if (!data) return <LoadingState />;
  return <><p role="status">{data.total} اختلاف مسجل ضمن الفترة</p>{!data.items.length ? <EmptyState title="لا توجد اختلافات في هذه الفترة" /> : <DataTable label="اختلافات الدفع" rows={data.items} rowKey={r => r.id} columns={[
    { key: "player", header: "اللاعب والفرع", primary: true, render: r => <><strong>{r.player}</strong><small>{r.branch}</small></> },
    { key: "reason", header: "سبب المتابعة", render: r => issueReason(r.reasonCode) },
    { key: "expected", header: "المتوقع", render: r => formatMoneyAr(r.expectedAmount, r.expectedCurrency) },
    { key: "received", header: "الوارد", render: r => formatMoneyAr(r.receivedAmount, r.receivedCurrency) },
    { key: "time", header: "وقت الرصد", render: r => formatDateTimeAr(r.createdAtUtc) },
    { key: "action", header: "التفاصيل", render: r => <Link href={`${route}/${r.id}`}>مراجعة الاختلاف</Link> },
  ]} />}<nav className="form-actions" aria-label="صفحات سجل الاختلافات"><button className="secondary-button" disabled={data.page <= 1} onClick={() => onPage(data.page - 1)}>السابق</button><span>{data.page} / {Math.max(1, Math.ceil(data.total / data.pageSize))}</span><button className="secondary-button" disabled={data.page * data.pageSize >= data.total} onClick={() => onPage(data.page + 1)}>التالي</button></nav></>;
}
export function ProviderQuarantineDetail({ id }: { id: string }) { return <IssueDetail key={id} id={id} />; }
function IssueDetail({ id }: { id: string }) {
  const { data: d, error } = useRead<ProviderIssue>(`${root}/${id}`);
  return <><PageHeader title="تفاصيل اختلاف الدفع" context="المالية / متابعة المالك" action={{ label: "سجل المتابعة", href: route, tone: "secondary" }} />{error && <ErrorState message={error} />}{!d ? !error && <LoadingState /> : <><Notice /><section className="detail-card"><h2>{d.player}</h2><p>{d.branch} · {issueReason(d.reasonCode)}</p><p>{formatDateTimeAr(d.createdAtUtc)}</p><div className="cash-session-stats"><div><small>المبلغ المتوقع</small><strong>{formatMoneyAr(d.expectedAmount, d.expectedCurrency)}</strong></div><div><small>المبلغ الوارد في الإشارة</small><strong>{formatMoneyAr(d.receivedAmount, d.receivedCurrency)}</strong></div></div><dl><dt>مرجع الطلب</dt><dd dir="ltr">{d.expectedReference}</dd><dt>المرجع الوارد</dt><dd dir="ltr">{d.receivedReference}</dd><dt>رقم حدث المزود</dt><dd dir="ltr">{d.providerEventId}</dd><dt>حالة الطلب الآن</dt><dd>{states[d.paymentStatus] ?? "تحتاج مراجعة"}</dd></dl><div className="form-actions"><Link className="secondary-button" href={`/dashboard/subscriptions/payments/${d.paymentRequestId}`}>تفاصيل طلب الدفع</Link>{d.receiptId && <Link className="secondary-button" href={`/dashboard/subscriptions/receipts/${d.receiptId}`}>إيصال التأكيد الصحيح</Link>}</div></section></>}</>;
}
