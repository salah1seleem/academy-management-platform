"use client";
import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { feedbackStates } from "./feedback-state";
import styles from "./feedback-case.module.css";

function usePrivateRead<T>(path: string, revision: number) {
  const [result, setResult] = useState<{ path: string; revision: number; data?: T; error?: string }>();
  useEffect(() => {
    let controller: AbortController;
    const load = () => {
      controller?.abort(); controller = new AbortController(); const signal = controller.signal; setResult(undefined);
      if (document.hidden) return;
      fetch(path, { cache: "no-store", signal }).then(async r => { if (!r.ok) throw new Error(); return r.json(); })
        .then(data => { if (!signal.aborted) setResult({ path, revision, data }); })
        .catch(() => { if (!signal.aborted) setResult({ path, revision, error: "التكليف غير متاح ضمن إسنادك وصلاحياتك الحالية." }); });
    };
    load(); window.addEventListener("focus", load); document.addEventListener("visibilitychange", load);
    return () => { controller?.abort(); window.removeEventListener("focus", load); document.removeEventListener("visibilitychange", load); };
  }, [path, revision]);
  return result?.path === path && result.revision === revision ? result : undefined;
}
function useAttempt() {
  const attempt = useRef<{ signature: string; requestId: string } | null>(null);
  return (body: object) => { const signature = JSON.stringify(body); if (attempt.current?.signature !== signature) attempt.current = { signature, requestId: crypto.randomUUID() }; return { ...body, requestId: attempt.current.requestId }; };
}
export function FeedbackAssignment({ id, version, current, brief, saved }: { id: string; version: number; current?: string | null; brief?: string | null; saved: () => void }) {
  const result = usePrivateRead<{ items: { id: string; name: string }[] }>(`/api/v1/manage/feedback/${id}/investigators`, 0);
  const [member, setMember] = useState(current ?? ""), [text, setText] = useState(brief ?? ""), [busy, setBusy] = useState(false), [error, setError] = useState("");
  const body = useAttempt();
  async function save(clear = false) {
    if (busy) return; setBusy(true); setError("");
    try { await csrfRequest(`/api/v1/manage/feedback/${id}/assignment`, "POST", body({ investigatorMembershipId: clear ? null : member, brief: clear ? null : text.trim(), version })); saved(); }
    catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر حفظ الإسناد؛ لم نسجل نجاحًا."); } finally { setBusy(false); }
  }
  return <div className={styles.investigation}><h2>تكليف تحقيق خاص</h2><p>اختر إداريًا مخولًا بالمتابعات ضمن فرع ورياضة المجموعة. لا يجوز إسناد التحقيق لطرف في الملاحظة، حتى بحساب إداري آخر.</p><p>اكتب موجزًا مستقلًا دون أسماء الطفل والأسرة أو وسيلة اتصالهم. يُرسل هذا الموجز فقط؛ لا يُنسخ نص الشكوى تلقائيًا. الإسناد لا يمنح أي صلاحية إضافية.</p>
    {result?.error && <p role="alert">{result.error}</p>}{result?.data && !result.data.items?.length && <p>لا يوجد إداري مخول مستقل ضمن فرع ورياضة المجموعة. راجع الصلاحيات أولًا.</p>}{result?.data && <form className={styles.form} onSubmit={e => { e.preventDefault(); void save(); }}>
      <label>مسؤول التحقيق<select required value={member} disabled={busy} onChange={e => setMember(e.target.value)}><option value="">اختر مسؤولًا مخولًا</option>{result.data.items?.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}</select></label>
      <label>موجز يراجعه المالك<textarea required minLength={5} maxLength={500} value={text} disabled={busy} onChange={e => setText(e.target.value)}/></label>
      <button type="submit" disabled={busy || !member || !result.data.items?.some(m => m.id === member)}>حفظ تكليف التحقيق</button>
    </form>}{current && <button type="button" disabled={busy} onClick={() => void save(true)}>إلغاء تكليف التحقيق</button>}{error && <p role="alert">{error}</p>}
  </div>;
}
type Investigation = { assignmentId: string; brief: string; status: string; dueOn: string | null };
type Detail = Investigation & { version: number; canEdit: boolean; findings: { id: string; note: string; createdAtUtc: string }[] };
export function PrivateInvestigations({ id }: { id?: string }) {
  const [revision, setRevision] = useState(0), [notice, setNotice] = useState("");
  const result = usePrivateRead<Detail & { items?: Investigation[]; hasMore?: boolean }>(`/api/v1/my-followups/investigations${id ? `/${id}` : ""}`, revision);
  const data = result?.data;
  return <section className={styles.investigation} dir="rtl"><h2>{id ? "تفاصيل تكليف التحقيق" : "تحقيقات خاصة مسندة إليك"}</h2>
    <p>موجز راجعه المالك، دون ملف الأسرة أو نص شكواها. نتائجك خاصة للمالك؛ ليست ردًا للأسرة أو حلًا أو إغلاقًا للملاحظة.</p>
    {id && <Link href="/dashboard/my-followups">العودة لمتابعاتي</Link>}{notice && <p role="status">{notice}</p>}
    {result?.error ? <p role="alert">{result.error}</p> : !data ? <p role="status">جارٍ تحميل التحقيقات…</p> : id ? <>
      <p>{data.brief}</p><p>{feedbackStates[data.status]} · الموعد: <bdi>{data.dueOn ?? "غير محدد"}</bdi></p>
      {data.canEdit ? <Findings key={`${id}:${data.version}`} item={data} saved={() => { setNotice("تم حفظ النتيجة الخاصة للمالك فقط."); setRevision(x => x + 1); }}/>: <p>للقراءة فقط وفق صلاحياتك الحالية.</p>}
      <h3>آخر نتائجك لهذا التكليف</h3>{data.findings.map(f => <blockquote key={f.id}>{f.note}</blockquote>)}
    </> : <>{data.items?.map(i => <article key={i.assignmentId}><p>{i.brief}</p><p>{feedbackStates[i.status]} · الموعد: <bdi>{i.dueOn ?? "غير محدد"}</bdi></p><Link href={`/dashboard/my-followups/investigations/${i.assignmentId}`}>فتح تكليف التحقيق</Link></article>)}{!data.items?.length && <p>لا توجد تحقيقات خاصة مسندة إليك.</p>}{data.hasMore && <p>تظهر أول 100 نتيجة؛ بقية التكليفات محفوظة لدى المالك.</p>}</>}
    <button type="button" onClick={() => setRevision(x => x + 1)}>تحديث التحقيقات</button>
  </section>;
}
function Findings({ item, saved }: { item: Detail; saved: () => void }) {
  const [note, setNote] = useState(""), [busy, setBusy] = useState(false), [error, setError] = useState(""); const body = useAttempt();
  async function submit(e: FormEvent) { e.preventDefault(); if (busy) return; setBusy(true); setError(""); try { await csrfRequest(`/api/v1/my-followups/investigations/${item.assignmentId}/transition`, "POST", body({ version: item.version, note: note.trim() })); saved(); } catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر حفظ النتيجة الخاصة."); } finally { setBusy(false); } }
  return <form className={styles.form} onSubmit={submit}><label>نتيجة التحقيق للمالك<textarea required minLength={5} maxLength={500} value={note} disabled={busy} onChange={e => setNote(e.target.value)}/></label><button disabled={busy}>حفظ النتيجة الخاصة</button>{error && <p role="alert">{error}</p>}</form>;
}
