"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useEffect, useRef, useState } from "react";
import { Button, ConfirmationDialog, EmptyState, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { formatDateAr } from "../lib/formatters";
import { GuardianShell } from "../components/guardian-shell";
import styles from "./player-goals.module.css";

type Goal = { id: string; sportEnrollmentId: string; title: string; target: string; reviewOn: string; state: string; version: number; revision: number };
type Data = { enrollment: { id: string; playerId: string; player: string; sport: string; level: string | null }; asOfDate: string; canCreate?: boolean; canEdit?: boolean;
  items?: Goal[]; item?: Goal; hasMore?: boolean; historyHasMore?: boolean; history?: { revision: number; action: string; note: string | null; actorName: string; createdAtUtc: string }[] };
const states: Record<string, string> = { Draft: "مسودة خاصة بفريق التدريب", Published: "قيد العمل · منشور للأسرة", Achieved: "تحقق الهدف بمراجعة المدرب", Withdrawn: "هدف مسحوب" };
const actions: Record<string, string> = { Created: "إنشاء مسودة", Edited: "تعديل المسودة", Published: "نشر للأسرة", Reviewed: "مراجعة التقدم", Achieved: "تحقق الهدف", Withdrawn: "سحب الهدف" };
const root = "/api/v1/evaluations/development-goals";
export function useGoalData<T = Data>(path: string | null, revision: number, failureMessage = "الهدف أو اللاعب غير متاح لك الآن. ارجع لملف اللاعب وأعد المحاولة.") {
  const [retry, setRetry] = useState(0);
  const [result, setResult] = useState<{ path: string; revision: number; data: T | null; error: string; status: number } | null>(null);
  useEffect(() => {
    if (path === null) return;
    let controller: AbortController | null = null;
    const read = () => {
      controller?.abort(); controller = new AbortController(); const signal = controller.signal; setResult(null);
      fetch(path, { cache: "no-store", signal }).then(async response => {
        const data = response.ok ? await response.json() as T : null;
        if (!signal.aborted) setResult({ path, revision, data, error: response.ok ? "" : failureMessage, status: response.status });
      }).catch(() => {
        if (!signal.aborted) setResult({ path, revision, data: null, error: failureMessage, status: 0 });
      });
    };
    const visibility = () => { if (document.hidden) { controller?.abort(); setResult(null); } else read(); };
    read(); window.addEventListener("focus", read); document.addEventListener("visibilitychange", visibility);
    return () => { controller?.abort(); window.removeEventListener("focus", read); document.removeEventListener("visibilitychange", visibility); };
  }, [path, revision, failureMessage, retry]);
  // Scope results during render as well as aborting the previous fetch. A new
  // player or filter must never briefly display the previous one's data.
  const current = result?.path === path && result.revision === revision ? result : null;
  return { data: current?.data ?? null, error: current?.error ?? "", status: current?.status, reload: () => { setResult(null); setRetry(n => n + 1); } };
}
function GoalLoadError({ message, reload, family }: { message: string; reload: () => void; family?: boolean }) {
  return <><ErrorState message={message} />{family
    ? <button className={`guardian-card-cta ${styles.retry}`} type="button" onClick={reload}>إعادة المحاولة</button>
    : <Button variant="secondary" onClick={reload}>إعادة المحاولة</Button>}</>;
}

export function PlayerGoals({ enrollmentId }: { enrollmentId: string }) {
  const { data, error, reload } = useGoalData(`${root}/enrollments/${enrollmentId}`, 0, "تعذر تحميل أهداف اللاعب.");
  const query = `?enrollmentId=${enrollmentId}`;
  return <>
    <PageHeader title="أهداف تطور اللاعب" context="التدريب والتطور"
      back={data ? { label: "العودة إلى ملف اللاعب", href: `/dashboard/players/${data.enrollment.playerId}?tab=development` } : undefined}
      action={data?.canCreate ? { label: "هدف جديد", href: `/dashboard/evaluations/goals/new${query}` } : undefined} />
    {error ? <GoalLoadError message={error} reload={reload} /> : !data ? <LoadingState /> : <>
      <SectionCard title={`${data.enrollment.player} · ${data.enrollment.sport}`}>
        {data.enrollment.level && <p>المستوى المسجل: {data.enrollment.level}</p>}
        <div className="form-actions">
          <Link className="button button-secondary" href={`/dashboard/evaluations/monthly${query}`}>تقرير التطور الشهري</Link>
          <Link className="button button-secondary" href={`/dashboard/evaluations/badges${query}`}>شارات المجهود والتحسن</Link>
        </div>
      </SectionCard>
      {!data.items?.length && <EmptyState message="لا توجد أهداف مسجلة لهذا الاشتراك الرياضي." />}
      {data.items?.map(goal => <SectionCard key={goal.id} title={goal.title}>
        <span className="status-pill">{states[goal.state]}</span>
        <p>{goal.target}</p><p>موعد المراجعة: {formatDateAr(goal.reviewOn)}</p>
        <Link className="button button-secondary" href={`/dashboard/evaluations/goals/${goal.id}`}>فتح الهدف وسجل المراجعة</Link>
      </SectionCard>)}
      {data.hasMore && <p>تظهر أحدث 100 هدف.</p>}
    </>}
  </>;
}

type FamilyGoals = { hasMore: boolean; items: { goal: Goal; sport: string; reviewsHasMore: boolean; reviews: { action: string; note: string | null; actorName: string; createdAtUtc: string; revision: number }[] }[] };
const sharedActions = new Set(["Published", "Reviewed", "Achieved"]);

export function GuardianPlayerGoals({ playerId }: { playerId: string }) {
  const path = `/guardian/children/${playerId}`;
  const { data, error, reload } = useGoalData<FamilyGoals>(`/api/v1${path}/development-goals`, 0, "تعذر تحميل أهداف التطور.");
  const visible = data?.items.filter(({ goal }) => goal.state === "Published" || goal.state === "Achieved") ?? [];
  return <GuardianShell>
    <header className={`guardian-page-header guardian-match-header ${styles.familyHeader}`}>
      <Link className="guardian-match-back" href={path}>العودة إلى ملف اللاعب</Link>
      <h1>أهداف التطور</h1>
    </header>
    {error ? <section className="profile-section"><GoalLoadError message={error} reload={reload} family /></section> : !data ? <LoadingState /> : <>
      {!visible.length && <section className="profile-section"><EmptyState message="لم ينشر فريق التدريب أهدافًا لهذا اللاعب بعد." /></section>}
      {visible.map(({ goal, sport, reviews, reviewsHasMore }) => {
        const sharedReviews = reviews.filter(review => sharedActions.has(review.action));
        const latest = sharedReviews[0];
        return <section className={`profile-section ${styles.familyGoal}`} key={goal.id}>
          <h2>{goal.title}</h2>
          <div className={styles.metadata}>
            <span className="status-pill">{goal.state === "Achieved" ? "تحقق الهدف" : "قيد العمل"}</span>
            <span>{sport}</span>
          </div>
          <p>{goal.target}</p><p>موعد المراجعة: {formatDateAr(goal.reviewOn)}</p>
          {latest && <article className="profile-row"><div>
            <b>آخر تحديث · {actions[latest.action]}</b>
            <small>{latest.actorName} · {formatDateAr(latest.createdAtUtc.slice(0, 10))}</small>
            {latest.note && <p>{latest.note}</p>}
          </div></article>}
          {sharedReviews.length > 1 && <details>
            <summary>المراجعات السابقة</summary>
            {sharedReviews.slice(1).map(review => <article className="profile-row" key={review.revision}><div>
              <b>{actions[review.action]} · {review.actorName}</b><small>{formatDateAr(review.createdAtUtc.slice(0, 10))}</small>
              {review.note && <p>{review.note}</p>}
            </div></article>)}
            {reviewsHasMore && <p>تظهر أحدث 20 مراجعة.</p>}
          </details>}
        </section>;
      })}
      {data.hasMore && <section className="profile-section"><p>تظهر أحدث 100 هدف.</p></section>}
    </>}
    <div className={styles.related}>
      <Link className="guardian-card-cta" href={`${path}/monthly`}>تقرير التطور الشهري</Link>
    </div>
  </GuardianShell>;
}

type DraftFields = Pick<Goal, "title" | "target" | "reviewOn">;
type ReviewFields = { action: "Reviewed" | "Achieved"; note: string };

function latestReviewDate(asOfDate: string) {
  if (!asOfDate) return undefined;
  const [year, month, day] = asOfDate.split("-").map(Number);
  const lastDay = new Date(Date.UTC(year + 1, month, 0)).getUTCDate();
  return new Date(Date.UTC(year + 1, month - 1, Math.min(day, lastDay))).toISOString().slice(0, 10);
}

export function PlayerGoalEditor({ enrollmentId, id }: { enrollmentId?: string; id?: string }) {
  const command = useRef<{ body: string; requestId: string } | null>(null);
  const saving = useRef(false);
  const router = useRouter();
  const [revision, setRevision] = useState(0), [error, setError] = useState(""), [busy, setBusy] = useState(false), [message, setMessage] = useState("");
  const [draftEdit, setDraftEdit] = useState<{ scope: string; fields: DraftFields }>();
  const [reviewEdit, setReviewEdit] = useState<{ scope: string; fields: ReviewFields }>();
  const [confirmation, setConfirmation] = useState<{ scope: string; action: "Published" | "Withdrawn" }>();
  const [conflict, setConflict] = useState(false);
  const { data, error: loadError, reload } = useGoalData(id ? `${root}/${id}` : `${root}/enrollments/${enrollmentId}`, revision, "تعذر تحميل بيانات الهدف.");
  const goal = data?.item;
  const scope = `${id ?? enrollmentId}:${goal?.version ?? 0}`;
  const savedDraft = { title: goal?.title ?? "", target: goal?.target ?? "", reviewOn: goal?.reviewOn ?? data?.asOfDate ?? "" };
  const draft = draftEdit?.scope === scope ? draftEdit.fields : savedDraft;
  const review = reviewEdit?.scope === scope ? reviewEdit.fields : { action: "Reviewed", note: "" } as const;
  const dirty = draft.title !== savedDraft.title || draft.target !== savedDraft.target || draft.reviewOn !== savedDraft.reviewOn;
  const expired = Boolean(goal && data && goal.reviewOn < data.asOfDate);
  const canWrite = id ? data?.canEdit : data?.canCreate;
  const dialog = confirmation?.scope === scope && canWrite && !conflict ? confirmation.action : null;

  async function save(body: object, transition = false) {
    if (saving.current || !canWrite || conflict) return;
    saving.current = true;
    setBusy(true); setError(""); setMessage("");
    try {
      const payload = { ...body, version: goal?.version ?? 0 };
      const encoded = JSON.stringify(payload);
      if (command.current?.body !== encoded) command.current = { body: encoded, requestId: crypto.randomUUID() };
      const response = await csrfRequest(id ? `${root}/${id}${transition ? "/transition" : ""}` : `${root}/enrollments/${enrollmentId}`, id && !transition ? "PUT" : "POST", { ...payload, requestId: command.current.requestId }, {}, { announce: false });
      const result = await response.json() as { id: string };
      if (!result.id) throw new Error();
      command.current = null;
      setDraftEdit(undefined); setReviewEdit(undefined);
      if (!id) router.push(`/dashboard/evaluations/goals/${result.id}`);
      else {
        setMessage("تم حفظ تحديث الهدف وسجل المراجعة.");
        setRevision(n => n + 1);
      }
    } catch (e) {
      if (e instanceof DashboardRequestError && e.status === 409) setConflict(true);
      setError(e instanceof DashboardRequestError ? e.message : "تعذر تأكيد الحفظ.");
    } finally {
      saving.current = false;
      setBusy(false); setConfirmation(undefined);
    }
  }

  function saveDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (draft.title.trim() && draft.target.trim()) void save({ ...draft, title: draft.title.trim(), target: draft.target.trim() });
    else setError("أدخل عنوان الهدف والخطوة المطلوبة.");
  }

  const locked = busy || conflict;
  return <>
    <PageHeader title={id ? "هدف التطور" : "هدف تطور جديد"} context="التدريب والتطور"
      back={data ? { label: "العودة إلى أهداف اللاعب", href: `/dashboard/evaluations/goals?enrollmentId=${data.enrollment.id}` } : undefined} />
    {loadError && <GoalLoadError message={loadError} reload={reload} />}
    {error && <ErrorState message={error} />}
    {conflict && <Button variant="secondary" onClick={() => { setConflict(false); setError(""); reload(); }}>قراءة أحدث نسخة</Button>}
    {message && <p role="status">{message}</p>}
    {!data ? !loadError && <LoadingState /> : <>
      <p>{data.enrollment.player} · {data.enrollment.sport}{goal?.state === "Draft" && <> · <span className="status-pill">{states.Draft}</span></>}</p>
      {goal && (goal.state !== "Draft" || !canWrite) && <SectionCard title={goal.title}>
        <span className="status-pill">{states[goal.state]}</span>
        <p>{goal.target}</p><p>موعد المراجعة: {formatDateAr(goal.reviewOn)}</p>
      </SectionCard>}
      {canWrite && (!goal || goal.state === "Draft") && <form className={`form-shell ${styles.editorForm}`} onSubmit={saveDraft}>
        <label>عنوان الهدف<input name="title" required maxLength={100} value={draft.title} onChange={e => setDraftEdit({ scope, fields: { ...draft, title: e.target.value } })} disabled={locked} /></label>
        <label>ما الذي نعمل عليه؟<textarea name="target" required maxLength={500} value={draft.target} onChange={e => setDraftEdit({ scope, fields: { ...draft, target: e.target.value } })} disabled={locked} /></label>
        <label>موعد المراجعة<input name="reviewOn" type="date" required min={data.asOfDate} max={latestReviewDate(data.asOfDate)} value={draft.reviewOn} onChange={e => setDraftEdit({ scope, fields: { ...draft, reviewOn: e.target.value } })} disabled={locked} /></label>
        {expired && <p>موعد المراجعة المحفوظ فات. اختر موعدًا جديدًا واحفظه قبل النشر.</p>}
        {dirty && goal && <p>احفظ التعديلات لمراجعة النص قبل النشر.</p>}
        <div className="form-actions">
          <Button type="submit" disabled={locked || Boolean(goal && !dirty)}>حفظ المسودة</Button>
          {goal && <Button variant="secondary" disabled={locked || dirty || expired} onClick={() => setConfirmation({ scope, action: "Published" })}>مراجعة النشر</Button>}
        </div>
      </form>}
      {!goal && !canWrite && <EmptyState message="إضافة هدف تحتاج صلاحية على هذا الاشتراك." />}
      {canWrite && goal?.state === "Published" && <form className={`form-shell ${styles.editorForm}`} onSubmit={e => { e.preventDefault(); void save(review, true); }}>
        <label>نتيجة المراجعة<select name="action" value={review.action} onChange={e => setReviewEdit({ scope, fields: { ...review, action: e.target.value as ReviewFields["action"] } })} disabled={locked}><option value="Reviewed">ما زال قيد العمل</option><option value="Achieved">تحقق الهدف بمراجعة فنية</option></select></label>
        <label>ملاحظة التقدم للأسرة (اختياري)<textarea name="note" maxLength={500} value={review.note} onChange={e => setReviewEdit({ scope, fields: { ...review, note: e.target.value } })} disabled={locked} /></label>
        <p>تظهر نتيجة المراجعة للأسرة. اعتماد تحقق الهدف لا يغيّر المستوى أو المجموعة أو الاشتراك.</p>
        <Button type="submit" disabled={locked}>حفظ المراجعة</Button>
      </form>}
      {!!data.history?.length && <SectionCard title="سجل المراجعات">
        {data.history.map(change => <article key={change.revision}><h3>{actions[change.action] ?? "تحديث"} · {change.actorName}</h3><p>{formatDateAr(change.createdAtUtc.slice(0, 10))}</p>{change.note && <p>{change.note}</p>}</article>)}
        {data.historyHasMore && <p>تظهر أحدث 100 مراجعة.</p>}
      </SectionCard>}
      {canWrite && goal && ["Draft", "Published"].includes(goal.state) && <Button className={styles.withdraw} variant="danger" disabled={locked} onClick={() => setConfirmation({ scope, action: "Withdrawn" })}>سحب الهدف</Button>}
      {goal && <ConfirmationDialog open={Boolean(dialog)} busy={busy} tone={dialog === "Published" ? "primary" : "danger"}
        title={dialog === "Published" ? "نشر الهدف للأسرة؟" : "سحب الهدف؟"}
        message={dialog === "Published"
          ? `${goal.title} — ${goal.target} · المراجعة ${formatDateAr(goal.reviewOn)}. راجع النص وتأكد من خلوه من البيانات الطبية أو بيانات لاعب آخر.`
          : "يُخفى الهدف عن الأسرة ويظل سجله متاحًا لفريق التدريب."}
        confirmLabel={dialog === "Published" ? "نشر الهدف للأسرة" : "سحب الهدف"}
        onCancel={() => setConfirmation(undefined)}
        onConfirm={() => { if (dialog && (dialog === "Withdrawn" || (!dirty && !expired))) void save({ action: dialog, note: null }, true); }} />}
    </>}
  </>;
}
