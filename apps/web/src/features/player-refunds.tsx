"use client";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { csrfRequest } from "./dashboard-api";
import { ErrorState } from "../components/dashboard-shell";
import { formatMoneyAr, formatDateAr } from "../lib/formatters";
type Data = { collections: { id: string; amount: number; refunded: number; allocated: number; available: string; currency: string; confirmedAtUtc?: string }[]; refunds: { id: string; amount: number; currency: string; reason?: string }[] };
export function PlayerRefunds({ playerId }: { playerId: string }) {
  const request = useRef<{ payload: string; key: string } | null>(null);
  const [selected, setSelected] = useState("");
  const [data, setData] = useState<Data | null>(null); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const load = useCallback(() => fetch(`/api/v1/people/players/${playerId}/refunds`, { cache: "no-store" }).then(async r => { if (!r.ok) throw new Error(); setData(await r.json()); }).catch(() => setError("تعذر تحميل الاستردادات.")), [playerId]);
  useEffect(() => { void load(); }, [load]);
  const eligible = data?.collections.filter(c => Number(c.available) > 0) ?? [];
  const collection = eligible.find(c => c.id === selected);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy || !collection) return;
    const form = event.currentTarget; const f = new FormData(form);
    const body = { collectionId: selected, amount: String(f.get("amount")), reason: String(f.get("reason") ?? "").trim() || null };
    const payload = JSON.stringify(body);
    if (request.current?.payload !== payload) request.current = { payload, key: crypto.randomUUID() };
    setBusy(true); setError("");
    try {
      await csrfRequest(`/api/v1/people/players/${playerId}/refunds`, "POST", body, { "Idempotency-Key": request.current!.key });
      request.current = null; form.reset(); setSelected(""); await load();
    } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تسجيل الاسترداد."); }
    finally { setBusy(false); }
  }
  return <section className="detail-card">
    <h2>الاسترداد</h2><p>سجّل مبلغًا تم رده بالفعل. لن يُحذف الإيصال ولن يتغير الاشتراك تلقائيًا.</p>
    {data?.collections.some(c => c.allocated > 0) && <p className="muted">المبالغ المخصصة لمطالبات ليست متاحة للاسترداد مباشرة. راجع الحركات المالية أولًا؛ الاسترداد لا يعيد فتح الدين تلقائيًا.</p>}
    {data && (eligible.length ? <details><summary>تسجيل استرداد جزئي</summary>
      <form className="form-shell" onSubmit={e => void submit(e)}>
        <label>التحصيل<select name="collection" required value={selected} onChange={e => setSelected(e.target.value)} disabled={busy}>
          <option value="">اختر التحصيل</option>
          {eligible.map(c => <option key={c.id} value={c.id}>{formatDateAr(c.confirmedAtUtc)} · المتاح: {formatMoneyAr(c.available, c.currency)}</option>)}
        </select></label>
        <label>قيمة الاسترداد<input name="amount" type="number" min="0.01" step="0.01" max={collection?.available} required disabled={busy} /></label>
        <label>السبب (اختياري)<textarea name="reason" maxLength={500} disabled={busy} /></label>
        <div className="form-actions"><button className="primary-button" disabled={busy || !collection}>تأكيد الاسترداد</button></div>
      </form>
    </details> : <p className="muted">لا يوجد مبلغ متاح للاسترداد المباشر.</p>)}
    {data?.refunds.map(r => <p key={r.id}>{formatMoneyAr(r.amount, r.currency)} · {r.reason}</p>)}
    {error && <ErrorState message={error} />}
  </section>;
}
