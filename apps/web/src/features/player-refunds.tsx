"use client";
import Link from "next/link";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { csrfRequest } from "./dashboard-api";
import { ErrorState } from "../components/dashboard-shell";
import { formatMoneyAr, formatDateAr } from "../lib/formatters";
type Data = { collections: { id: string; amount: number; refunded: number; allocated: number; available: string; currency: string; paymentMethod?: string; confirmedAtUtc?: string }[]; refunds: { id: string; amount: number; currency: string; reason?: string; cashSessionId?: string }[] };
type Quote = { amount: string; available: string; currency: string; balance?: string; quoteHash?: string; cashEnabled: boolean; sessions: { id: string; name: string }[] };
export function PlayerRefunds({ playerId }: { playerId: string }) { return <RefundEditor key={playerId} playerId={playerId} />; }
function RefundEditor({ playerId }: { playerId: string }) {
  const request = useRef<{ payload: string; key: string } | null>(null);
  const [selected, setSelected] = useState(""), [amount, setAmount] = useState(""), [reason, setReason] = useState(""), [method, setMethod] = useState("Online"), [session, setSession] = useState("");
  const [data, setData] = useState<Data | null>(null), [quote, setQuote] = useState<Quote | null>(null);
  const [error, setError] = useState(""), [busy, setBusy] = useState(false), [saved, setSaved] = useState(false);
  const load = useCallback(() => fetch(`/api/v1/people/players/${playerId}/refunds`, { cache: "no-store" }).then(async r => { if (!r.ok) throw new Error(); setData(await r.json()); }).catch(() => setError("تعذر تحميل الاستردادات.")), [playerId]);
  useEffect(() => { void load(); }, [load]);
  const eligible = data?.collections.filter(c => Number(c.available) > 0) ?? [];
  const collection = eligible.find(c => c.id === selected);
  function invalidate() { setQuote(null); setError(""); setSaved(false); }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy || !collection) return;
    setBusy(true); setError(""); setSaved(false);
    try {
      const base = { collectionId: selected, amount, reason: reason.trim() || null };
      if (method === "Cash" && !quote?.quoteHash) {
        const response = await csrfRequest(`/api/v1/people/players/${playerId}/refunds/quote`, "POST", { ...base, cashSessionId: session || null }, {}, { announce: false });
        const q = await response.json() as Quote;
        setQuote(q); return;
      }
      const body = method === "Cash" ? { ...base, cashSessionId: session, quoteHash: quote!.quoteHash } : base;
      const payload = JSON.stringify(body);
      if (request.current?.payload !== payload) request.current = { payload, key: crypto.randomUUID() };
      await csrfRequest(`/api/v1/people/players/${playerId}/refunds`, "POST", body, { "Idempotency-Key": request.current!.key });
      request.current = null; setSelected(""); setAmount(""); setReason(""); setSession(""); setQuote(null); setSaved(true); await load();
    } catch (caught) { setError(caught instanceof Error && !(caught instanceof TypeError) ? caught.message : "تعذر الاتصال. أعد المحاولة بنفس التفاصيل دون صرف المبلغ مرة أخرى."); }
    finally { setBusy(false); }
  }
  return <section className="detail-card" aria-label="استرداد اللاعب">
    <h2>الاسترداد</h2><p>راجع المبلغ ثم سجّل ما تم رده بالفعل. لا يُحذف الإيصال ولا يتغير الاشتراك أو الدين تلقائيًا.</p>
    {data?.collections.some(c => c.allocated > 0) && <p className="muted">المبالغ المخصصة لمطالبات ليست متاحة للاسترداد مباشرة. راجع الحركات المالية أولًا؛ الاسترداد لا يعيد فتح الدين تلقائيًا.</p>}
    {saved && <p role="status">تم تسجيل الاسترداد بنجاح. لا تصرف المبلغ مرة أخرى.</p>}
    {data && (eligible.length ? <details><summary>تسجيل استرداد جزئي</summary>
      <form className="form-shell" onSubmit={e => void submit(e)}>
        <label>التحصيل<select name="collection" required value={selected} onChange={e => { setSelected(e.target.value); setSession(""); setMethod(eligible.find(c => c.id === e.target.value)?.paymentMethod === "Cash" ? "Cash" : "Online"); invalidate(); }} disabled={busy}>
          <option value="">اختر التحصيل</option>
          {eligible.map(c => <option key={c.id} value={c.id}>{formatDateAr(c.confirmedAtUtc)} · المتاح: {formatMoneyAr(c.available, c.currency)}</option>)}
        </select></label>
        <label>قيمة الاسترداد<input name="amount" type="number" min="0.01" step="0.01" max={collection?.available} required disabled={busy} value={amount} onChange={e => { setAmount(e.target.value); invalidate(); }} /></label>
        <label>طريقة رد المبلغ<select value={method} disabled={busy || collection?.paymentMethod === "Cash"} onChange={e => { setMethod(e.target.value); setSession(""); invalidate(); }}><option value="Online">تحويل مُنفّذ خارج النظام</option><option value="Cash">صرف نقدي من وردية مخولة</option></select></label>
        <label>السبب (اختياري)<textarea name="reason" maxLength={500} disabled={busy} value={reason} onChange={e => setReason(e.target.value)} /></label>
        {method === "Online" && <p className="muted">توثيق تحويل أتممته بالفعل، وليس أمرًا لمزود دفع. الاسترداد النقدي يحتاج وردية ومراجعة.</p>}
        {method === "Cash" && quote && <div className="detail-card" role="region" aria-label="مراجعة الاسترداد النقدي">
          <label>وردية الصرف<select value={session} disabled={busy} onChange={e => { setSession(e.target.value); setQuote({ ...quote, quoteHash: undefined, balance: undefined }); }}><option value="">اختر ورديتك</option>{quote.sessions.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
          {!quote.cashEnabled ? <p>النقدي غير مفعل. راجع المالك.</p> : !quote.sessions.length ? <p>لا توجد وردية مخولة في فرع اللاعب وبعملة التحصيل.</p> : !quote.quoteHash ? <p>اختر الوردية ثم راجع المبلغ قبل الصرف.</p> : <><strong>المبلغ {formatMoneyAr(quote.amount, quote.currency)}</strong><p>المتاح من التحصيل {formatMoneyAr(quote.available, quote.currency)} · رصيد الوردية {formatMoneyAr(quote.balance, quote.currency)}</p><p>الاشتراك والحصص والمديونية لا تتغير بهذه العملية.</p></>}
        </div>}
        <div className="form-actions"><button className="primary-button" disabled={busy || !collection}>{method === "Cash" ? quote?.quoteHash ? "تأكيد صرف الاسترداد" : "مراجعة الاسترداد النقدي" : "تأكيد الاسترداد"}</button>{quote?.quoteHash && <button type="button" className="secondary-button" disabled={busy} onClick={() => setQuote({ ...quote, quoteHash: undefined })}>تحديث المراجعة</button>}</div>
      </form>
    </details> : <p className="muted">لا يوجد مبلغ متاح للاسترداد المباشر.</p>)}
    {data?.refunds.map(r => <p key={r.id}>{formatMoneyAr(r.amount, r.currency)} · {r.reason} {r.cashSessionId && <Link href={`/dashboard/finance/cash-registers/sessions/${r.cashSessionId}`}>عرض قيد الصرف</Link>}</p>)}
    {error && <ErrorState message={error} />}
  </section>;
}
