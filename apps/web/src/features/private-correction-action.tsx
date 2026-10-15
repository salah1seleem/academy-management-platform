"use client";
import { useRef, useState } from "react";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { formatMoneyAr } from "../lib/formatters";

type Quote = { month: string; quoteHash: string; canCorrect: boolean; message: string; difference: number | null; currency: string };

export function PrivateCorrectionAction({ path, onChanged }: { path: string; onChanged: () => Promise<void> }) {
  const [month, setMonth] = useState(""), [note, setNote] = useState(""), [quote, setQuote] = useState<Quote | null>(null), [error, setError] = useState(""), [busy, setBusy] = useState(false);
  const keys = useRef(new Map<string, string>());
  const latest = useRef(0);
  async function inspect() {
    const version = ++latest.current;
    setBusy(true); setError(""); setQuote(null);
    try {
      const response = await fetch(`${path}/correction-preview${month ? `?month=${month}-01` : ""}`, { cache: "no-store" });
      if (!response.ok) throw new Error();
      const value: Quote = await response.json();
      if (version === latest.current) { setQuote(value); setMonth(value.month.slice(0, 7)); }
    } catch { if (version === latest.current) setError("تعذر معاينة التصحيح. راجع الصلاحية والاتصال ثم حاول مجددًا."); }
    finally { if (version === latest.current) setBusy(false); }
  }
  async function confirm() {
    if (!quote?.canCorrect || busy) return;
    setBusy(true); setError("");
    const body = { month: quote.month, quoteHash: quote.quoteHash, note: note.trim() || null };
    const signature = JSON.stringify(body);
    if (!keys.current.has(signature)) keys.current.set(signature, crypto.randomUUID());
    try { await csrfRequest(`${path}/corrections`, "POST", body, { "Idempotency-Key": keys.current.get(signature)! }); await onChanged(); }
    catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر تأكيد التصحيح أو تحديث المصدر؛ أعد المحاولة بنفس التأكيد دون تكرار الحركة."); }
    finally { setBusy(false); }
  }
  return <section aria-label="تصحيح عمولة بعد الاعتماد" className="detail-card private-correction-form">
    <h3>تصحيح عمولة بعد الاعتماد</h3>
    <p>يبقى كشف الراتب الأصلي كما هو. اختر شهر التصحيح وراجعه قبل التأكيد؛ ليس صرفًا أو خصمًا تلقائيًا من الأساسي.</p>
    <label>شهر تصحيح العمولة<input type="month" value={month} disabled={busy} onChange={e => { latest.current++; setMonth(e.target.value); setQuote(null); }} /></label>
    <button className="secondary-button" type="button" disabled={busy} onClick={() => void inspect()}>معاينة شهر التصحيح</button>
    {quote && <><p>{quote.message}</p>{quote.difference !== null && <strong>فرق العمولة: {formatMoneyAr(quote.difference, quote.currency)}</strong>}
      {quote.canCorrect && <><label>ملاحظة التصحيح — اختياري<textarea maxLength={500} value={note} disabled={busy} onChange={e => setNote(e.target.value)} /></label><button className="primary-button" disabled={busy} onClick={() => void confirm()}>تأكيد تصحيح العمولة للشهر المختار</button></>}
    </>}
    {error && <p role="alert">{error}</p>}
  </section>;
}
