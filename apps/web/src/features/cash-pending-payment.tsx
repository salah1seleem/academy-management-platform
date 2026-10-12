"use client";
import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { formatMoneyAr } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";

type Options = { amount: string; currency: string; sessions: { id: string; name: string; branch: string }[] };
export function CashPendingPayment({ id, onCollected }: { id: string; onCollected: () => Promise<unknown> }) {
  const [data, setData] = useState<Options | null>(null), [error, setError] = useState("");
  const [session, setSession] = useState(""), [busy, setBusy] = useState(false), [refresh, setRefresh] = useState(0);
  useEffect(() => {
    let active = true;
    fetch(`/api/v1/manage/payments/${id}/cash-options`, { cache: "no-store" }).then(async response => {
      const result = await response.json();
      if (!response.ok) throw new Error(result.title ?? "تعذر تحميل ورديات التحصيل.");
      return result as Options;
    }).then(result => { if (active) { setData(result); setError(""); } })
      .catch(e => { if (active) setError(e instanceof Error ? e.message : "تعذر تحميل ورديات التحصيل."); });
    return () => { active = false; };
  }, [id, refresh]);
  async function collect(event: FormEvent) {
    event.preventDefault();
    if (!data || !session || busy || !window.confirm(`تأكيد استلام ${formatMoneyAr(data.amount, data.currency)} نقدًا وإضافته للوردية؟`)) return;
    setBusy(true); setError("");
    try {
      await csrfRequest(`/api/v1/manage/payments/${id}/collect`, "POST", { paymentMethod: "Cash", cashSessionId: session, expectedAmount: data.amount });
      await onCollected();
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر التحصيل. راجع الطلب قبل إعادة المحاولة."); }
    finally { setBusy(false); }
  }
  return <section className="detail-card" aria-label="تحصيل طلب التجديد نقدًا">
    <h3>تحصيل نقدي من ورديتك</h3>
    {error && <ErrorState message={error} />}
    {!data && !error && <LoadingState />}
    {data && <form onSubmit={collect} className="form-shell">
      <p>المبلغ المطلوب: <strong>{formatMoneyAr(data.amount, data.currency)}</strong></p>
      <label>وردية التحصيل<select value={session} required disabled={busy} onChange={e => setSession(e.target.value)}>
        <option value="">اختر ورديتك المفتوحة</option>
        {data.sessions.map(s => <option key={s.id} value={s.id}>{s.name} · {s.branch}</option>)}
      </select></label>
      {!data.sessions.length && <p>لا توجد وردية مخولة مفتوحة في فرع اللاعب وبعملته.</p>}
      <div className="form-actions"><button className="primary-button" disabled={!session || busy}>{busy ? "جارٍ التحصيل…" : "تأكيد التحصيل النقدي وإصدار الإيصال"}</button></div>
    </form>}
    <div className="form-actions"><Link className="secondary-button" href="/dashboard/finance/cash-registers">فتح الصناديق والورديات</Link>
      <button className="secondary-button" type="button" disabled={busy} onClick={() => { setData(null); setSession(""); setError(""); setRefresh(n => n + 1); }}>تحديث المبلغ والورديات</button>
    </div>
  </section>;
}
