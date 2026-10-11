"use client";
import { useEffect, useState } from "react";
import { CalendarClock, ArrowLeftRight } from "lucide-react";
import { formatDateAr } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";
type Group = { id: string; arabicName: string };
type Slot = { id: string; groupId: string; group: string; date: string; start: string; end: string };
type Preview = { fingerprint: string; conflicts: string[]; effect: string; changes: { source: Slot; target: Slot; expectedPlayers: number; capacity: number | null }[] };
const when = (s: Slot) => `${formatDateAr(s.date)} · ${s.start.slice(0, 5)} — ${s.end.slice(0, 5)}`;
export function PlayerSessionChange({ enrollmentId, groups, onSaved }: { enrollmentId: string; groups: Group[]; onSaved?: () => void }) {
  const [open, setOpen] = useState(false), [target, setTarget] = useState(""), [options, setOptions] = useState<{ original: Slot[]; alternative: Slot[] }>(), [selection, setSelection] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false), [loading, setLoading] = useState(false), [message, setMessage] = useState(""), [preview, setPreview] = useState<Preview>(), [key, setKey] = useState("");
  useEffect(() => {
    if (!open) return;
    let active = true;
    fetch(`/api/v1/people/enrollments/${enrollmentId}/session-transfer/options${target ? `?targetGroup=${encodeURIComponent(target)}` : ""}`).then(r => { if (!r.ok) throw new Error(); return r.json(); }).then(data => { if (active) setOptions(data); }).catch(() => { if (active) setMessage("تعذر تحميل الحصص المتاحة. حاول مرة أخرى."); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [open, enrollmentId, target]);
  function invalidate() { setPreview(undefined); setKey(""); setMessage(""); }
  const pairs = Object.entries(selection).map(([sourceSessionId, targetSessionId]) => ({ sourceSessionId, targetSessionId }));
  async function review() {
    setBusy(true); invalidate();
    try { const r = await csrfRequest(`/api/v1/people/enrollments/${enrollmentId}/session-transfer/preview`, "POST", { pairs }); setPreview(await r.json() as Preview); setKey(crypto.randomUUID()); }
    catch (e) { setMessage(e instanceof Error ? e.message : "تعذر معاينة الحصص."); } finally { setBusy(false); }
  }
  async function save() {
    if (!preview || preview.conflicts.length) return;
    setBusy(true); setMessage("");
    try { await csrfRequest(`/api/v1/people/enrollments/${enrollmentId}/session-transfer`, "POST", { pairs, fingerprint: preview.fingerprint }, { "Idempotency-Key": key }); setMessage("تم نقل الحصص المختارة وتحديث مواعيد ولي الأمر."); setSelection({}); setPreview(undefined); onSaved?.(); }
    catch (e) { setPreview(undefined); setMessage(e instanceof Error ? e.message : "تعذر الاعتماد؛ أعد المعاينة."); } finally { setBusy(false); }
  }
  return <details className="placement-level" onToggle={e => { const next = e.currentTarget.open; setOpen(next); if (next) { setLoading(true); setOptions(undefined); setMessage(""); } }}><summary><CalendarClock size={17} aria-hidden="true" /> نقل حصص مختارة</summary>{open && <div className="form-shell">
    <p className="muted">حصة واحدة أو أكثر دون تغيير المجموعة الأساسية. اختر المجموعة البديلة، ثم الحصة الأصلية وبديلها؛ لا يُحفظ شيء قبل المعاينة والتأكيد.</p>
    <label>مجموعة الحصة البديلة<select aria-label="مجموعة الحصة البديلة" value={target} disabled={busy} onChange={e => { setTarget(e.target.value); setSelection({}); setLoading(true); setOptions(undefined); invalidate(); }}><option value="">اختر المجموعة</option>{groups.map(g => <option key={g.id} value={g.id}>{g.arabicName}</option>)}</select></label>
    {loading && <p role="status">جارٍ تحميل الحصص…</p>}
    {options && <><p className="muted">أقرب100 موعد متاح؛ الحصص السابقة أو المنقولة مسبقًا غير قابلة للاختيار.</p>
      {!options.original.length && <p>لا توجد حصص قابلة للنقل لهذا التسجيل.</p>}
      {target && !options.alternative.length && <p role="status">لا توجد حصص بديلة متاحة في المجموعة المختارة.</p>}
      <div className="session-move-list">{options.original.map(s => <div className="session-move-row" key={s.id}>
        <label className="session-move-select"><input type="checkbox" checked={Object.hasOwn(selection, s.id)} disabled={busy || !target || !options.alternative.length} onChange={e => { setSelection(v => { const next = { ...v }; if (e.target.checked) next[s.id] = ""; else delete next[s.id]; return next; }); invalidate(); }} /><span><strong>{s.group}</strong><small>{when(s)}</small></span></label>
        {Object.hasOwn(selection, s.id) && <label>بديل {when(s)}<select aria-label={`بديل الحصة ${s.id}`} value={selection[s.id]} disabled={busy} onChange={e => { setSelection(v => ({ ...v, [s.id]: e.target.value })); invalidate(); }}><option value="">اختر موعدًا بديلًا</option>{options.alternative.map(a => <option value={a.id} key={a.id} disabled={Object.entries(selection).some(([id, value]) => id !== s.id && value === a.id)}>{when(a)}</option>)}</select></label>}
      </div>)}</div></>}
    <div className="form-actions"><button className="secondary-button" disabled={busy || loading || !pairs.length || pairs.some(p => !p.targetSessionId)} onClick={() => void review()}>معاينة نقل الحصص</button></div>
    {preview && <section className="placement-preview" aria-label="معاينة نقل الحصص"><h3>مراجعة {preview.changes.length} حصة</h3><p>{preview.effect}</p>{preview.changes.map(c => <div className="session-move-impact" key={c.source.id}><span>{c.source.group}<small>{when(c.source)}</small></span><ArrowLeftRight size={18} aria-hidden="true" /><span>{c.target.group}<small>{when(c.target)} · {c.expectedPlayers}/{c.capacity ?? "—"} مقعد</small></span></div>)}{preview.conflicts.map(c => <p role="alert" key={c}>{c}</p>)}<button disabled={busy || preview.conflicts.length > 0} onClick={() => void save()}>تأكيد نقل الحصص</button></section>}
    {message && <p role="status">{message}</p>}
  </div>}</details>;
}
