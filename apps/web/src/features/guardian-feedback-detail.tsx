"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { Button, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

export type FeedbackDetail = { id: string; kind: string; score: number | null; message: string; status: string; dueOn: string | null; overdue: boolean;
  ownerResponse: string | null; createdAtUtc: string; reviewedAtUtc: string | null; respondedAtUtc: string | null; playerId: string; playerName: string | null;
  trainingGroupId: string; groupName: string | null; branchName: string | null; coachName: string | null };
const kinds: Record<string, string> = { Rating: "تقييم مدرب", Complaint: "شكوى", Suggestion: "اقتراح" };
export function GuardianFeedbackDetail({ id }: { id: string }) {
  const [item, setItem] = useState<FeedbackDetail | null>(null), [error, setError] = useState(""), [message, setMessage] = useState("");
  const [reply, setReply] = useState(""), [due, setDue] = useState(""), [busy, setBusy] = useState(false), [revision, setRevision] = useState(0);
  useEffect(() => {
    let controller: AbortController | null = null;
    const load = () => {
      controller?.abort(); controller = new AbortController(); const signal = controller.signal;
      setItem(null); setError("");
      fetch(`/api/v1/manage/feedback/${id}`, { signal, cache: "no-store" }).then(async r => {
        if (!r.ok) throw new Error("الملاحظة غير متاحة أو ليست لديك صلاحية قراءتها."); return r.json() as Promise<FeedbackDetail>;
      }).then(x => { if (!signal.aborted) setItem(x); }).catch(() => { if (!signal.aborted) setError("الملاحظة غير متاحة أو ليست لديك صلاحية قراءتها."); });
    };
    const visibility = () => { if (document.hidden) { controller?.abort(); setItem(null); setReply(""); } else load(); };
    load(); window.addEventListener("focus", load); document.addEventListener("visibilitychange", visibility);
    return () => { controller?.abort(); window.removeEventListener("focus", load); document.removeEventListener("visibilitychange", visibility); };
  }, [id, revision]);
  async function save(action: "review" | "due" | "resolve") {
    if (busy || !item) return;
    setBusy(true); setError(""); setMessage("");
    try {
      await csrfRequest(`/api/v1/manage/feedback/${id}/${action}`, "POST", action === "resolve" ? { response: reply.trim() } : action === "due" ? { dueOn: due } : {});
      setReply(""); setDue(""); setMessage(action === "resolve" ? "تم حفظ الرد وحل الملاحظة. يظهر الرد للأسرة المرتبطة داخل التطبيق." : "تم حفظ تحديث الملاحظة."); setRevision(n => n + 1);
    } catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر الحفظ؛ لم نسجل نجاحًا."); }
    finally { setBusy(false); }
  }
  return <><PageHeader title="متابعة ملاحظة الأسرة" context="ملاحظات أولياء الأمور"/><Link className="button button-secondary" href="/dashboard/feedback">العودة للملاحظات</Link>
    {error && <ErrorState message={error}/>} {message && <p role="status">{message}</p>}
    {!item ? !error && <LoadingState/> : <>
      <SectionCard title={kinds[item.kind] ?? "ملاحظة"}>
        <p>{item.playerName ?? "اللاعب غير متاح"} · {item.groupName} · {item.branchName}</p><p>المدرب المذكور: {item.coachName ?? "لم يحدد"}</p>
        <p>{item.score ? `${item.score}/5 · ` : ""}{item.message || "لا يوجد نص إضافي"}</p>
        <p>الحالة: {item.status === "Resolved" ? "تم الرد والحل" : item.status === "Reviewed" ? "تمت المراجعة" : "تنتظر المراجعة"}</p>
        <p>موعد المتابعة: <bdi>{item.dueOn ?? "لم يحدد"}</bdi>{item.overdue ? " · تجاوزت الموعد المحدد" : ""}</p>
        <small>هذا موعد حدده المالك، وليس وعد استجابة تلقائيًا. لا تُعرض هوية مقدم الملاحظة للمدرب موضوعها.</small>
        <p><Link href={`/dashboard/feedback?groupId=${item.trainingGroupId}`}>ملاحظات المجموعة نفسها</Link> · <Link href={`/dashboard/players/${item.playerId}`}>ملف اللاعب نفسه</Link></p>
        {item.ownerResponse && <blockquote>رد الإدارة: {item.ownerResponse}</blockquote>}
      </SectionCard>
      {item.status !== "Resolved" && <SectionCard title="إجراء على الملاحظة نفسها">
        {item.status === "Open" && <Button variant="secondary" disabled={busy} onClick={() => void save("review")}>تسجيل المراجعة</Button>}
        {item.kind !== "Rating" && !item.dueOn && <form onSubmit={e => { e.preventDefault(); void save("due"); }} className="feedback-reply">
          <label>موعد متابعة الملاحظة<input type="date" required value={due} disabled={busy} onChange={e => setDue(e.target.value)}/></label><Button type="submit" disabled={busy}>حفظ الموعد</Button>
        </form>}
        {item.kind !== "Rating" && <form onSubmit={e => { e.preventDefault(); void save("resolve"); }} className="feedback-reply">
          <label>الرد على الملاحظة<textarea required minLength={5} maxLength={500} value={reply} disabled={busy} onChange={e => setReply(e.target.value)}/></label>
          <p>يرسل إشعارًا داخليًا عامًا للأسرة المرتبطة فقط؛ ليس رسالة خارجية. لا يُغلق أي متابعة تشغيلية بدلًا عنها.</p>
          <Button type="submit" disabled={busy}>إرسال الرد وحل الملاحظة</Button>
        </form>}
      </SectionCard>}
    </>}
  </>;
}
