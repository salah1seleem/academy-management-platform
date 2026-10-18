"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useEffect, useRef, useState } from "react";
import { Button, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { GuardianShell } from "../components/guardian-shell";

type Goal = { id: string; sportEnrollmentId: string; title: string; target: string; reviewOn: string; state: string; version: number; revision: number };
type Data = { enrollment: { id: string; playerId: string; player: string; sport: string; level: string | null }; asOfDate: string; canCreate?: boolean; canEdit?: boolean;
  items?: Goal[]; item?: Goal; hasMore?: boolean; historyHasMore?: boolean; history?: { revision: number; action: string; note: string | null; actorName: string; createdAtUtc: string }[] };
const states: Record<string, string> = { Draft: "مسودة خاصة بفريق التدريب", Published: "قيد العمل · منشور للأسرة", Achieved: "تحقق الهدف بمراجعة المدرب", Withdrawn: "هدف مسحوب" };
const actions: Record<string, string> = { Created: "إنشاء مسودة", Edited: "تعديل المسودة", Published: "نشر للأسرة", Reviewed: "مراجعة التقدم", Achieved: "تحقق الهدف", Withdrawn: "سحب الهدف" };
const root = "/api/v1/evaluations/development-goals";
export function useGoalData<T = Data>(path: string, revision: number, failureMessage = "الهدف أو اللاعب غير متاح لك الآن. ارجع لملف اللاعب وأعد المحاولة.") {
  const [data, setData] = useState<T | null>(null), [error, setError] = useState("");
  useEffect(() => {
    let controller: AbortController | null = null;
    const read = () => {
      controller?.abort(); controller = new AbortController(); const signal = controller.signal; setData(null); setError("");
      fetch(path, { cache: "no-store", signal }).then(async r => { if (!r.ok) throw Error(); return r.json() as Promise<T>; }).then(d => { if (!signal.aborted) setData(d); }).catch(() => { if (!signal.aborted) setError(failureMessage); });
    };
    const visibility = () => { if (document.hidden) { controller?.abort(); setData(null); } else read(); };
    read(); window.addEventListener("focus", read); document.addEventListener("visibilitychange", visibility);
    return () => { controller?.abort(); window.removeEventListener("focus", read); document.removeEventListener("visibilitychange", visibility); };
  }, [path, revision, failureMessage]);
  return { data, error };
}
export function PlayerGoals({ enrollmentId }: { enrollmentId: string }) {
  const { data, error } = useGoalData(`${root}/enrollments/${enrollmentId}`, 0);
  return <><PageHeader title="أهداف تطور اللاعب" context="التدريب والتطور" />{error && <ErrorState message={error} />}{!data ? !error && <LoadingState /> : <>
    <SectionCard title={`${data.enrollment.player} · ${data.enrollment.sport}`}><p>المستوى المسجل: {data.enrollment.level ?? "لم يحدد"}. الهدف الفني خطوة قابلة للمراجعة، وليس ضمانًا زمنيًا أو ترقية تلقائية.</p>
      <div className="row-actions"><Link className="button button-secondary" href={`/dashboard/players/${data.enrollment.playerId}?tab=development`}>ملف اللاعب</Link>{data.canCreate && <Link className="button button-primary" href={`/dashboard/evaluations/goals/new?enrollmentId=${enrollmentId}`}>هدف جديد</Link>}</div>
    </SectionCard>
    {!data.items?.length && <SectionCard title="ابدأ بخطوة واضحة"><p>لا توجد أهداف مسجلة لهذا الاشتراك الرياضي. أضف ما يعمل عليه اللاعب ثم انشره عندما يكون جاهزًا للأسرة.</p></SectionCard>}
    {data.items?.map(g => <SectionCard key={g.id} title={g.title}><span className="status-pill">{states[g.state]}</span><p>{g.target}</p><p>المراجعة الفنية: <bdi>{g.reviewOn}</bdi></p><Link className="button button-secondary" href={`/dashboard/evaluations/goals/${g.id}`}>فتح الهدف وسجل المراجعة</Link></SectionCard>)}
    {data.hasMore && <p>نعرض أحدث100 هدف؛ راجع الأقدم من ملف اللاعب مع الإدارة.</p>}
  </>}</>;
}
type FamilyGoals = { hasMore: boolean; items: { goal: Goal; sport: string; reviewsHasMore: boolean; reviews: { action: string; note: string | null; actorName: string; createdAtUtc: string; revision: number }[] }[] };
export function GuardianPlayerGoals({ playerId }: { playerId: string }) {
  const { data, error } = useGoalData<FamilyGoals>(`/api/v1/guardian/children/${playerId}/development-goals`, 0);
  return <GuardianShell><header className="guardian-page-header"><h1>خطوات تطور ابنك</h1><p>أهداف منشورة ومراجعات فعلية من فريق التدريب، وليست ترقية تلقائية أو ضمانًا زمنيًا.</p><Link className="back-link" href={`/guardian/children/${playerId}`}>العودة لملف اللاعب</Link></header>
    {error && <ErrorState message={error}/>} {!data ? !error && <LoadingState/> : <>
      {!data.items.length && <p>لم ينشر فريق التدريب أهدافًا بعد. تظل تقييمات اللاعب متاحة في ملفه.</p>}
      {data.items.filter(x => ["Published", "Achieved"].includes(x.goal.state)).map(({ goal: g, sport, reviews, reviewsHasMore }) => <section className="profile-section" key={g.id}>
        <span className="status-pill">{states[g.state]}</span><h2>{g.title}</h2><p>{sport} · {g.target}</p><p>موعد المراجعة: <bdi>{g.reviewOn}</bdi></p>
        {reviews.map(c => <article className="profile-row" key={c.revision}><div><b>{actions[c.action]} · {c.actorName}</b><small>{c.createdAtUtc.slice(0, 10)}</small>{c.note && <p>{c.note}</p>}</div></article>)}{reviewsHasMore && <p>تظهر أحدث20 مراجعة منشورة؛ يمكن الرجوع للإدارة لطلب سجل أقدم.</p>}
      </section>)}{data.hasMore && <p>تظهر أحدث100 هدف.</p>}
    </>}
  </GuardianShell>;
}
export function PlayerGoalEditor({ enrollmentId, id }: { enrollmentId?: string; id?: string }) {
  const command = useRef<{ body: string; requestId: string } | null>(null);
  const router = useRouter(), [revision, setRevision] = useState(0), [error, setError] = useState(""), [busy, setBusy] = useState(false), [message, setMessage] = useState("");
  const { data, error: loadError } = useGoalData(id ? `${root}/${id}` : `${root}/enrollments/${enrollmentId}`, revision);
  async function save(body: object, transition = false) {
    if (busy) return; setBusy(true); setError(""); setMessage("");
    try {
      const payload = { ...body, version: data?.item?.version ?? 0 };
      const encoded = JSON.stringify(payload);
      if (command.current?.body !== encoded) command.current = { body: encoded, requestId: crypto.randomUUID() };
      const response = await csrfRequest(id ? `${root}/${id}${transition ? "/transition" : ""}` : `${root}/enrollments/${enrollmentId}`, id && !transition ? "PUT" : "POST", { ...payload, requestId: command.current.requestId });
      const result = await response.json() as { id: string };
      command.current = null;
      if (!id) router.push(`/dashboard/evaluations/goals/${result.id}`); else { setMessage("تم حفظ تحديث الهدف وسجل المراجعة."); setRevision(n => n + 1); }
    } catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر الحفظ. لم نسجل نجاحًا."); } finally { setBusy(false); }
  }
  function draft(e: FormEvent<HTMLFormElement>) { e.preventDefault(); const values = new FormData(e.currentTarget); void save({ title: values.get("title"), target: values.get("target"), reviewOn: values.get("reviewOn") }); }
  const g = data?.item, canWrite = id ? data?.canEdit : data?.canCreate;
  return <><PageHeader title={id ? "هدف اللاعب ومراجعاته" : "خطوة جديدة لتطور اللاعب"} context="التدريب والتطور" />
    {(loadError || error) && <ErrorState message={loadError || error} />}{message && <p role="status">{message}</p>}{!data ? !loadError && <LoadingState /> : <>
      <p>{data.enrollment.player} · {data.enrollment.sport}</p><Link className="button button-secondary" href={`/dashboard/evaluations/goals?enrollmentId=${data.enrollment.id}`}>العودة لأهداف اللاعب</Link>
      {g && <SectionCard title={g.title}><span className="status-pill">{states[g.state]}</span><p>{g.target}</p><p>موعد مراجعة: <bdi>{g.reviewOn}</bdi> · نسخة{g.revision}</p><p>تحقق الهدف لا يغيّر المستوى أو المجموعة أو الاشتراك تلقائيًا.</p></SectionCard>}
      {canWrite && (!g || g.state === "Draft") && <form className="form-shell" key={g?.revision ?? "new"} onSubmit={draft}>
        <label>عنوان الهدف<input name="title" required maxLength={100} defaultValue={g?.title} disabled={busy} /></label>
        <label>ما الذي نعمل عليه؟<textarea name="target" required maxLength={500} defaultValue={g?.target} disabled={busy} /></label>
        <label>موعد المراجعة<input name="reviewOn" type="date" required min={data.asOfDate} defaultValue={g?.reviewOn ?? data.asOfDate} disabled={busy} /></label>
        <p>المسودة لا تظهر للأسرة. عند النشر يظهر الهدف وسجل المراجعات المنشورة فقط. لا تكتب بيانات طبية أو معلومات طفل آخر.</p><Button type="submit" disabled={busy}>حفظ المسودة</Button>
      </form>}
      {canWrite && g?.state === "Draft" && <Button disabled={busy} onClick={() => void save({ action: "Published", note: null }, true)}>نشر الهدف للأسرة</Button>}
      {canWrite && g?.state === "Published" && <form className="form-shell" onSubmit={e => { e.preventDefault(); const v = new FormData(e.currentTarget); void save({ action: v.get("action"), note: v.get("note") }, true); }}>
        <label>نتيجة المراجعة<select name="action" disabled={busy}><option value="Reviewed">ما زال قيد العمل</option><option value="Achieved">تحقق الهدف بمراجعة فنية</option></select></label>
        <label>ملاحظة التقدم للأسرة (اختياري)<textarea name="note" maxLength={500} disabled={busy} /></label><Button type="submit" disabled={busy}>حفظ المراجعة</Button>
      </form>}
      {canWrite && g && ["Draft", "Published"].includes(g.state) && <Button variant="danger" disabled={busy} onClick={() => { if (window.confirm("سحب الهدف يخفيه عن الأسرة ويحفظ تاريخه. هل تريد المتابعة؟")) void save({ action: "Withdrawn", note: null }, true); }}>سحب الهدف مع حفظ السجل</Button>}
      {data.history && <SectionCard title="سجل فني محفوظ">{data.history.map(c => <article key={c.revision}><h3>{actions[c.action] ?? "تحديث"} · {c.actorName}</h3><p><bdi>{c.createdAtUtc.slice(0, 10)}</bdi> · نسخة{c.revision}</p>{c.note && <p>{c.note}</p>}</article>)}{data.historyHasMore && <p>تظهر أحدث100 مراجعة.</p>}</SectionCard>}
    </>}</>;
}
