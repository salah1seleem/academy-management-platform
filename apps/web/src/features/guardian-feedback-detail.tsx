"use client";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { Button, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { feedbackStates, feedbackTransitions } from "./feedback-state";
import styles from "./feedback-case.module.css";
import { FeedbackAssignment } from "./feedback-investigations";
import { FeedbackTriage, feedbackSubjects, feedbackUrgencies } from "./feedback-triage";
import { FeedbackIncidentEvidence } from "./feedback-incident";
import { FeedbackDeadlineEvidence } from "./feedback-deadline-policy";

export type FeedbackDetail = { id: string; kind: string; score: number | null; message: string; status: string; dueOn: string | null; overdue: boolean;
  followUpPolicyEvidence?: string | null;
  subject?: string | null; urgency?: string | null; reportedSubject?: string | null; reportedUrgency?: string | null; incidentOn?: string | null; triagedAtUtc?: string | null; safety?: boolean; incidentSessionEvidence?: string | null;
  investigatorMembershipId?: string | null; investigationBrief?: string | null; investigatorName?: string | null;
  ownerResponse: string | null; createdAtUtc: string; reviewedAtUtc: string | null; respondedAtUtc: string | null; playerId: string; playerName: string | null;
  trainingGroupId: string; groupName: string | null; branchName: string | null; coachName: string | null;
  version?: number; timeZone?: string; historyHasMore?: boolean; history?: { beforeSubject?: string | null; afterSubject?: string | null; beforeUrgency?: string | null; afterUrgency?: string | null; id: string; action: string; fromStatus: string; toStatus: string; beforeDueOn: string | null; afterDueOn: string | null; internalNote: string | null; publicResponse: string | null; actorName: string | null; investigatorName?: string | null; createdAtUtc: string }[] };
const kinds: Record<string, string> = { Rating: "تقييم مدرب", Complaint: "شكوى", Suggestion: "اقتراح" };
export function GuardianFeedbackDetail({ id }: { id: string }) {
  const [item, setItem] = useState<FeedbackDetail | null>(null), [error, setError] = useState(""), [message, setMessage] = useState("");
  const [reply, setReply] = useState(""), [due, setDue] = useState(""), [busy, setBusy] = useState(false), [revision, setRevision] = useState(0);
  const [target, setTarget] = useState(""), [note, setNote] = useState("");
  const attempt = useRef<{ signature: string; requestId: string } | null>(null);
  useEffect(() => {
    let controller: AbortController | null = null;
    const load = () => {
      controller?.abort(); controller = new AbortController(); const signal = controller.signal;
      setItem(null); setError("");
      fetch(`/api/v1/manage/feedback/${id}`, { signal, cache: "no-store" }).then(async r => {
        if (!r.ok) throw new Error("الملاحظة غير متاحة أو ليست لديك صلاحية قراءتها."); return r.json() as Promise<FeedbackDetail>;
      }).then(x => { if (!signal.aborted) setItem(x); }).catch(() => { if (!signal.aborted) setError("الملاحظة غير متاحة أو ليست لديك صلاحية قراءتها."); });
    };
    const visibility = () => { if (document.hidden) { controller?.abort(); setItem(null); setReply(""); setNote(""); setTarget(""); attempt.current = null; } else load(); };
    load(); window.addEventListener("focus", load); document.addEventListener("visibilitychange", visibility);
    return () => { controller?.abort(); window.removeEventListener("focus", load); document.removeEventListener("visibilitychange", visibility); };
  }, [id, revision]);
  async function save(action: "review" | "due" | "resolve" | "transition") {
    if (busy || !item) return;
    setBusy(true); setError(""); setMessage("");
    try {
      let body: object = action === "resolve" ? { response: reply.trim() } : action === "due" ? { dueOn: due } : {};
      if (action === "transition") {
        const signature = JSON.stringify([id, item.version, target, note.trim()]);
        if (attempt.current?.signature !== signature) attempt.current = { signature, requestId: crypto.randomUUID() };
        body = { status: target, version: item.version, internalNote: note.trim() || null, requestId: attempt.current.requestId };
      }
      await csrfRequest(`/api/v1/manage/feedback/${id}/${action}`, "POST", body);
      setReply(""); setDue(""); setNote(""); setTarget(""); attempt.current = null;
      setMessage(action === "resolve" ? "تم حفظ الرد وحل الملاحظة. يظهر الرد للأسرة المرتبطة داخل التطبيق." : "تم حفظ تحديث الملاحظة."); setRevision(n => n + 1);
    } catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر الحفظ؛ لم نسجل نجاحًا."); }
    finally { setBusy(false); }
  }
  return <><PageHeader title="متابعة ملاحظة الأسرة" context="ملاحظات أولياء الأمور"/><Link className="button button-secondary" href="/dashboard/feedback">العودة للملاحظات</Link>
    {error && <ErrorState message={error}/>} {message && <p role="status">{message}</p>}
    {!item ? !error && <LoadingState/> : <>
      <SectionCard title={kinds[item.kind] ?? "ملاحظة"}>
        <p>{item.playerName ?? "اللاعب غير متاح"} · {item.groupName} · {item.branchName}</p><p>المدرب المذكور: {item.coachName ?? "لم يحدد"}</p>
        <p>{item.score ? `${item.score}/5 · ` : ""}{item.message || "لا يوجد نص إضافي"}</p>
        <FeedbackIncidentEvidence evidence={item.incidentSessionEvidence}/>
        {item.kind !== "Rating" && <><p>عند التقديم: {feedbackSubjects[item.reportedSubject ?? ""] ?? "لم يصنف وقتها"} · {feedbackUrgencies[item.reportedUrgency ?? ""] ?? "لم يحدد"} · الواقعة: <bdi>{item.incidentOn ?? "غير محددة"}</bdi></p><p>مراجعة الإدارة: {feedbackSubjects[item.subject ?? ""] ?? "لم تصنف بعد"} · {feedbackUrgencies[item.urgency ?? ""] ?? "لم تحدد"}</p>{item.safety && <p role="note">بلاغ سلامة يحتاج متابعة بشرية، وليس إثبات خطأ أو حكمًا آليًا.{!item.triagedAtUtc && " لم يؤكد الفرز البشري بعد."}</p>}</>}
        <p>الحالة: <strong>{feedbackStates[item.status] ?? "غير متاحة"}</strong></p>
        <p>موعد المتابعة: <bdi>{item.dueOn ?? "لم يحدد"}</bdi>{item.overdue ? " · تجاوزت الموعد المحدد" : ""}</p>
        <FeedbackDeadlineEvidence evidence={item.followUpPolicyEvidence}/>
        <p><Link href={`/dashboard/feedback?groupId=${item.trainingGroupId}`}>ملاحظات المجموعة نفسها</Link> · <Link href={`/dashboard/players/${item.playerId}`}>ملف اللاعب نفسه</Link></p>
        {item.ownerResponse && <blockquote>رد الإدارة: {item.ownerResponse}</blockquote>}
      </SectionCard>
      {item.status !== "Resolved" && item.status !== "Closed" && <SectionCard title="إجراء على الملاحظة نفسها">
        {item.status === "Open" && <Button variant="secondary" disabled={busy} onClick={() => void save("review")}>تسجيل المراجعة</Button>}
        {item.kind !== "Rating" && !item.dueOn && <form onSubmit={e => { e.preventDefault(); void save("due"); }} className="feedback-reply">
          <label>موعد متابعة الملاحظة<input type="date" required value={due} disabled={busy} onChange={e => setDue(e.target.value)}/></label><Button type="submit" disabled={busy}>حفظ الموعد</Button>
        </form>}
        {item.kind !== "Rating" && <form onSubmit={e => { e.preventDefault(); void save("resolve"); }} className="feedback-reply">
          <label>الرد على الملاحظة<textarea required minLength={5} maxLength={500} value={reply} disabled={busy} onChange={e => setReply(e.target.value)}/></label>
          <p>يرسل إشعارًا داخليًا عامًا للأسرة المرتبطة فقط؛ ليس رسالة خارجية. لا يُغلق أي متابعة تشغيلية بدلًا عنها.</p>
          <Button type="submit" disabled={busy || item.safety && !item.triagedAtUtc}>إرسال الرد وحل الملاحظة</Button>
        </form>}
      </SectionCard>}
      {item.kind !== "Rating" && !!item.version && item.status !== "Resolved" && item.status !== "Closed" && <SectionCard title="تصنيف الملاحظة والاستعجال"><FeedbackTriage key={`triage:${item.version}`} id={id} version={item.version} subject={item.subject} urgency={item.urgency} safety={item.safety} saved={() => { setMessage("تم تأكيد الفرز البشري وحفظ تصنيف الإدارة دون تغيير بلاغ الأسرة الأصلي."); setRevision(n => n + 1); }}/></SectionCard>}
      {item.kind !== "Rating" && !!item.version && <SectionCard title="مسار المعالجة الخاص">
        <p className={styles.hint}>هذا السجل للمالك فقط. الملاحظة الداخلية اختيارية ولا تُرسل للأسرة أو المدرب. استخدم الرد أعلاه لإبلاغ الأسرة بالحل.</p>
        <form className={styles.form} onSubmit={e => { e.preventDefault(); void save("transition"); }}>
          <label>الخطوة التالية<select required value={target} disabled={busy} onChange={e => setTarget(e.target.value)}><option value="">اختر الإجراء</option>{(feedbackTransitions[item.status] ?? []).map(s => <option key={s} value={s}>{feedbackStates[s]}</option>)}</select></label>
          <label>ملاحظة داخلية (اختياري)<textarea value={note} disabled={busy} maxLength={500} rows={3} onChange={e => setNote(e.target.value)}/></label>
          <Button type="submit" disabled={busy || !target}>حفظ مرحلة المعالجة</Button>
        </form>
        {item.status === "Reopened" && item.ownerResponse && <p>الرد السابق محفوظ. إعادة الفتح تعيد الملاحظة للمتابعة ولا ترسل إشعارًا تلقائيًا.</p>}
      </SectionCard>}
      {item.kind !== "Rating" && !!item.version && item.status !== "Resolved" && item.status !== "Closed" && <SectionCard title="مسؤول التحقيق">
        <p>المسؤول الحالي: {item.investigatorName ?? "لم يسند"}</p>
        <FeedbackAssignment key={`${id}:${item.version}`} id={id} version={item.version} current={item.investigatorMembershipId} brief={item.investigationBrief} saved={() => { setMessage("تم حفظ تكليف التحقيق؛ لم يُرسل شيء للأسرة."); setRevision(n => n + 1); }}/>
      </SectionCard>}
      <SectionCard title="سجل الملاحظة الخاص">
        <p className={styles.hint}>التاريخ السابق لهذه الإضافة لا يُعاد اختلاقه. الأحدث أولًا، حتى 50 تحديثًا.</p>
        {item.historyHasMore && <p>توجد تحديثات أقدم غير معروضة؛ لم تُحذف.</p>}
        {!item.history?.length ? <p>لم تُسجل تحديثات بعد.</p> : <ol className={styles.history}>{item.history.map(c => <li key={c.id}>
          <div className={styles.historyHead}><strong>{c.action === "Triage" ? "فرز بشري" : c.action === "Assign" ? `تكليف تحقيق: ${c.investigatorName ?? "إلغاء الإسناد"}` : c.action === "Findings" ? "نتيجة تحقيق خاصة" : c.action === "Due" ? "تحديد موعد متابعة" : `${feedbackStates[c.fromStatus] ?? c.fromStatus} ← ${feedbackStates[c.toStatus] ?? c.toStatus}`}</strong><span>{c.actorName ?? "مسؤول الأكاديمية"}</span></div>
          <time dateTime={c.createdAtUtc}>{new Intl.DateTimeFormat("ar-EG-u-nu-latn", { dateStyle: "medium", timeStyle: "short", timeZone: item.timeZone ?? "UTC" }).format(new Date(c.createdAtUtc))} · توقيت الأكاديمية</time>
          {c.action === "Due" && <p>الموعد: <bdi>{c.afterDueOn}</bdi></p>}
          {c.internalNote && <p>ملاحظة داخلية: {c.internalNote}</p>}{c.publicResponse && <blockquote>{c.action === "Transition" && c.toStatus === "Reopened" ? "الرد السابق المحفوظ عند إعادة الفتح" : "الرد المحفوظ للأسرة"}: {c.publicResponse}</blockquote>}
          {c.action === "Triage" && <p>{feedbackSubjects[c.beforeSubject ?? ""] ?? "غير مصنف"} · {feedbackUrgencies[c.beforeUrgency ?? ""] ?? "غير محددة"} ← {feedbackSubjects[c.afterSubject ?? ""] ?? "غير مصنف"} · {feedbackUrgencies[c.afterUrgency ?? ""] ?? "غير محددة"}. هذا فرز بشري وليس ردًا للأسرة.</p>}
        </li>)}</ol>}
      </SectionCard>
    </>}
  </>;
}
