"use client";
import { useRef, useState } from "react";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./feedback-case.module.css";
export const feedbackSubjects: Record<string, string> = { Coach: "المدرب", Schedule: "المواعيد", Administration: "الإدارة", Facility: "المرفق", Water: "المياه", Payment: "الدفع", Other: "موضوع آخر" };
export const feedbackUrgencies: Record<string, string> = { Routine: "عادية", Urgent: "عاجلة", Safety: "سلامة أو إساءة — مراجعة بشرية عاجلة" };
export function FeedbackTriage({ id, version, subject, urgency, safety, saved }: { id: string; version: number; subject?: string | null; urgency?: string | null; safety?: boolean; saved: () => void }) {
  const [topic, setTopic] = useState(subject ?? "Other"), [priority, setPriority] = useState(urgency ?? "Routine"), [note, setNote] = useState(""), [busy, setBusy] = useState(false), [error, setError] = useState("");
  const attempt = useRef<{ signature: string; requestId: string } | null>(null);
  async function submit(e: React.FormEvent) { e.preventDefault(); if (busy) return; const body = { subject: topic, urgency: priority, version, note: note.trim() || null }, signature = JSON.stringify(body); if (attempt.current?.signature !== signature) attempt.current = { signature, requestId: crypto.randomUUID() }; setBusy(true); setError(""); try { await csrfRequest(`/api/v1/manage/feedback/${id}/triage`, "POST", { ...body, requestId: attempt.current.requestId }); saved(); } catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر حفظ الفرز البشري."); } finally { setBusy(false); } }
  return <form className={styles.form} onSubmit={submit}><h2>الفرز البشري</h2><p>تصنيف يراجعه المالك، وليس حكمًا على شخص أو تقييمًا آليًا. لا يغيّر الحضور أو الراتب أو يرسل ردًا للأسرة.</p>{safety && <p role="note">أُبلغ عن سلامة أو إساءة. تبقى في قائمة السلامة حتى لو غُير تصنيفها، إلى أن تُحل برد صريح بعد الفرز البشري.</p>}
    <label>موضوع المراجعة<select value={topic} disabled={busy} onChange={e => setTopic(e.target.value)}>{Object.entries(feedbackSubjects).map(([v, label]) => <option key={v} value={v}>{label}</option>)}</select></label>
    <label>درجة الاستعجال المراجعة<select value={priority} disabled={busy} onChange={e => setPriority(e.target.value)}>{Object.entries(feedbackUrgencies).map(([v, label]) => <option key={v} value={v}>{label}</option>)}</select></label>
    <label>ملاحظة فرز داخلية (اختياري)<textarea maxLength={500} value={note} disabled={busy} onChange={e => setNote(e.target.value)}/></label><button className="button button-primary" disabled={busy}>تأكيد الفرز البشري</button>{error && <p role="alert">{error}</p>}
  </form>;
}
