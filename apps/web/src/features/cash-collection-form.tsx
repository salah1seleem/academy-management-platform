"use client";
import Link from "next/link";
import { FormEvent, useRef, useState } from "react";
import { Banknote, CheckCircle2, Search } from "lucide-react";
import { ErrorState } from "../components/dashboard-shell";
import { formatMoneyAr } from "../lib/formatters";
import { clearFinancialIntent, pendingFinancialIntent } from "../lib/pending-financial-intent";
import { csrfRequest } from "./dashboard-api";

type Choice = { enrollmentId: string; player: string; code: string; sport: string; enrollmentBranch?: string; plans: { id: string; name: string }[] };
type Quote = { quoteHash: string; planName: string; originalAmount: string; discountAmount: string; amount: string; currency: string; discountReason?: string; enrollmentBranchId?: string; collectionBranchId?: string; enrollmentBranch?: string; collectionBranch?: string };
export function CashCollectionForm({ sessionId, onCollected }: { sessionId: string; onCollected: () => Promise<void> }) {
  const [search, setSearch] = useState(""), [items, setItems] = useState<Choice[]>([]), [searched, setSearched] = useState(false);
  const [enrollment, setEnrollment] = useState(""), [plan, setPlan] = useState(""), [quote, setQuote] = useState<Quote | null>(null);
  const [busy, setBusy] = useState(false), [error, setError] = useState(""), [receipt, setReceipt] = useState("");
  const sequence = useRef(0), inflight = useRef(false), root = `/api/v1/finance/cash-registers/sessions/${sessionId}`;
  const selected = items.find(i => i.enrollmentId === enrollment);
  function invalidate() { sequence.current++; setQuote(null); setError(""); setReceipt(""); }
  async function lookup(e: FormEvent) {
    e.preventDefault(); if (inflight.current) return; inflight.current = true;
    invalidate(); const version = sequence.current; setBusy(true); setSearched(false); setItems([]); setEnrollment(""); setPlan("");
    try {
      const response = await fetch(`${root}/collection-options?search=${encodeURIComponent(search.trim())}`, { cache: "no-store" });
      if (!response.ok) throw new Error("تعذر البحث في لاعبي فرع الصندوق.");
      const result = await response.json() as { items: Choice[] };
      if (version === sequence.current) { setItems(result.items); setSearched(true); }
    } catch (e) { if (version === sequence.current) setError(e instanceof Error ? e.message : "تعذر البحث."); }
    finally { inflight.current = false; setBusy(false); }
  }
  async function review() {
    if (inflight.current) return; inflight.current = true; setBusy(true); setError(""); setReceipt(""); setQuote(null); const version = ++sequence.current;
    try {
      const response = await csrfRequest(`${root}/collection-quote`, "POST", { enrollmentId: enrollment, planId: plan }, {}, { announce: false });
      const result = await response.json() as Quote; if (version === sequence.current) setQuote(result);
    } catch (e) { if (version === sequence.current) setError(e instanceof Error ? e.message : "تعذر مراجعة السعر."); }
    finally { inflight.current = false; setBusy(false); }
  }
  async function collect() {
    if (inflight.current || !quote || !window.confirm(`تأكيد استلام ${formatMoneyAr(quote.amount, quote.currency)} نقدًا من ${selected?.player} وإصدار الإيصال؟`)) return;
    inflight.current = true; setBusy(true); setError("");
    const body = { enrollmentId: enrollment, planId: plan, quoteHash: quote.quoteHash }, scope = `cash-collection:${sessionId}`;
    try {
      const response = await csrfRequest(`${root}/collections`, "POST", body, { "Idempotency-Key": await pendingFinancialIntent(scope, body) });
      const result = await response.json() as { receiptId: string }; clearFinancialIntent(scope); setReceipt(result.receiptId); setQuote(null);
      await onCollected();
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر تسجيل القبض. أعد إرسال نفس العملية إن انقطع الاتصال."); }
    finally { inflight.current = false; setBusy(false); }
  }
  return <section className="detail-card cash-collection-panel"><h2><Banknote aria-hidden="true" /> قبض اشتراك نقدي</h2><p className="muted">اختر تسجيلًا متاحًا لصلاحيتك، ثم راجع السعر وفرع التحصيل قبل تأكيد استلام النقدية. للمالك تحصيل تسجيل من فرع آخر دون نقله.</p>
    {error && <ErrorState message={error} />}
    {receipt && <div role="status" className="cash-intro"><CheckCircle2 aria-hidden="true" /><span>تم القبض وتحديث رصيد الوردية والاشتراك.</span><Link href={`/dashboard/subscriptions/receipts/${receipt}`}>عرض الإيصال</Link></div>}
    <form className="form-shell" onSubmit={lookup}><label>البحث باللاعب أو كوده<input value={search} onChange={e => { invalidate(); setSearch(e.target.value); setItems([]); setEnrollment(""); setPlan(""); setSearched(false); }} minLength={2} maxLength={100} required disabled={busy} placeholder="اكتب حرفين على الأقل" /></label><div className="form-actions"><button className="secondary-button" disabled={busy}><Search size={16} aria-hidden="true" /> بحث عن اللاعب</button></div></form>
    {searched && !items.length && <p className="muted">لا توجد تسجيلات مطابقة ضمن صلاحيتك.</p>}
    {!!items.length && <div className="form-shell"><label>تسجيل اللاعب<select value={enrollment} disabled={busy} onChange={e => { invalidate(); setEnrollment(e.target.value); setPlan(""); }}><option value="">اختر التسجيل</option>{items.map(i => <option key={i.enrollmentId} value={i.enrollmentId}>{i.player} · {i.code} · {i.sport}{i.enrollmentBranch && ` · ${i.enrollmentBranch}`}</option>)}</select></label><label>باقة التدريب<select value={plan} disabled={busy || !selected} onChange={e => { invalidate(); setPlan(e.target.value); }}><option value="">اختر الباقة</option>{selected?.plans.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>{selected && !selected.plans.length && <p>لا توجد باقة منشورة تطابق تسجيل اللاعب وعملة الصندوق.</p>}<div className="form-actions"><button className="secondary-button" disabled={busy || !plan || !enrollment} onClick={() => void review()}>مراجعة مبلغ القبض</button></div></div>}
    {quote && <div className="cash-quote" aria-label="مراجعة القبض"><h3>{selected?.player} · {quote.planName}</h3><dl>{quote.enrollmentBranch && <div><dt>فرع التسجيل</dt><dd>{quote.enrollmentBranch}</dd></div>}{quote.collectionBranch && <div><dt>فرع التحصيل</dt><dd>{quote.collectionBranch}</dd></div>}<div><dt>قيمة الباقة</dt><dd>{formatMoneyAr(quote.originalAmount, quote.currency)}</dd></div><div><dt>الخصم المستحق</dt><dd>{formatMoneyAr(quote.discountAmount, quote.currency)}</dd></div><div><dt>النقدية المطلوب استلامها</dt><dd><strong>{formatMoneyAr(quote.amount, quote.currency)}</strong></dd></div></dl>{quote.enrollmentBranchId !== quote.collectionBranchId && quote.enrollmentBranchId && <p role="note">يُضاف القبض إلى فرع التحصيل؛ يبقى تدريب اللاعب ومجموعته في فرع التسجيل.</p>}{quote.discountReason && <p>{quote.discountReason}</p>}<button className="primary-button" disabled={busy} onClick={() => void collect()}>تأكيد استلام النقدية وإصدار الإيصال</button></div>}
  </section>;
}
