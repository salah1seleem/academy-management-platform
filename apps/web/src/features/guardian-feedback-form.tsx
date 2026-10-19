"use client";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { GuardianShell } from "../components/guardian-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { feedbackSubjects, feedbackUrgencies } from "./feedback-triage";
import { feedbackStates } from "./feedback-state";
import styles from "./player-skills.module.css";
import { FeedbackIncident, FeedbackIncidentPicker } from "./feedback-incident";
type Options = { asOfDate: string; groups: { id: string; name: string; coaches: { id: string; name: string }[] }[]; mine: { id: string; kind: string; message: string; status: string; ownerResponse: string | null; reportedSubject?: string | null; reportedUrgency?: string | null }[] };
export function GuardianFeedbackForm({ playerId }: { playerId: string }) {
  const [data, setData] = useState<Options>(), [error, setError] = useState(""), [success, setSuccess] = useState(""), [revision, setRevision] = useState(0);
  const [group, setGroup] = useState(""), [coach, setCoach] = useState(""), [kind, setKind] = useState("Complaint"), [subject, setSubject] = useState("Other"), [urgency, setUrgency] = useState("Routine"), [incident, setIncident] = useState(""), [message, setMessage] = useState(""), [score, setScore] = useState(5), [busy, setBusy] = useState(false);
  const attempt = useRef<{ signature: string; key: string } | null>(null);
  const [linkedSession, setLinkedSession] = useState<FeedbackIncident | null>(null);
  function changeSession(s: FeedbackIncident | null) { setLinkedSession(s); setCoach(""); if (s) setGroup(s.groupId); else if (linkedSession) setGroup(""); }
  useEffect(() => {
    let controller: AbortController;
    const load = () => { controller?.abort(); controller = new AbortController(); const signal = controller.signal; setData(undefined); setLinkedSession(null); setCoach(""); setGroup(""); if (document.hidden) return; fetch(`/api/v1/guardian/children/${playerId}/feedback-options`, { cache: "no-store", signal }).then(async r => { if (!r.ok) throw new Error(); return r.json(); }).then(d => { if (!signal.aborted) { setData(d); setError(""); } }).catch(() => { if (!signal.aborted) setError("الملاحظات غير متاحة لهذا الطفل ضمن ارتباطك الحالي."); }); };
    load(); document.addEventListener("visibilitychange", load); window.addEventListener("focus", load); return () => { controller?.abort(); document.removeEventListener("visibilitychange", load); window.removeEventListener("focus", load); };
  }, [playerId, revision]);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (busy) return; const body = { trainingGroupId: group, coachMembershipId: coach || null, kind, score: kind === "Rating" ? score : null, message: message.trim(), ...(kind !== "Rating" ? { subject, urgency, incidentOn: incident || null, incidentSessionId: linkedSession?.id ?? null } : {}) }, signature = JSON.stringify(body);
    if (attempt.current?.signature !== signature) attempt.current = { signature, key: crypto.randomUUID() };
    setBusy(true); setError(""); setSuccess(""); try { const response = await csrfRequest(`/api/v1/guardian/children/${playerId}/feedback`, "POST", body, { "Idempotency-Key": attempt.current.key }, { announce: false }); const saved = await response.json(); if (!saved.id) throw new Error(); setSuccess(urgency === "Safety" && kind !== "Rating" ? "وصل بلاغ السلامة إلى قائمة المراجعة البشرية للمالك. هذا ليس تأكيد استجابة فورية." : "وصلت ملاحظتك لإدارة الأكاديمية."); setMessage(""); attempt.current = null; setRevision(x => x + 1); } catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر إرسال الملاحظة؛ حاول مرة أخرى."); } finally { setBusy(false); }
  }
  return <GuardianShell><div className={styles.family}><Link className="back-link" href={`/guardian/children/${playerId}`}>العودة لملف الطفل</Link><header className="guardian-page-header"><h1>ملاحظتك لإدارة الأكاديمية</h1><p>تقييم المدرب مرة شهريًا لكل طفل. الشكاوى والاقتراحات خاصة بإدارة الأكاديمية، ولا تظهر للمدرب موضوع الملاحظة.</p></header>{success && <p className="section-card" role="status">{success}</p>}{error && <p className="section-card" role="alert">{error}</p>}{data && <><form className="guardian-form" onSubmit={submit}>
    <label>المجموعة<select required disabled={busy || !!linkedSession} value={group} onChange={e => { setGroup(e.target.value); setCoach(""); }}><option value="">اختر مجموعة الطفل</option>{linkedSession && !data.groups.some(g=>g.id===linkedSession.groupId) && <option value={linkedSession.groupId}>{linkedSession.groupName} — وقت الواقعة</option>}{data.groups.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}</select></label>
    <label>نوع الملاحظة<select value={kind} disabled={busy} onChange={e => { setKind(e.target.value); setCoach(""); changeSession(null); }}><option value="Complaint">شكوى</option><option value="Suggestion">اقتراح</option><option value="Request">طلب للإدارة</option><option value="Rating">تقييم المدرب</option></select></label>
    {kind === "Request" && <p role="note">اكتب ما تحتاجه من الإدارة لهذا الطفل. الرد أو حل الطلب لا ينفّذ دفعًا أو تجديدًا أو نقل مجموعة أو تغيير حضور؛ لكل إجراء مساره وصلاحيته.</p>}
    <label>{kind === "Rating" ? "المدرب" : "المدرب المعني (اختياري)"}<select required={kind === "Rating"} disabled={busy} value={coach} onChange={e => setCoach(e.target.value)}><option value="">{kind === "Rating" ? "اختر المدرب" : "لا أعرف / ملاحظة عامة"}</option>{(linkedSession?.coaches ?? data.groups.find(g => g.id === group)?.coaches)?.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
    {kind === "Rating" ? <label>التقييم<select value={score} disabled={busy} onChange={e => setScore(Number(e.target.value))}>{[1, 2, 3, 4, 5].map(n => <option key={n} value={n}>{n} من 5</option>)}</select></label> : <>
      <label>موضوع الملاحظة<select value={subject} disabled={busy} onChange={e => setSubject(e.target.value)}>{Object.entries(feedbackSubjects).map(([v, label]) => <option key={v} value={v}>{label}</option>)}</select></label>
      <label>درجة الاستعجال<select value={urgency} disabled={busy} onChange={e => setUrgency(e.target.value)}>{Object.entries(feedbackUrgencies).map(([v, label]) => <option key={v} value={v}>{label}</option>)}</select></label>
      <label>تاريخ الواقعة (اختياري)<input type="date" max={data.asOfDate} value={incident} disabled={busy} onChange={e => { setIncident(e.target.value); changeSession(null); }}/></label>
      {incident && <fieldset disabled={busy}><FeedbackIncidentPicker key={incident} playerId={playerId} day={incident} value={linkedSession} changed={changeSession}/></fieldset>}
      <small>قائمة المدربين للإسناد الحالي، وليست إثباتًا لمدرب واقعة قديمة. يمكنك اختيار «لا أعرف» لتراجع الإدارة الواقعة.</small>
      {urgency === "Safety" && <p role="note">بلاغ السلامة يحتاج مراجعة بشرية عاجلة. هذه الخدمة ليست طوارئ أو مراقبة لحظية؛ عند خطر فوري تواصل مباشرة مع خدمات الطوارئ المحلية أو مسؤول الأكاديمية.</p>}
    </>}
    <label>{kind === "Rating" ? "تعليق (اختياري)" : "اكتب الملاحظة"}<textarea required={kind !== "Rating"} minLength={kind === "Rating" ? undefined : 5} maxLength={500} disabled={busy} value={message} onChange={e => setMessage(e.target.value)}/></label><button className="primary-button" disabled={busy || !data.groups.length && !linkedSession}>إرسال للإدارة</button>
  </form><section className="section-card"><h2>ملاحظاتي السابقة</h2>{data.mine.map(i => <article className="profile-section" key={i.id}><p>{i.message}</p><p>{feedbackStates[i.status]}</p>{i.reportedSubject && <small>{feedbackSubjects[i.reportedSubject]} · {feedbackUrgencies[i.reportedUrgency ?? ""]}</small>}{i.ownerResponse && <p>رد الإدارة: {i.ownerResponse}</p>}</article>)}</section></>}</div></GuardianShell>;
}
