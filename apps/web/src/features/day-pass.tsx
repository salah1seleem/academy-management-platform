"use client";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { GuardianShell } from "../components/guardian-shell";
import { EmptyState, ErrorState, LoadingState } from "../components/dashboard-shell";
import { csrfRequest, uploadAsset } from "./dashboard-api";

type Plan = { id: string; name: string; amount: string; currency: string };
type Booking = { bookingId: string; date: string; startTime: string; endTime: string; resource: string; reservedSeats: number; plans: Plan[] };
type Child = { id: string; name: string };
type Options = { children: Child[]; bookings: Booking[] };
type Quote = { player: string; bookingDate: string; startTime: string; endTime: string; resource: string; plan: string; amount: string; currency: string; quoteHash: string; transfer: { instaPayAddress?: string; walletNumber?: string; recipientName?: string } };
type Ticket = { id: string; playerId: string; resourceBookingId: string; status: "PendingProof" | "UnderReview" | "Confirmed" | "Rejected"; planName: string; amount: string; currency: string; reviewReason?: string; proofAssetId?: string };
type SavedIntent = { fingerprint: string; key: string };
const status: Record<Ticket["status"], string> = { PendingProof: "بانتظار إثبات التحويل", UnderReview: "الإثبات قيد مراجعة الإدارة", Confirmed: "تذكرة مؤكدة · تحقق الهوية عند الدخول", Rejected: "لم يُقبل الإثبات" };
const intentKey = "academy-day-pass-intent";

export function GuardianDayPass() {
  const [options, setOptions] = useState<Options | null>(null), [tickets, setTickets] = useState<Ticket[]>([]);
  const [playerId, setPlayerId] = useState(""), [bookingId, setBookingId] = useState(""), [planId, setPlanId] = useState("");
  const [quote, setQuote] = useState<Quote | null>(null), [active, setActive] = useState<Ticket | null>(null);
  const [file, setFile] = useState<File | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState(""), [message, setMessage] = useState("");
  const load = useCallback(async () => {
    const [choices, mine] = await Promise.all([fetch("/api/v1/guardian/day-pass/options", { cache: "no-store" }), fetch("/api/v1/guardian/day-pass/orders", { cache: "no-store" })]);
    if (!choices.ok || !mine.ok) throw new Error("تعذر تحميل الزيارات؛ راجع إتاحة الخدمة بالأكاديمية.");
    setOptions(await choices.json()); setTickets(await mine.json());
  }, []);
  useEffect(() => { let disposed = false; void Promise.resolve().then(() => { if (!disposed) return load(); }).catch(e => { if (!disposed) setError(e.message); }); return () => { disposed = true; }; }, [load]);
  const selectedBooking = options?.bookings.find(x => x.bookingId === bookingId);
  async function runQuote() {
    if (!playerId || !bookingId || !planId) return;
    setBusy(true); setError(""); setMessage(""); setQuote(null);
    try { const response = await csrfRequest("/api/v1/guardian/day-pass/quote", "POST", { playerId, bookingId, planId }, {}, { announce: false }); setQuote(await response.json()); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر حساب السعر."); }
    finally { setBusy(false); }
  }
  async function order() {
    if (!quote || busy) return;
    setBusy(true); setError(""); setMessage("");
    try {
      const fingerprint = `${playerId}:${bookingId}:${planId}:${quote.quoteHash}`;
      let saved: SavedIntent | null = null;
      try { saved = JSON.parse(sessionStorage.getItem(intentKey) || "null") as SavedIntent | null; } catch { throw new Error("لا يمكن حفظ معرف الطلب بأمان على هذا الجهاز؛ لم يُنشأ طلب. فعّل تخزين الجلسة وحاول مرة أخرى."); }
      if (saved?.fingerprint !== fingerprint) { saved = { fingerprint, key: crypto.randomUUID() }; try { sessionStorage.setItem(intentKey, JSON.stringify(saved)); } catch { throw new Error("تعذر حفظ معرف الطلب بأمان؛ لم يُنشأ طلب."); } }
      const response = await csrfRequest("/api/v1/guardian/day-pass/orders", "POST", { playerId, bookingId, planId, quoteHash: quote.quoteHash }, { "Idempotency-Key": saved!.key }, { announce: false });
      const ticket = await response.json() as Ticket; setActive(ticket); setMessage("تم إنشاء طلب زيارة واحد؛ حوّل المبلغ ثم ارفع الإثبات. لا توجد تذكرة دخول قبل موافقة الإدارة."); await load();
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر إنشاء الطلب؛ راجع قائمته قبل إعادة المحاولة."); }
    finally { setBusy(false); }
  }
  async function proof(ticket: Ticket) {
    if (!file || busy) return; setBusy(true); setError(""); setMessage("");
    try { const asset = await uploadAsset(file, "Proof"); await csrfRequest(`/api/v1/guardian/day-pass/orders/${ticket.id}/proof`, "POST", { assetId: asset.id }, {}, { announce: false }); setFile(null); setMessage("وصل الإثبات للإدارة. سيظهر القرار في قائمة طلباتك."); await load(); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر إرسال الإثبات."); }
    finally { setBusy(false); }
  }
  const resetQuote = () => { setQuote(null); setActive(null); setMessage(""); };
  return <GuardianShell><Link className="back-link" href="/guardian">العودة للرئيسية</Link><header className="guardian-page-header"><span>زيارة مستقلة عن اشتراك التدريب</span><h1>الزيارة اليومية</h1><p>اختر طفلًا مرتبطًا وموعدًا متاحًا. لا يُسجل حضور تدريب ولا تُخصم حصة.</p></header>
    {error && <ErrorState message={error} />}{message && <p className="notice" role="status">{message}</p>}
    {!options && !error ? <LoadingState /> : options && <section className="profile-section"><h2>طلب زيارة</h2>{options.bookings.length === 0 ? <EmptyState message="لا توجد مواعيد زيارة منشورة خلال الثلاثين يومًا القادمة." /> : <div className="form-shell"><label>الطفل<select value={playerId} onChange={e => { setPlayerId(e.target.value); resetQuote(); }}><option value="">اختر الطفل</option>{options.children.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label><label>الموعد<select value={bookingId} onChange={e => { setBookingId(e.target.value); setPlanId(""); resetQuote(); }}><option value="">اختر الموعد</option>{options.bookings.map(b => <option key={b.bookingId} value={b.bookingId}>{b.resource} · {b.date} · {b.startTime.slice(0,5)}–{b.endTime.slice(0,5)}</option>)}</select></label><label>باقة الزيارة<select value={planId} onChange={e => { setPlanId(e.target.value); resetQuote(); }}><option value="">اختر الباقة</option>{selectedBooking?.plans.map(p => <option key={p.id} value={p.id}>{p.name} · {p.amount} {p.currency}</option>)}</select></label><button type="button" className="secondary-button" disabled={busy || !playerId || !bookingId || !planId} onClick={() => void runQuote()}>مراجعة السعر والموعد</button></div>}
      {quote && <div className="detail-card"><h3>{quote.player} · {quote.resource}</h3><p>{quote.bookingDate} · {quote.startTime.slice(0,5)}–{quote.endTime.slice(0,5)}</p><strong>{quote.plan} · {quote.amount} {quote.currency}</strong><p>الدفع تحويل يدوي؛ لا نؤكد استلام المبلغ بمجرد رفع صورة.</p>{quote.transfer.recipientName && <p>{quote.transfer.recipientName}</p>}{[quote.transfer.instaPayAddress, quote.transfer.walletNumber].filter(Boolean).map(v => <p dir="ltr" key={v}>{v}</p>)}<button type="button" className="primary-button" disabled={busy || (!quote.transfer.instaPayAddress && !quote.transfer.walletNumber)} onClick={() => void order()}>إنشاء طلب الزيارة</button></div>}
    </section>}
    <section className="profile-section"><h2>طلبات الزيارة</h2>{tickets.length === 0 ? <EmptyState message="لم تُنشئ طلب زيارة بعد." /> : tickets.map(t => <article key={t.id} className="profile-row"><div><b>{t.planName} · {t.amount} {t.currency}</b><small>{options?.children.find(c => c.id === t.playerId)?.name ?? "طفل مرتبط"} · {status[t.status]}</small>{t.reviewReason && <small>سبب الرفض: {t.reviewReason}</small>}</div>{t.status === "PendingProof" && <div className="row-actions"><input type="file" aria-label={`إثبات تحويل ${t.planName}`} accept="image/jpeg,image/png,image/webp" onChange={e => { setActive(t); setFile(e.target.files?.[0] ?? null); }} /><button type="button" className="secondary-button" disabled={busy || !file || active?.id !== t.id} onClick={() => void proof(t)}>رفع الإثبات</button></div>}</article>)}</section>
  </GuardianShell>;
}
