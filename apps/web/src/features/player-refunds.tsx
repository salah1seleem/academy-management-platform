"use client";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { csrfRequest } from "./dashboard-api";
import { ErrorState } from "../components/dashboard-shell";
import { formatMoneyAr } from "../lib/formatters";
type Data = { collections: { id: string; amount: number; refunded: number; currency: string }[]; refunds: { id: string; amount: number; currency: string; reason?: string }[] };
export function PlayerRefunds({ playerId }: { playerId: string }) {
  const request = useRef<{ payload: string; key: string } | null>(null);
  const [data, setData] = useState<Data | null>(null); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const load = useCallback(() => fetch(`/api/v1/people/players/${playerId}/refunds`).then(async r => { if (!r.ok) throw new Error(); setData(await r.json()); }).catch(() => setError("تعذر تحميل الاستردادات.")), [playerId]);
  useEffect(() => { void load(); }, [load]);
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); if (busy) return; const form = event.currentTarget; const f = new FormData(form); const body = { collectionId: f.get("collection"), amount: Number(f.get("amount")), reason: f.get("reason") }; const payload = JSON.stringify(body); if (request.current?.payload !== payload) request.current = { payload, key: crypto.randomUUID() }; setBusy(true); setError(""); try { await csrfRequest(`/api/v1/people/players/${playerId}/refunds`, "POST", body, { "Idempotency-Key": request.current!.key }); request.current = null; form.reset(); await load(); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تسجيل الاسترداد."); } finally { setBusy(false); } }
  return <section className="detail-card"><h2>الاسترداد</h2><p>سجّل مبلغًا تم رده بالفعل. لن يُحذف الإيصال ولن يتغير الاشتراك تلقائيًا.</p>{data && <details><summary>تسجيل استرداد جزئي</summary><form className="form-shell" onSubmit={e => void submit(e)}><label>التحصيل<select name="collection" required><option value="">اختر التحصيل</option>{data.collections.filter(c => c.amount > c.refunded).map(c => <option key={c.id} value={c.id}>المتاح: {formatMoneyAr(c.amount - c.refunded, c.currency)}</option>)}</select></label><label>قيمة الاسترداد<input name="amount" type="number" min="0.01" step="0.01" required /></label><label>السبب (اختياري)<textarea name="reason" maxLength={500} /></label><button className="primary-button" disabled={busy}>تأكيد الاسترداد</button></form></details>}{data?.refunds.map(r => <p key={r.id}>{formatMoneyAr(r.amount, r.currency)} · {r.reason}</p>)}{error && <ErrorState message={error} />}</section>;
}
