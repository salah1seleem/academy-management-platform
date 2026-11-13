"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useRef, useState, type FormEvent } from "react";
import { Button, EmptyState, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { GuardianShell } from "../components/guardian-shell";
import { formatDateAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { useGoalData } from "./player-goals";
import { useStaffAction } from "./use-staff-action";
import styles from "./player-skills.module.css";

const root = "/api/v1/evaluations/technical-reviews";
const inbox = "/dashboard/evaluations/technical-reviews";
const withDecided = "?includeDecided=true";
const outcomes: Record<string, string> = {
  ReadyForPlacement: "مستعد لمراجعة التسكين",
  NeedsPractice: "يحتاج مزيدًا من التدريب",
  ContextChanged: "أُغلق لتغيّر الخطة أو الأدلة",
};
const units: Record<string, string> = { Count: "مرة", Seconds: "ثانية", Metres: "متر" };

type Request = { id: string; planId: string; sportEnrollmentId: string; sequence: number; sourceReviewSequence: number; levelName: string; curriculumTitle: string; requirements: number; actorName: string; createdAtUtc: string; version: number };
type Decision = { outcome: string; recommendedLevelName: string | null; familyNote: string | null; actorName: string; createdAtUtc: string };
type Entry = { request: Request; decision: Decision | null; application?: { toLevelName: string; createdAtUtc: string } | null };
type PlanData = { plan: { id: string; version: number }; enrollment: { player: string; sport: string }; source: { eligible: boolean; head: number; requirements: number; mastered: number }; latestRequestId: string | null; canRequest: boolean; items: Entry[]; hasMore: boolean };
type DetailData = Entry & {
  enrollment: { player: string; sport: string };
  evidence: { name: string; unit: string; protocol: string; measuredValue: number | null; observedOn: string; actorName: string }[];
  canReviewPlacement?: boolean; currentEligible: boolean; contextChanged: boolean; canDecide: boolean;
  levels: { id: string; name: string }[];
};

function useTechnicalCommand(onSaved: () => void) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [needsRead, setNeedsRead] = useState(false);
  const inFlight = useRef(false);
  const pending = useRef<{ signature: string; requestId: string } | null>(null);

  async function save(path: string, body: object) {
    if (inFlight.current || needsRead) return;
    const signature = JSON.stringify({ path, body });
    if (pending.current?.signature !== signature) pending.current = { signature, requestId: crypto.randomUUID() };
    inFlight.current = true;
    setBusy(true);
    setError("");
    try {
      await csrfRequest(path, "POST", { ...body, requestId: pending.current.requestId }, {}, { announce: false });
      pending.current = null;
      onSaved();
    } catch (caught) {
      const status = caught instanceof DashboardRequestError ? caught.status : 0;
      const uncertain = status === 0 || status >= 500;
      const changed = [401, 403, 404, 409].includes(status);
      setNeedsRead(uncertain || changed);
      setError(uncertain
        ? "تعذر تأكيد الحفظ. تحقّق من حالة المراجعة قبل إعادة الإرسال."
        : changed ? "تغيّرت البيانات أو الصلاحية. تحقّق من حالة المراجعة."
        : caught instanceof DashboardRequestError ? caught.message : "تعذر الحفظ. أعد المحاولة.");
    } finally {
      inFlight.current = false;
      setBusy(false);
    }
  }

  function check(reload: () => void) {
    reload();
    setNeedsRead(false);
    setError("");
  }

  return { busy, error, needsRead, save, check };
}

function TechnicalReadError({ message, reload }: { message: string; reload: () => void }) {
  return <><ErrorState message={message} /><Button variant="secondary" onClick={reload}>إعادة المحاولة</Button></>;
}

function RequestMeta({ request }: { request: Request }) {
  return <p className={styles.reviewMeta}>طلبها {request.actorName} · {formatDateAr(request.createdAtUtc)}</p>;
}

function DecisionText({ entry, contextChanged = false }: { entry: Entry; contextChanged?: boolean }) {
  const { decision, application } = entry;
  return <>
    <p><span className="status-pill">{application ? "نُفّذت التوصية" : decision ? outcomes[decision.outcome] : "بانتظار القرار الفني"}</span></p>
    {application ? <p>المستوى المنفّذ: <strong>{application.toLevelName}</strong> · {formatDateAr(application.createdAtUtc)}</p>
      : decision?.recommendedLevelName && <>
        <p>المستوى الموصى به: <strong>{decision.recommendedLevelName}</strong></p>
        <p className={styles.reviewMeta}>{contextChanged
          ? "تغيّرت الخطة أو الأدلة بعد صدور التوصية."
          : "لم يتغير مستوى اللاعب أو مجموعته بهذا القرار. التنفيذ يحتاج مراجعة إدارية."}</p>
      </>}
    {decision?.familyNote && <p>{decision.familyNote}</p>}
    {decision && <p className={styles.reviewMeta}>{decision.actorName} · {formatDateAr(decision.createdAtUtc)}</p>}
  </>;
}

export function TechnicalInboxLink() {
  return useStaffAction("players", "read") ? <Link className="button button-secondary" href={inbox}>طلبات المراجعة الفنية</Link> : null;
}

export function PlayerTechnicalReviews({ planId, playerId }: { planId: string; playerId?: string }) {
  const [revision, setRevision] = useState(0);
  const [message, setMessage] = useState("");
  const family = Boolean(playerId);
  const back = family ? `/guardian/children/${playerId}/skills/plans/${planId}` : `/dashboard/evaluations/skills/plans/${planId}`;
  const path = family ? `/api/v1${back}/technical-reviews` : `/api/v1/evaluations/skills/plans/${planId}/technical-reviews`;
  const { data, error, reload } = useGoalData<PlanData>(path, revision, "تعذر تحميل المراجعات لهذه الخطة.");
  const command = useTechnicalCommand(() => {
    setMessage("تم إرسال طلب المراجعة.");
    setRevision(n => n + 1);
  });
  const latest = data?.items.find(entry => entry.request.id === data.latestRequestId);
  const title = "المراجعة الفنية للمستوى";
  const backLabel = "العودة إلى مهارات اللاعب";
  const content = <>
    {family ? <header className="guardian-page-header guardian-match-header">
      <Link className="guardian-match-back" href={back}>{backLabel}</Link><h1>{title}</h1>
    </header> : <PageHeader title={title} context="تطور اللاعب" back={{ href: back, label: backLabel }} />}
    {message && <p role="status">{message}</p>}
    {command.error && <ErrorState message={command.error} />}
    {command.needsRead && <Button variant="secondary" onClick={() => command.check(reload)}>تحقّق من حالة المراجعة</Button>}
    {error ? (family ? <section className="guardian-section"><TechnicalReadError message={error} reload={reload} /></section>
      : <TechnicalReadError message={error} reload={reload} />) : !data ? <LoadingState /> : <>
      <SectionCard title={`${data.enrollment.player} · ${data.enrollment.sport}`}>
        {!family && <>
          <p><bdi>{data.source.mastered}/{data.source.requirements}</bdi> متطلبات متقنة</p>
          {data.canRequest ? <Button disabled={command.busy || command.needsRead} onClick={() => {
            setMessage("");
            void command.save(path, { planVersion: data.plan.version, sourceReviewSequence: data.source.head, previousRequestId: data.latestRequestId });
          }}>{command.busy ? "جارٍ إرسال الطلب…" : "طلب مراجعة فنية للمستوى"}</Button>
            : latest && !latest.decision ? <p>يوجد طلب بانتظار القرار.</p>
            : data.source.eligible && latest && data.source.head <= latest.request.sourceReviewSequence ? <p>يلزم تسجيل دليل فني أحدث لإرسال طلب آخر.</p>
            : data.source.requirements > 0 && data.source.mastered < data.source.requirements ? <p>يمكن طلب المراجعة بعد إتقان متطلبات الخطة الحالية.</p>
            : null}
        </>}
        {!data.items.length && <p>لا توجد طلبات مراجعة لهذه الخطة بعد.</p>}
      </SectionCard>
      {data.items.map(entry => <SectionCard key={entry.request.id} title={`مراجعة ${entry.request.sequence} · ${entry.request.levelName}`}>
        <DecisionText entry={entry} contextChanged={!data.source.eligible || data.source.head !== entry.request.sourceReviewSequence} />
        <RequestMeta request={entry.request} />
        {!family && <Link className="button button-secondary" href={`${inbox}/${entry.request.id}?fromPlan=1`}>فتح الأدلة وقرار المراجعة</Link>}
      </SectionCard>)}
      {data.hasMore && <p>تظهر أحدث 20 مراجعة لهذه الخطة.</p>}
    </>}
  </>;
  return family ? <GuardianShell><div className={styles.family}>{content}</div></GuardianShell> : content;
}

export function TechnicalReviewInbox() {
  return <Suspense fallback={<LoadingState />}><ReviewInbox /></Suspense>;
}

function ReviewInbox() {
  const search = useSearchParams();
  const all = search.get("includeDecided") === "true";
  const { data, error, reload } = useGoalData<{ items: Entry[]; contexts: Record<string, { player: string; branch: string; sport: string }>; hasMore: boolean }>(
    `${root}/inbox?includeDecided=${all}`, 0, "تعذر تحميل طلبات المراجعة.",
  );
  function changeFilter(value: boolean) {
    const url = new URL(window.location.href);
    if (value) url.searchParams.set("includeDecided", "true");
    else url.searchParams.delete("includeDecided");
    window.history.replaceState(null, "", url);
  }
  return <>
    <PageHeader title="طلبات المراجعة الفنية" context="تطور اللاعبين" />
    <div className="form-actions">
      <label className={styles.reviewFilter}><input type="checkbox" checked={all} onChange={event => changeFilter(event.target.checked)} /> إظهار الطلبات التي صدر قرارها أيضًا</label>
    </div>
    {error ? <TechnicalReadError message={error} reload={reload} /> : !data ? <LoadingState /> : <>
      {!data.items.length && <EmptyState message={all ? "لا توجد طلبات مراجعة ضمن نطاقك." : "لا توجد طلبات بانتظار القرار."} />}
      {data.items.map(entry => {
        const scope = data.contexts[entry.request.sportEnrollmentId];
        return <SectionCard key={entry.request.id} title={scope?.player ?? "طلب مراجعة"}>
          <p>{[scope?.branch, scope?.sport, entry.request.levelName].filter(Boolean).join(" · ")}</p>
          <DecisionText entry={entry} />
          <RequestMeta request={entry.request} />
          <Link className="button button-secondary" href={`${inbox}/${entry.request.id}${all ? withDecided : ""}`}>مراجعة الأدلة والقرار</Link>
        </SectionCard>;
      })}
      {data.hasMore && <p>تظهر أول 100 طلب حسب الأقدم.</p>}
    </>}
  </>;
}

export function TechnicalReviewDetail({ id }: { id: string }) {
  return <Suspense fallback={<LoadingState />}><ReviewDetail key={id} id={id} /></Suspense>;
}

function ReviewDetail({ id }: { id: string }) {
  const search = useSearchParams();
  const [revision, setRevision] = useState(0);
  const [message, setMessage] = useState("");
  const [outcome, setOutcome] = useState("");
  const [level, setLevel] = useState("");
  const [note, setNote] = useState("");
  const { data, error, reload } = useGoalData<DetailData>(`${root}/${id}`, revision, "طلب المراجعة غير متاح لك الآن.");
  const command = useTechnicalCommand(() => {
    setMessage("تم حفظ القرار ونشره لولي الأمر المرتبط.");
    setRevision(n => n + 1);
  });
  const ready = Boolean(data?.currentEligible && data.levels.length);
  const choices = ["NeedsPractice", ...(ready ? ["ReadyForPlacement"] : []), ...(data?.contextChanged ? ["ContextChanged"] : [])];
  const selected = choices.includes(outcome) ? outcome : "";
  const selectedLevel = data?.levels.some(item => item.id === level) ? level : "";
  const canSubmit = selected !== "" && (selected !== "ReadyForPlacement" || selectedLevel !== "");
  const locked = command.busy || command.needsRead;
  const plan = data ? `/dashboard/evaluations/skills/plans/${data.request.planId}` : "";
  const fromPlan = search.get("fromPlan") === "1" && Boolean(plan);

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!data?.canDecide || !canSubmit || locked) return;
    void command.save(`${root}/${id}/decision`, {
      version: data.request.version,
      outcome: selected,
      recommendedLevelId: selected === "ReadyForPlacement" ? selectedLevel : null,
      familyNote: note.trim() || null,
    });
  }

  const recovery = <>
    {command.error && <ErrorState message={command.error} />}
    {command.needsRead && <Button variant="secondary" onClick={() => command.check(reload)}>تحقّق من حالة المراجعة</Button>}
  </>;

  return <>
    <PageHeader title="قرار المراجعة الفنية" context="تطور اللاعب"
      back={fromPlan ? { href: `${plan}/technical-review`, label: "العودة إلى مراجعات الخطة" }
        : { href: inbox + (search.get("includeDecided") === "true" ? withDecided : ""), label: "العودة إلى طلبات المراجعة" }} />
    {error ? <TechnicalReadError message={error} reload={reload} /> : !data ? <LoadingState /> : <>
      <SectionCard title={`${data.enrollment.player} · ${data.request.levelName}`}>
        <p>{data.enrollment.sport} · {data.request.curriculumTitle}</p>
        <DecisionText entry={data} contextChanged={data.contextChanged} />
        <RequestMeta request={data.request} />
        {data.contextChanged && !data.decision && <p role="alert">تغيّرت الخطة أو الأدلة بعد إرسال الطلب. يلزم دليل حديث قبل التوصية بمستوى أعلى.</p>}
        <div className="form-actions">
          <Link className="button button-secondary" href={plan}>خطة المهارات وأحدث الملاحظات</Link>
          {data.canReviewPlacement && (data.application || !data.contextChanged) && <Link className={`button ${data.application ? "button-secondary" : "button-primary"}`} href={`${inbox}/${id}/promotion${fromPlan ? "?fromPlan=1" : search.get("includeDecided") === "true" ? withDecided : ""}`}>
            {data.application ? "سجل تنفيذ التوصية" : "معاينة وتنفيذ التوصية"}
          </Link>}
        </div>
      </SectionCard>
      <SectionCard title="الأدلة عند إرسال الطلب">
        <ul className={styles.curriculumRequirements}>
          {data.evidence.map((e, i) => <li key={i}>
            <h3>{e.name}</h3>
            <p>{e.measuredValue !== null ? <><bdi>{e.measuredValue}</bdi> {units[e.unit]}</> : "أتقن المهارة"}</p>
            <p className={styles.reviewMeta}>{e.actorName} · {formatDateAr(e.observedOn)}</p>
            {e.protocol && <p>{e.protocol}</p>}
          </li>)}
        </ul>
      </SectionCard>
      {message && <p role="status">{message}</p>}
      {!data.canDecide && recovery}
      {data.canDecide && <form className={`form-shell ${styles.reviewForm}`} onSubmit={save}>
        <h2>تسجيل القرار</h2>
        {data.currentEligible && data.levels.length === 0 && <p>لا يوجد مستوى أعلى مفعّل لهذه الرياضة.</p>}
        <label>قرار المراجعة<select value={selected} onChange={event => { setOutcome(event.target.value); setLevel(""); }} disabled={locked} required>
          <option value="">اختر القرار</option>
          <option value="NeedsPractice">{outcomes.NeedsPractice}</option>
          {ready && <option value="ReadyForPlacement">{outcomes.ReadyForPlacement}</option>}
          {data.contextChanged && <option value="ContextChanged">إغلاق لتغير الخطة أو الأدلة</option>}
        </select></label>
        {selected === "ReadyForPlacement" && <label>المستوى الموصى به<select value={selectedLevel} onChange={event => setLevel(event.target.value)} disabled={locked} required>
          <option value="">اختر مستوى أعلى لنفس الرياضة</option>
          {data.levels.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
        </select></label>}
        <label>ملخص فني للأسرة (اختياري)<textarea value={note} onChange={event => setNote(event.target.value)} maxLength={500} disabled={locked} /></label>
        <p>يظهر القرار والملخص لولي الأمر المرتبط. استبعد البيانات الصحية والملاحظات الإدارية الخاصة.</p>
        {selected === "ReadyForPlacement" && <p>يُنفَّذ تغيير المستوى في خطوة إدارية منفصلة.</p>}
        {recovery}
        <Button type="submit" disabled={locked || !canSubmit}>{command.busy ? "جارٍ حفظ القرار…" : "حفظ قرار المراجعة ونشر الملخص"}</Button>
      </form>}
    </>}
  </>;
}
