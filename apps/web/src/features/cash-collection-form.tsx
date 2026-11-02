"use client";
import Link from "next/link";
import { FormEvent, useEffect, useRef, useState } from "react";
import { Banknote, CheckCircle2, Search } from "lucide-react";
import { ErrorState } from "../components/dashboard-shell";
import { formatMoneyAr } from "../lib/formatters";
import { clearFinancialIntent, pendingFinancialIntent } from "../lib/pending-financial-intent";
import { forgetCashCollection, recallCashCollection, rememberCashCollection, type PendingCashCollection } from "../lib/pending-cash-collection";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

type Choice = { enrollmentId: string; player: string; code: string; sport: string; enrollmentBranch?: string; paymentReference?: string; plans: { id: string; name: string }[] };
type Quote = { quoteHash: string; planName: string; originalAmount: string; discountAmount: string; amount: string; currency: string; discountReason?: string; enrollmentBranchId?: string; collectionBranchId?: string; enrollmentBranch?: string; collectionBranch?: string };
export function CashCollectionForm({ sessionId, onCollected, allowCrossBranchSearch = false }: { sessionId: string; onCollected: () => Promise<void>; allowCrossBranchSearch?: boolean }) {
  const [search, setSearch] = useState(""), [items, setItems] = useState<Choice[]>([]), [searched, setSearched] = useState(false);
  const [enrollment, setEnrollment] = useState(""), [plan, setPlan] = useState(""), [quote, setQuote] = useState<Quote | null>(null);
  const [busy, setBusy] = useState(false), [error, setError] = useState(""), [receipt, setReceipt] = useState("");
  const [crossBranch, setCrossBranch] = useState(false);
  const [pending, setPending] = useState<PendingCashCollection | null>(null), [canDiscard, setCanDiscard] = useState(false);
  const sequence = useRef(0), inflight = useRef(false), root = `/api/v1/finance/cash-registers/sessions/${sessionId}`;
  const selected = items.find(i => i.enrollmentId === enrollment);
  useEffect(() => { let active = true; void Promise.resolve().then(() => { if (active) setPending(recallCashCollection(sessionId)); }); return () => { active = false; }; }, [sessionId]);
  function invalidate() { sequence.current++; setQuote(null); setError(""); setReceipt(""); }
  async function lookup(e: FormEvent) {
    e.preventDefault(); if (inflight.current || pending) return; inflight.current = true;
    invalidate(); const version = sequence.current; setBusy(true); setSearched(false); setItems([]); setEnrollment(""); setPlan("");
    try {
      const response = crossBranch
        ? await csrfRequest(`${root}/cross-branch/search`, "POST", { search: search.trim() }, {}, { announce: false })
        : await fetch(`${root}/collection-options?search=${encodeURIComponent(search.trim())}`, { cache: "no-store" });
      if (!response.ok) throw new Error("تعذر البحث في لاعبي فرع الصندوق.");
      const result = await response.json() as { items: Choice[] };
      if (version === sequence.current) { setItems(result.items); setSearched(true); }
    } catch (e) { if (version === sequence.current) setError(e instanceof Error ? e.message : "تعذر البحث."); }
    finally { inflight.current = false; setBusy(false); }
  }
  async function review() {
    if (inflight.current || pending) return; inflight.current = true; setBusy(true); setError(""); setReceipt(""); setQuote(null); const version = ++sequence.current;
    try {
      const reference = selected?.paymentReference;
      const response = await csrfRequest(reference ? `${root}/cross-branch/quote` : `${root}/collection-quote`, "POST", reference ? { reference, planId: plan } : { enrollmentId: enrollment, planId: plan }, {}, { announce: false });
      const result = await response.json() as Quote; if (version === sequence.current) setQuote(result);
    } catch (e) { if (version === sequence.current) setError(e instanceof Error ? e.message : "تعذر مراجعة السعر."); }
    finally { inflight.current = false; setBusy(false); }
  }
  async function collect() {
    if (inflight.current || pending || !quote || !window.confirm(`تأكيد استلام ${formatMoneyAr(quote.amount, quote.currency)} نقدًا من ${selected?.player} وإصدار الإيصال؟`)) return;
    inflight.current = true; setBusy(true); setError("");
    const reference = selected?.paymentReference;
    const body = reference ? { reference, planId: plan, quoteHash: quote.quoteHash } : { enrollmentId: enrollment, planId: plan, quoteHash: quote.quoteHash };
    try {
      const value: PendingCashCollection = { body, external: Boolean(reference), key: await pendingFinancialIntent(`cash-collection:${sessionId}`, body) };
      rememberCashCollection(sessionId, value); setPending(value); setCanDiscard(false); await send(value);
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر بدء التحصيل."); }
    finally { inflight.current = false; setBusy(false); }
  }
  async function settled(receiptId: string) {
    forgetCashCollection(sessionId); clearFinancialIntent(`cash-collection:${sessionId}`); setPending(null); setCanDiscard(false); setReceipt(receiptId); setQuote(null); setError("");
    try { await onCollected(); } catch { setError("تم تسجيل التحصيل والإيصال، لكن تعذر تحديث عرض الوردية. افتح الإيصال أو أعد تحميل الصفحة، ولا تكرر القبض."); }
  }
  async function send(value: PendingCashCollection) {
    try {
      const response = await csrfRequest(value.external ? `${root}/cross-branch/collect` : `${root}/collections`, "POST", value.body, { "Idempotency-Key": value.key });
      await settled((await response.json() as { receiptId: string }).receiptId);
    } catch (e) {
      setError(e instanceof DashboardRequestError ? e.message : "انقطع الاتصال ولم تتأكد نتيجة التحصيل. افحص النتيجة أو أعد إرسال نفس المحاولة.");
      if (e instanceof DashboardRequestError && e.status >= 400 && e.status < 500) {
        // A definitive rejection is not an uncertain transport failure. Check
        // for a committed replay first; never mistake a key conflict for no receipt.
        try {
          const response = await csrfRequest(`${root}/collection-result`, "POST", { key: value.key, reference: "reference" in value.body ? value.body.reference : undefined }, {}, { announce: false });
          const result = await response.json() as { status: string; receiptId?: string };
          if (result.status === "Confirmed" && result.receiptId) await settled(result.receiptId);
          else if (result.status === "NotRecorded") { forgetCashCollection(sessionId); clearFinancialIntent(`cash-collection:${sessionId}`); setPending(null); setQuote(null); if (value.external) { setItems([]); setEnrollment(""); setPlan(""); setSearched(false); } }
        } catch { /* Keep the original pending request until its outcome is known. */ }
      }
    }
  }
  async function recover(retry: boolean) {
    if (!pending || inflight.current) return; inflight.current = true; setBusy(true); setError("");
    try {
      if (retry) await send(pending);
      else {
        const response = await csrfRequest(`${root}/collection-result`, "POST", { key: pending.key, reference: "reference" in pending.body ? pending.body.reference : undefined }, {}, { announce: false });
        const result = await response.json() as { status: string; receiptId?: string; canDiscard: boolean };
        if (result.status === "Confirmed" && result.receiptId) await settled(result.receiptId);
        else { setCanDiscard(result.canDiscard); setError(result.canDiscard ? "لا يوجد تحصيل مسجل، وقد انتهت صلاحية المحاولة. يمكنك إلغاء المحاولة غير المنفذة." : "لم يُسجل تحصيل بهذا المعرف. أعد إرسال نفس المحاولة؛ لا تبدأ قبضًا جديدًا قبل حسم النتيجة."); }
      }
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر فحص النتيجة."); }
    finally { inflight.current = false; setBusy(false); }
  }
  return <section className="detail-card cash-collection-panel"><h2><Banknote aria-hidden="true" /> قبض اشتراك نقدي</h2><p className="muted">اختر تسجيلًا متاحًا لصلاحيتك، ثم راجع السعر وفرع التحصيل قبل تأكيد استلام النقدية. للمالك تحصيل تسجيل من فرع آخر دون نقله.</p>
    {error && <ErrorState message={error} />}
    {pending && <aside className="cash-quote" aria-label="استعادة نتيجة التحصيل"><h3>تحصيل لم تتأكد نتيجته</h3><p>سنفحص نفس المحاولة أو نعيد إرسالها بمعرفها الأصلي، دون بدء تحصيل جديد.</p><div className="form-actions"><button className="secondary-button" disabled={busy} onClick={() => void recover(false)}>فحص نتيجة المحاولة</button><button className="primary-button" disabled={busy} onClick={() => void recover(true)}>إعادة إرسال نفس التحصيل</button>{canDiscard && <button disabled={busy} onClick={() => { forgetCashCollection(sessionId); clearFinancialIntent(`cash-collection:${sessionId}`); setPending(null); setCanDiscard(false); setQuote(null); setItems([]); setEnrollment(""); setPlan(""); setSearched(false); setError(""); }}>إلغاء المحاولة غير المنفذة</button>}</div></aside>}
    {allowCrossBranchSearch && !pending && <label className="cash-cross-choice"><input type="checkbox" checked={crossBranch} disabled={busy} onChange={e => { invalidate(); setCrossBranch(e.target.checked); setItems([]); setEnrollment(""); setPlan(""); setSearched(false); }} /><span>دفع لتسجيل في فرع آخر<small>بحث للدفع فقط، لا يفتح ملف الطفل أو يغير فرعه. مهلة الاختيار 10 دقائق.</small></span></label>}
    {receipt && <div role="status" className="cash-intro"><CheckCircle2 aria-hidden="true" /><span>تم القبض وتحديث رصيد الوردية والاشتراك.</span><Link href={`/dashboard/subscriptions/receipts/${receipt}`}>عرض الإيصال</Link></div>}
    {!pending && <form className="form-shell" onSubmit={lookup}><label>البحث باللاعب أو كوده<input value={search} onChange={e => { invalidate(); setSearch(e.target.value); setItems([]); setEnrollment(""); setPlan(""); setSearched(false); }} minLength={2} maxLength={100} required disabled={busy} placeholder="اكتب حرفين على الأقل" /></label><div className="form-actions"><button className="secondary-button" disabled={busy}><Search size={16} aria-hidden="true" /> بحث عن اللاعب</button></div></form>}
    {searched && !items.length && <p className="muted">لا توجد تسجيلات مطابقة ضمن صلاحيتك.</p>}
    {!pending && !!items.length && <div className="form-shell"><label>تسجيل اللاعب<select value={enrollment} disabled={busy} onChange={e => { invalidate(); setEnrollment(e.target.value); setPlan(""); }}><option value="">اختر التسجيل</option>{items.map(i => <option key={i.enrollmentId} value={i.enrollmentId}>{i.player} · {i.code} · {i.sport}{i.enrollmentBranch && ` · ${i.enrollmentBranch}`}</option>)}</select></label><label>باقة التدريب<select value={plan} disabled={busy || !selected} onChange={e => { invalidate(); setPlan(e.target.value); }}><option value="">اختر الباقة</option>{selected?.plans.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>{selected && !selected.plans.length && <p>لا توجد باقة منشورة تطابق تسجيل اللاعب وعملة الصندوق.</p>}<div className="form-actions"><button className="secondary-button" disabled={busy || !plan || !enrollment} onClick={() => void review()}>مراجعة مبلغ القبض</button></div></div>}
    {!pending && quote && <div className="cash-quote" aria-label="مراجعة القبض"><h3>{selected?.player} · {quote.planName}</h3><dl>{quote.enrollmentBranch && <div><dt>فرع التسجيل</dt><dd>{quote.enrollmentBranch}</dd></div>}{quote.collectionBranch && <div><dt>فرع التحصيل</dt><dd>{quote.collectionBranch}</dd></div>}<div><dt>قيمة الباقة</dt><dd>{formatMoneyAr(quote.originalAmount, quote.currency)}</dd></div><div><dt>الخصم المستحق</dt><dd>{formatMoneyAr(quote.discountAmount, quote.currency)}</dd></div><div><dt>النقدية المطلوب استلامها</dt><dd><strong>{formatMoneyAr(quote.amount, quote.currency)}</strong></dd></div></dl>{quote.enrollmentBranchId !== quote.collectionBranchId && quote.enrollmentBranchId && <p role="note">يُضاف القبض إلى فرع التحصيل؛ يبقى تدريب اللاعب ومجموعته في فرع التسجيل.</p>}{quote.discountReason && <p>{quote.discountReason}</p>}<button className="primary-button" disabled={busy} onClick={() => void collect()}>تأكيد استلام النقدية وإصدار الإيصال</button></div>}
  </section>;
}
