"use client";
import { useEffect, useState } from "react";
import { monthToDate } from "../lib/date-range";
import { csrfRequest } from "./dashboard-api";
import { PlayerSessionChange } from "./player-session-change";
type Group = { id: string; arabicName: string; sportId: string; branchId: string; registrationFormat?: string };
type Slot = { id: string; sessionDate: string; startTime: string; endTime: string };
type Preview = { fingerprint: string; effectiveDate: string; sourceGroup: string; targetGroup: string; conflicts: string[]; removedSessions: Slot[]; addedSessions: Slot[]; effect: string };
export function PlayerGroupChange({ enrollmentId, groupId, onSaved }: { enrollmentId: string; groupId: string; onSaved?: () => void }) {
  const [groups, setGroups] = useState<Group[]>([]), [selected, setSelected] = useState(groupId), [date, setDate] = useState(() => monthToDate().to);
  const [message, setMessage] = useState(""), [busy, setBusy] = useState(false), [preview, setPreview] = useState<Preview>(), [key, setKey] = useState("");
  useEffect(() => {
    let active = true;
    fetch("/api/v1/manage/structure/options").then(r => { if (!r.ok) throw new Error(); return r.json(); }).then((o: { groups: Group[] }) => {
      const original = o.groups.find(g => g.id === groupId);
      if (active) setGroups(o.groups.filter(g => original && g.sportId === original.sportId && g.branchId === original.branchId && (g.registrationFormat ?? "Group") === (original.registrationFormat ?? "Group")));
    }).catch(() => { if (active) setMessage("تعذر تحميل المجموعات المسموح بها."); });
    return () => { active = false; };
  }, [groupId]);
  function invalidate() { setPreview(undefined); setMessage(""); setKey(""); }
  async function review() {
    setBusy(true); setMessage(""); setPreview(undefined);
    try { const r = await csrfRequest(`/api/v1/people/enrollments/${enrollmentId}/group-transfer/preview`, "POST", { groupId: selected, effectiveDate: date }); setPreview(await r.json() as Preview); setKey(crypto.randomUUID()); }
    catch (c) { setMessage(c instanceof Error ? c.message : "تعذر معاينة النقل."); } finally { setBusy(false); }
  }
  async function move() {
    if (!preview || preview.conflicts.length) return;
    setBusy(true); setMessage("");
    try { await csrfRequest(`/api/v1/people/enrollments/${enrollmentId}/group-transfer`, "POST", { groupId: selected, effectiveDate: date, fingerprint: preview.fingerprint }, { "Idempotency-Key": key }); setMessage("تم حفظ نقل المجموعة حسب التاريخ المحدد."); if (onSaved) onSaved(); else window.location.reload(); }
    catch (c) { setPreview(undefined); setMessage(c instanceof Error ? c.message : "تعذر حفظ النقل؛ أعد المعاينة."); } finally { setBusy(false); }
  }
  return <><details className="placement-level"><summary>تعديل المجموعة</summary><div className="form-shell">
    <div className="form-grid"><label>المجموعة الجديدة<select aria-label="تعديل المجموعة" disabled={busy} value={selected} onChange={e => { setSelected(e.target.value); invalidate(); }}>{groups.map(g => <option value={g.id} key={g.id}>{g.arabicName}</option>)}</select></label>
      <label>بدء النقل<input aria-label="بدء النقل" type="date" min={monthToDate().to} value={date} disabled={busy} onChange={e => { setDate(e.target.value); invalidate(); }} /></label></div>
    <p className="muted">كل الحصص من بداية اليوم المحدد. يبقى الماضي محفوظًا، ولا ينشئ النقل تحصيلًا أو حضورًا أو يخصم رصيدًا.</p>
    <div className="form-actions"><button className="secondary-button" disabled={busy || !date || selected === groupId} onClick={() => void review()}>معاينة نقل المجموعة</button></div>
    {preview && <section className="placement-preview" aria-label="معاينة النقل"><h3>{preview.sourceGroup} ← {preview.targetGroup}</h3><p>من {preview.effectiveDate} · {preview.removedSessions.length} موعد سابق يستبدل بـ {preview.addedSessions.length} موعد جديد</p><p className="muted">{preview.effect}</p>
      {preview.conflicts.map(c => <p role="alert" key={c}>{c}</p>)}
      <details><summary>المواعيد الجديدة</summary>{preview.addedSessions.map(s => <p key={s.id}>{s.sessionDate} · {s.startTime.slice(0, 5)} — {s.endTime.slice(0, 5)}</p>)}</details>
      <button disabled={busy || preview.conflicts.length > 0} onClick={() => void move()}>تأكيد نقل المجموعة</button></section>}
    {message && <p role="status">{message}</p>}
  </div></details><PlayerSessionChange enrollmentId={enrollmentId} groups={groups} onSaved={onSaved} /></>;
}
