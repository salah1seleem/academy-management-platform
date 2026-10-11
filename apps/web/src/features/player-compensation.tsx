"use client";
import { useState } from "react";
import { CalendarCheck, RotateCcw } from "lucide-react";
import { formatDateAr } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";
export type Compensation = { id: string; sport: string; cause: string; unit: string; units: number; consumed: number; allocated: number; validFrom: string; validTo: string; sourceDate: string; bookings: { id: string; group: string; date: string; units: number; consumed: number; state: string; version: number }[] };
type Slot = { id: string; group: string; date: string; start: string; end: string };
type Options = { today: string; sources: { id: string; group: string; date: string }[]; targets: Slot[] };
type Review = { fingerprint: string; units: number; conflicts: string[]; available?: number; expectedPlayers?: number; capacity?: number | null };
const unitName = (u: string) => u === "Minute" ? "دقيقة" : "حصة";
export function CompensationCards({ items }: { items?: Compensation[] }) {
  if (!items?.length) return null;
  return <section className="compensation-cards" aria-label="التعويضات المعتمدة"><h3><CalendarCheck size={18} aria-hidden="true" /> التعويضات المعتمدة</h3>{items.map(g => <article key={g.id}><strong>{g.sport} · {g.units} {unitName(g.unit)}</strong><small>عن حصة {formatDateAr(g.sourceDate)} · حتى {formatDateAr(g.validTo)}</small><div className="team-chip">استُخدم {g.consumed} · غير محجوز {g.units - g.allocated}</div>{g.bookings.filter(b => b.state === "Reserved").map(b => <small key={b.id}>{b.group} · {formatDateAr(b.date)}{b.consumed > 0 ? " · تم الحضور بالتعويض" : " · موعد محجوز"}</small>)}</article>)}</section>;
}
export function PlayerCompensation({ enrollmentId, onSaved }: { enrollmentId: string; onSaved?: () => void }) {
  const [open, setOpen] = useState(false), [options, setOptions] = useState<Options>(), [items, setItems] = useState<Compensation[]>([]), [loading, setLoading] = useState(false);
  const [source, setSource] = useState(""), [cause, setCause] = useState("CancelledSession"), [unit, setUnit] = useState("Session"), [from, setFrom] = useState(""), [to, setTo] = useState("");
  const [grant, setGrant] = useState(""), [target, setTarget] = useState(""), [busy, setBusy] = useState(false), [message, setMessage] = useState(""), [review, setReview] = useState<{ mode: "grant" | "book"; data: Review; key: string }>();
  async function load() {
    const [o, g] = await Promise.all([fetch(`/api/v1/people/enrollments/${enrollmentId}/compensation/options`), fetch(`/api/v1/people/enrollments/${enrollmentId}/compensations`)]);
    if (!o.ok || !g.ok) throw new Error("تعذر تحميل التعويضات في نطاق صلاحياتك.");
    const next = await o.json() as Options, grants = await g.json() as Compensation[]; setOptions(next); setItems(grants);
    setFrom(v => v || next.today); setTo(v => v || new Date(Date.parse(`${next.today}T12:00:00Z`) + 30 * 86400000).toISOString().slice(0, 10)); setGrant(v => v || grants[0]?.id || "");
  }
  function toggle(next: boolean) {
    setOpen(next); if (!next) return; setLoading(true);
    void load().catch(e => setMessage(e instanceof Error ? e.message : "تعذر التحميل.")).finally(() => setLoading(false));
  }
  function invalidate() { setReview(undefined); setMessage(""); }
  const body = () => ({ sourceSessionId: source, cause, unit, validFrom: from, validTo: to });
  async function preview(mode: "grant" | "book") {
    setBusy(true); invalidate();
    try { const path = mode === "grant" ? `/api/v1/people/enrollments/${enrollmentId}/compensation/preview` : `/api/v1/people/compensations/${grant}/booking/preview`; const r = await csrfRequest(path, "POST", mode === "grant" ? body() : { trainingSessionId: target }); setReview({ mode, data: await r.json() as Review, key: crypto.randomUUID() }); }
    catch (e) { setMessage(e instanceof Error ? e.message : "تعذر المعاينة."); } finally { setBusy(false); }
  }
  async function confirm() {
    if (!review || review.data.conflicts.length) return; setBusy(true);
    try { const path = review.mode === "grant" ? `/api/v1/people/enrollments/${enrollmentId}/compensation` : `/api/v1/people/compensations/${grant}/booking`; await csrfRequest(path, "POST", { ...(review.mode === "grant" ? body() : { trainingSessionId: target }), fingerprint: review.data.fingerprint }, { "Idempotency-Key": review.key }); setReview(undefined); setSource(""); setTarget(""); await load(); setMessage(review.mode === "grant" ? "تم اعتماد التعويض دون تحصيل أو تمديد اشتراك." : "تم حجز الحصة التعويضية وتحديث مواعيد ولي الأمر."); onSaved?.(); }
    catch (e) { setReview(undefined); setMessage(e instanceof Error ? e.message : "تغيرت البيانات؛ أعد المعاينة."); } finally { setBusy(false); }
  }
  async function cancel(id: string, version: number) {
    if (!window.confirm("تحرير هذا الحجز وإعادة وحداته إلى التعويض؟ يبقى سجله محفوظًا.")) return; setBusy(true); invalidate();
    try { await csrfRequest(`/api/v1/people/compensation-bookings/${id}/cancel`, "POST", { version }); await load(); setMessage("تم تحرير الحجز مع حفظ سجله."); onSaved?.(); } catch (e) { setMessage(e instanceof Error ? e.message : "تعذر تحرير الحجز."); } finally { setBusy(false); }
  }
  return <details className="placement-level" onToggle={e => toggle(e.currentTarget.open)}><summary><RotateCcw size={17} aria-hidden="true" /> التعويضات والحصص البديلة</summary>{open && <div className="form-shell">
    {loading ? <p role="status">جارٍ تحميل التعويضات…</p> : options && <>
      <CompensationCards items={items} />
      <fieldset disabled={busy}><legend>اعتماد تعويض من واقعة</legend><p className="muted">تعويض إضافي صريح من الإدارة، لا خصم من الرصيد الأصلي أو تحصيل جديد. السبب مربوط بحصة فعلية؛ صلاحية بحد أقصى90 يومًا.</p><div className="form-grid">
        <label>الحصة الأصلية<select aria-label="الحصة الأصلية" value={source} onChange={e => { setSource(e.target.value); invalidate(); }}><option value="">اختر الحصة</option>{options.sources.map(s => <option key={s.id} value={s.id}>{s.group} · {s.date}</option>)}</select></label>
        <label>سبب التعويض<select value={cause} onChange={e => { setCause(e.target.value); invalidate(); }}><option value="CancelledSession">إلغاء الحصة</option><option value="CoachAbsent">غياب الجهاز الفني</option><option value="ApprovedAbsence">غياب طفل وافقت الإدارة على تعويضه</option></select></label>
        <label>وحدة التعويض<select value={unit} onChange={e => { setUnit(e.target.value); invalidate(); }}><option value="Session">حصة واحدة</option><option value="Minute">دقائق الحصة الأصلية</option></select></label>
        <label>صالح من<input type="date" min={options.today} value={from} onChange={e => { setFrom(e.target.value); invalidate(); }} /></label><label>صالح إلى<input type="date" min={from} value={to} onChange={e => { setTo(e.target.value); invalidate(); }} /></label>
      </div><button className="secondary-button" disabled={!source || !from || !to} onClick={() => void preview("grant")}>معاينة استحقاق التعويض</button></fieldset>
      {!!items.length && <fieldset disabled={busy}><legend>حجز حصة بالتعويض</legend><div className="form-grid"><label>التعويض<select value={grant} onChange={e => { setGrant(e.target.value); invalidate(); }}><option value="">اختر التعويض</option>{items.map(g => <option value={g.id} key={g.id}>{g.sport} · عن {g.sourceDate} · غير محجوز {g.units - g.allocated} {unitName(g.unit)}</option>)}</select></label><label>الحصة التعويضية<select value={target} onChange={e => { setTarget(e.target.value); invalidate(); }}><option value="">اختر موعدًا</option>{options.targets.map(s => <option key={s.id} value={s.id}>{s.group} · {s.date} · {s.start.slice(0, 5)}–{s.end.slice(0, 5)}</option>)}</select></label></div><button className="secondary-button" disabled={!grant || !target} onClick={() => void preview("book")}>معاينة حجز التعويض</button></fieldset>}
      {items.flatMap(g => g.bookings).filter(b => b.state === "Reserved" && b.consumed === 0).map(b => <div className="profile-row" key={b.id}><span>{b.group} · {b.date}</span><button className="secondary-button" disabled={busy} onClick={() => void cancel(b.id, b.version)}>تحرير حجز التعويض</button></div>)}
    </>}
    {review && <section className="placement-preview" aria-label="معاينة التعويض"><h3>{review.mode === "grant" ? "مراجعة الاستحقاق" : "مراجعة الموعد"}</h3><p>الوحدات: {review.data.units}{review.mode === "book" && <> · المتوقع {review.data.expectedPlayers}/{review.data.capacity ?? "—"}</>}</p>{review.data.conflicts.map(c => <p role="alert" key={c}>{c}</p>)}<button disabled={busy || !!review.data.conflicts.length} onClick={() => void confirm()}>{review.mode === "grant" ? "تأكيد استحقاق التعويض" : "تأكيد حجز التعويض"}</button></section>}
    {message && <p role="status">{message}</p>}
  </div>}</details>;
}
