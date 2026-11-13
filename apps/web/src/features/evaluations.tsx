"use client";

import Link from "next/link";
import Image from "next/image";
import { FormEvent, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { AdminDataTable, ConfirmationDialog, EmptyState, ErrorState, ListSearch, LoadingState, PageHeader, RowActions, StatusBadge, type DataColumn } from "../components/dashboard-shell";
import { announceSaveSuccess, csrfRequest, DashboardRequestError } from "./dashboard-api";
import { formatDateAr } from "../lib/formatters";
import styles from "./evaluation-workflow.module.css";
import reportStyles from "./evaluation-report.module.css";
import { IdentityMark, ProgressTrack } from "../components/sports-ui";
import { evaluationListHref, evaluationListParams, evaluationListScope, evaluationRowAnchor, evaluationsPath, maxEvaluationSearchLength } from "./evaluation-list-query";

import { axisLabels } from "./evaluation-axes";

const statusLabels: Record<string, string> = { Draft: "مسودة", Published: "منشور", PublishedInternal: "للاستخدام الداخلي", Superseded: "مستبدل" };
const footLabels: Record<string, string> = { Right: "اليمنى", Left: "اليسرى", Both: "القدمان" };

type EnrollmentOption = { id: string; playerId: string; player: string; playerCode: string; sportId: string; sport: string; groupId: string | null; group: string; branch?: string; criteriaReady?: boolean };
type OptionData = { enrollments: EnrollmentOption[]; sports: { id: string; name: string }[]; canEvaluate?: boolean };

type EvaluationRow = { id: string; player: string; sport: string; group: string; evaluationDate: string; evaluator: string; status: string; overallScore?: number; scoredCriteria: number; totalApplicableCriteria: number; completenessPercentage: number; version: number };
export function EvaluationsList({ publishedOnly = false }: { publishedOnly?: boolean }) {
  const params = useSearchParams();
  const scope = evaluationListScope({
    playerId: params.get("playerId"), groupId: params.get("groupId"), search: params.get("search"),
    status: params.get("status"), page: params.get("page"), pageSize: params.get("pageSize"),
  });
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<{ request: string; rows?: EvaluationRow[]; error?: string; retry?: boolean }>();
  const apiQuery = evaluationListParams({ ...scope, page: 0, pageSize: 25 }, publishedOnly);
  if (publishedOnly) apiQuery.set("status", "Published");
  const path = `${evaluationsApi}?${apiQuery}`;
  const request = `${path}:${attempt}`;
  const current = result?.request === request ? result : undefined;
  const rows = current?.rows;
  const title = publishedOnly ? "التقارير المنشورة" : "تقييمات اللاعبين";
  const page = Math.min(scope.page, Math.max(0, Math.ceil((rows?.length ?? 0) / scope.pageSize) - 1));
  const focusRestored = useRef("");

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    const timer = setTimeout(() => {
      void evaluationGet<EvaluationRow[]>(path, "تعذر تحميل التقييمات.", signal).then(rows => {
        if (!Array.isArray(rows)) throw new Error("تعذر تحميل التقييمات.");
        if (!signal.aborted) setResult({ request, rows });
      }).catch(caught => {
        if (signal.aborted) return;
        const denied = caught instanceof DashboardRequestError && [401, 403, 404].includes(caught.status);
        setResult({ request, error: denied ? "التقييمات غير متاحة لحسابك." : "تعذر تحميل التقييمات.", retry: !denied });
      });
    }, 150);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [path, request]);

  useEffect(() => {
    const anchor = window.location.hash.slice(1);
    if (!rows || !anchor.startsWith("evaluation-") || focusRestored.current === anchor) return;
    const link = document.getElementById(anchor);
    if (link) {
      link.scrollIntoView({ block: "nearest" });
      link.focus({ preventScroll: true });
      focusRestored.current = anchor;
    }
  }, [rows, page, scope.pageSize]);

  function updateScope(change: Partial<typeof scope>) {
    focusRestored.current = "";
    window.history.replaceState(null, "", evaluationListHref({ ...scope, ...change }, publishedOnly));
  }

  function reportHref(id: string) {
    const query = evaluationListParams({ ...scope, page }, publishedOnly);
    query.set("from", publishedOnly ? "published" : "list");
    return `${evaluationsPath}/${encodeURIComponent(id)}/report?${query}`;
  }

  const columns: DataColumn<EvaluationRow>[] = [
    { key: "player", header: "اللاعب", primary: true, width: "minmax(9rem, 1.5fr)", render: row => <IdentityMark name={row.player} detail={row.sport} /> },
    { key: "group", header: "المجموعة", width: "minmax(8rem, 1.25fr)", render: row => row.group },
    { key: "date", header: "تاريخ التقييم", width: "minmax(6rem, .9fr)", render: row => formatDateAr(row.evaluationDate) },
    { key: "evaluator", header: "المقيّم", width: "minmax(6rem, .9fr)", render: row => row.evaluator },
    { key: "completion", header: "الاكتمال", width: "minmax(5rem, .75fr)", render: row => <div className="session-completion"><span>{row.scoredCriteria}/{row.totalApplicableCriteria}</span><ProgressTrack value={row.scoredCriteria} total={row.totalApplicableCriteria} label={`اكتمال تقييم ${row.player}`} />{row.overallScore != null && <small>الإجمالي {row.overallScore}</small>}</div> },
  ];
  if (!publishedOnly) columns.push({ key: "status", header: "الحالة", width: "minmax(4.5rem, .7fr)", render: row => <StatusBadge status={row.status} label={statusLabels[row.status]} /> });
  columns.push({
    key: "actions", header: "الإجراء", width: "minmax(6rem, .8fr)",
    render: row => {
      if (row.status === "Draft") {
        return <RowActions actions={[{ label: "عرض وتعديل", href: `${evaluationsPath}/${encodeURIComponent(row.id)}/edit` }]} />;
      }
      if (row.status === "Published" || row.status === "PublishedInternal") {
        return <Link id={evaluationRowAnchor(row.id)} className="row-primary-action" href={reportHref(row.id)}>عرض التقرير</Link>;
      }
      return "—";
    },
  });

  return <>
    <PageHeader title={title} context={`التقييمات / ${title}`} action={!publishedOnly ? { label: "تقييم جديد", href: `${evaluationsPath}/new` } : undefined} />
    <section className="list-card">
      <div className="player-filters">
        <ListSearch value={scope.search} onChange={search => updateScope({ search: search.slice(0, maxEvaluationSearchLength), page: 0 })} placeholder="اسم اللاعب أو الكود" />
        {!publishedOnly && <select aria-label="حالة التقييم" value={scope.status} onChange={event => updateScope({ status: event.target.value, page: 0 })}>
          <option value="">كل الحالات</option><option value="Draft">مسودة</option><option value="Published">منشور</option><option value="PublishedInternal">للاستخدام الداخلي</option>
        </select>}
      </div>
      {current?.error ? <>
        <ErrorState message={current.error} />
        {current.retry && <button type="button" className="button button-secondary" onClick={() => setAttempt(n => n + 1)}>إعادة المحاولة</button>}
      </> : !rows ? <LoadingState /> : rows.length === 0 ? <EmptyState
        title={publishedOnly ? "لا توجد تقارير منشورة" : "لا توجد تقييمات"}
        message={scope.search || scope.status ? "لا توجد نتائج تطابق الفلاتر الحالية." : undefined}
      /> : <AdminDataTable label={title} rows={rows.slice(page * scope.pageSize, (page + 1) * scope.pageSize)} rowKey={row => row.id} columns={columns}
        serverPagination={{ page, pageSize: scope.pageSize, total: rows.length, onPageChange: page => updateScope({ page }), onPageSizeChange: pageSize => updateScope({ pageSize, page: 0 }) }} />}
    </section>
  </>;
}

const evaluationsApi = "/api/v1/evaluations";
const evaluationBack = { href: evaluationsPath, label: "العودة إلى تقييمات اللاعبين" };
const reportingMonth = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { month: "long", year: "numeric", timeZone: "UTC" });

async function evaluationGet<T>(url: string, message: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { cache: "no-store", signal });
  if (!response.ok) {
    const denied = [401, 403, 404].includes(response.status);
    throw new DashboardRequestError(denied ? "هذه البيانات غير متاحة لحسابك." : message, response.status);
  }
  return response.json() as Promise<T>;
}

export function NewEvaluation({ initialEnrollment = "" }: { initialEnrollment?: string }) {
  const router = useRouter();
  const [setup, setSetup] = useState<{ options: OptionData; today: string }>();
  const [retry, setRetry] = useState(0);
  const [loadError, setLoadError] = useState("");
  const [sport, setSport] = useState("");
  const [group, setGroup] = useState("");
  const [selection, setSelection] = useState(initialEnrollment.toLowerCase());
  const [date, setDate] = useState<string | null>(null);
  const [period, setPeriod] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [uncertain, setUncertain] = useState(false);
  const [error, setError] = useState("");
  const writing = useRef(false);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    Promise.all([
      evaluationGet<OptionData>(evaluationsApi + "/options", "تعذر تحميل تسجيلات اللاعبين.", signal),
      evaluationGet<{ today: string }>("/api/v1/academy/calendar", "تعذر تحميل تاريخ الأكاديمية.", signal),
    ]).then(([options, calendar]) => {
      if (signal.aborted) return;
      if (!Array.isArray(options.enrollments) || !Array.isArray(options.sports) || !/^\d{4}-\d{2}-\d{2}$/.test(calendar.today)) {
        throw new Error();
      }
      setSetup({ options, today: calendar.today });
    }).catch(caught => {
      if (!signal.aborted) setLoadError(caught instanceof DashboardRequestError ? caught.message : "تعذر تجهيز نموذج التقييم.");
    });
    return () => controller.abort();
  }, [retry]);

  const options = setup?.options;
  const enrollments = options?.enrollments ?? [];
  const matchesSport = enrollments.filter(row => !sport || row.sportId === sport);
  const groups = [...new Map(matchesSport.flatMap(row => row.groupId ? [[row.groupId, row.group] as const] : [])).entries()];
  const visible = matchesSport.filter(row => !group || row.groupId === group);
  const selected = visible.find(row => row.id.toLowerCase() === selection);
  const day = date ?? setup?.today ?? "";
  const periodDate = new Date(day + "T00:00:00Z");
  const effectivePeriod = period ?? (Number.isNaN(periodDate.valueOf()) ? "" : reportingMonth.format(periodDate));
  const allowed = options?.canEvaluate !== false;
  const ready = !!selected && selected.criteriaReady !== false && !!effectivePeriod.trim() && !!day;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!setup || !selected || !allowed || !ready || uncertain || writing.current) return;
    const notes = new FormData(event.currentTarget).get("generalNotes");
    writing.current = true;
    setBusy(true);
    setError("");
    try {
      const response = await csrfRequest(evaluationsApi, "POST", {
        sportEnrollmentId: selected.id, evaluationDate: day, reportingPeriod: effectivePeriod.trim(),
        generalNotes: typeof notes === "string" ? notes.trim() || null : null,
      }, {}, { announce: false });
      const created = await response.json() as { id?: string };
      if (!created.id || !/^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(created.id)) throw new Error();
      router.push(evaluationsPath + "/" + created.id + "/edit");
      return;
    } catch (caught) {
      if (caught instanceof DashboardRequestError && caught.status >= 400 && caught.status < 500 && caught.status !== 408) {
        setError(caught.message);
      } else {
        setUncertain(true);
        setError("لم يتأكد إنشاء المسودة. راجع تقييمات اللاعب قبل إنشاء مسودة أخرى.");
      }
      writing.current = false;
      setBusy(false);
    }
  }

  return <>
    <PageHeader title="تقييم جديد" context="تقييمات اللاعبين" back={evaluationBack} />
    {loadError ? <section className="form-shell">
      <ErrorState message={loadError} />
      <button className="secondary-button" type="button" onClick={() => { setLoadError(""); setRetry(value => value + 1); }}>إعادة المحاولة</button>
    </section> : !setup ? <LoadingState /> : !allowed ? <ErrorState message="لا تملك صلاحية إنشاء تقييم." />
      : initialEnrollment && !selected ? <section className="form-shell">
        <ErrorState message="تسجيل اللاعب غير متاح للتقييم." />
        <Link className="button button-secondary" href={evaluationsPath + "/new"}>اختيار لاعب آخر</Link>
      </section> : enrollments.length === 0 ? <EmptyState title="لا توجد تسجيلات متاحة للتقييم" message="اختر لاعبًا له تسجيل نشط ضمن نطاقك." />
      : <form className="form-shell" onSubmit={event => void submit(event)} aria-busy={busy || undefined}>
        <fieldset className={styles.evaluationFields} disabled={busy || uncertain}>
          {initialEnrollment && selected ? <IdentityMark name={selected.player} detail={[selected.sport, selected.group, selected.branch].filter(Boolean).join(" · ")} />
            : <>
              <div className={styles.evaluationFilters}>
                <label>الرياضة<select value={sport} onChange={event => { setSport(event.target.value); setGroup(""); setSelection(""); }}>
                  <option value="">كل الرياضات</option>
                  {options?.sports.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
                </select></label>
                <label>المجموعة<select value={group} onChange={event => { setGroup(event.target.value); setSelection(""); }}>
                  <option value="">كل المجموعات</option>
                  {groups.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
                </select></label>
              </div>
              <label>اللاعب<select name="enrollmentId" value={selection} required onChange={event => { setSelection(event.target.value); setError(""); }}>
                <option value="">{visible.length ? "اختر اللاعب" : "لا توجد تسجيلات مطابقة"}</option>
                {visible.map(item => <option key={item.id} value={item.id.toLowerCase()}>{item.player} · {item.playerCode} · {item.sport} · {item.group}</option>)}
              </select></label>
            </>}
          {selected?.criteriaReady === false && <ErrorState message="جهّز معايير هذه الرياضة قبل بدء التقييم." />}
          <label>تاريخ التقييم<input name="evaluationDate" type="date" value={day} onChange={event => setDate(event.target.value)} required /></label>
          <label>فترة التقرير<input name="reportingPeriod" value={effectivePeriod} onChange={event => setPeriod(event.target.value)} required /></label>
          <label>ملاحظات المدرب (اختياري)<textarea name="generalNotes" rows={3} /></label>
        </fieldset>
        {error && <ErrorState message={error} />}
        {uncertain && selected ? <Link className="button button-secondary" href={`${evaluationsPath}?playerId=${encodeURIComponent(selected.playerId)}`}>عرض تقييمات اللاعب</Link>
          : <div className="form-actions"><button className="primary-button" type="submit" disabled={busy || !ready}>{busy ? "جارٍ إنشاء المسودة…" : "إنشاء المسودة"}</button></div>}
      </form>}
  </>;
}

type EvaluationDetails = {
  id: string; sportEnrollmentId: string; player: string; sport: string; group: string;
  evaluationDate: string; reportingPeriod: string; status: string; generalNotes?: string | null; version: number;
  scores: { criterionId: string; name: string; score?: number | null; notes?: string | null; axis?: string | null }[];
};
type DraftScore = { criterionId: string; score: number | null; notes: string | null };
type DraftContent = { reportingPeriod: string; generalNotes: string | null; scores: DraftScore[] };
type PendingEvaluation = { draft: DraftContent; status: "Draft" | "Published" | "PublishedInternal" };

function matchesEvaluation(saved: EvaluationDetails, expected: PendingEvaluation) {
  if (saved.status !== expected.status || saved.reportingPeriod !== expected.draft.reportingPeriod ||
      (saved.generalNotes ?? null) !== expected.draft.generalNotes || saved.scores.length !== expected.draft.scores.length) return false;
  const scores = new Map(saved.scores.map(score => [score.criterionId, score]));
  return expected.draft.scores.every(expectedScore => {
    const actual = scores.get(expectedScore.criterionId);
    return actual && (actual.score ?? null) === expectedScore.score && (actual.notes ?? null) === expectedScore.notes;
  });
}

function evaluationScore(text: string) {
  const digits = text.trim()
    .replace(/[٠-٩]/g, digit => String(digit.charCodeAt(0) - 0x660))
    .replace(/[۰-۹]/g, digit => String(digit.charCodeAt(0) - 0x6f0));
  return digits === "" ? null : /^\d+$/.test(digits) && Number(digits) <= 100 ? Number(digits) : NaN;
}

export function EvaluationEditor({ id }: { id: string }) {
  const router = useRouter();
  const path = evaluationsApi + "/" + encodeURIComponent(id);
  const [data, setData] = useState<EvaluationDetails | null>(null);
  const [scores, setScores] = useState<Record<string, string>>({});
  const [notes, setNotes] = useState<Record<string, string>>({});
  const [general, setGeneral] = useState("");
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [dialog, setDialog] = useState<"family" | "internal" | "reload" | null>(null);
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState<PendingEvaluation | null>(null);
  const working = useRef(false);
  const current = useRef<AbortController | null>(null);
  const checkButton = useRef<HTMLButtonElement>(null);

  const draft: DraftContent | null = data && {
    reportingPeriod: data.reportingPeriod,
    generalNotes: general.trim() || null,
    scores: data.scores.map(item => ({
      criterionId: item.criterionId, score: evaluationScore(scores[item.criterionId] ?? ""), notes: notes[item.criterionId]?.trim() || null,
    })),
  };
  const invalid = draft?.scores.some(item => Number.isNaN(item.score)) ?? false;
  const assessed = draft?.scores.filter(item => item.score !== null && !Number.isNaN(item.score)).length ?? 0;
  const readOnly = data !== null && data.status !== "Draft";
  const locked = busy || pending !== null || readOnly;

  function apply(item: EvaluationDetails) {
    setData(item);
    setGeneral(item.generalNotes ?? "");
    setScores(Object.fromEntries(item.scores.map(score => [score.criterionId, score.score == null ? "" : String(score.score)])));
    setNotes(Object.fromEntries(item.scores.map(score => [score.criterionId, score.notes ?? ""])));
  }

  useEffect(() => {
    const controller = new AbortController();
    current.current = controller;
    readEvaluation(path, id, controller.signal).then(item => {
      if (!controller.signal.aborted) apply(item);
    }).catch(caught => {
      if (!controller.signal.aborted) setError(caught instanceof DashboardRequestError ? caught.message : "تعذر تحميل التقييم.");
    });
    return () => controller.abort();
  }, [id, path]);

  useEffect(() => {
    if (pending && !busy) checkButton.current?.focus();
  }, [pending, busy]);

  async function run(action: (signal: AbortSignal) => Promise<void>) {
    const signal = current.current?.signal;
    if (!signal || signal.aborted || working.current) return;
    working.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await action(signal);
    } catch (caught) {
      if (signal.aborted) return;
      if (caught instanceof DashboardRequestError && [401, 403, 404].includes(caught.status)) {
        setData(null);
        setPending(null);
      }
      setError(caught instanceof DashboardRequestError ? caught.message : "لم تتأكد نتيجة العملية. تحقّق من التقييم قبل إعادة المحاولة.");
    } finally {
      working.current = false;
      if (!signal.aborted) { setBusy(false); setDialog(null); }
    }
  }

  async function read(signal: AbortSignal) {
    const next = await readEvaluation(path, id, signal);
    if (data && next.sportEnrollmentId !== data.sportEnrollmentId) {
      throw new DashboardRequestError("هذا التقييم لا يخص تسجيل اللاعب المفتوح.", 404);
    }
    return next;
  }

  async function verify(expected: PendingEvaluation, signal: AbortSignal) {
    const next = await read(signal);
    if (signal.aborted) return next;
    if (!matchesEvaluation(next, expected)) throw new DashboardRequestError("لم تتطابق النسخة المحفوظة مع التغييرات المطلوبة.", 409);
    apply(next);
    setPending(null);
    return next;
  }

  async function write(url: string, method: "PUT" | "POST", body: object, signal: AbortSignal) {
    try {
      await csrfRequest(url, method, body, {}, { announce: false });
    } catch (caught) {
      if (!signal.aborted && caught instanceof DashboardRequestError && [400, 422].includes(caught.status)) setPending(null);
      throw caught;
    }
  }

  function confirm(expected: PendingEvaluation) {
    const text = expected.status === "Draft" ? "تم حفظ المسودة." : expected.status === "Published" ? "تم نشر التقييم لولي الأمر." : "تم اعتماد التقييم للاستخدام الداخلي.";
    setMessage(text);
    announceSaveSuccess(text);
    if (expected.status !== "Draft") router.push(evaluationsPath + "/" + id + "/report");
  }

  function submit(audience?: "family" | "internal") {
    if (!data || !draft || invalid || locked || (audience && assessed === 0)) return;
    const snapshot = data;
    const savedDraft: PendingEvaluation = { draft, status: "Draft" };
    void run(async signal => {
      let saved = snapshot;
      if (!audience || !matchesEvaluation(snapshot, savedDraft)) {
        setPending(savedDraft);
        await write(path, "PUT", { ...draft, version: snapshot.version }, signal);
        saved = await verify(savedDraft, signal);
      }
      if (signal.aborted) return;
      if (!audience) { confirm(savedDraft); return; }

      const expected: PendingEvaluation = { draft, status: audience === "family" ? "Published" : "PublishedInternal" };
      setPending(expected);
      await write(path + "/publish", "POST", { version: saved.version, shareWithGuardian: audience === "family" }, signal);
      await verify(expected, signal);
      if (!signal.aborted) confirm(expected);
    });
  }

  function reload() {
    void run(async signal => {
      const next = await read(signal);
      if (!signal.aborted) { apply(next); setPending(null); }
    });
  }

  return <>
    <PageHeader title={data ? `تقييم ${data.player}` : "تقييم اللاعب"} context="تقييمات اللاعبين" back={evaluationBack} />
    {!data ? error ? <section className="form-shell"><ErrorState message={error} /><button className="secondary-button" type="button" disabled={busy} onClick={reload}>إعادة المحاولة</button></section> : <LoadingState />
      : <>
        <section className={styles.evaluationSummary} aria-label="بيانات التقييم">
          <div><b>{data.sport} · {data.group}</b><span>{formatDateAr(data.evaluationDate)} · {data.reportingPeriod}</span></div>
          <StatusBadge status={data.status} label={statusLabels[data.status]} />
        </section>
        {readOnly ? <section className="form-shell">
          {data.status === "Published" || data.status === "PublishedInternal"
            ? <Link className="button button-primary" href={evaluationsPath + "/" + id + "/report"}>عرض التقرير</Link>
            : <p>لا يمكن تعديل هذا التقييم.</p>}
        </section> : <section className="evaluation-editor" aria-busy={busy || undefined}>
          <fieldset className={styles.evaluationFields} disabled={locked}>
            <div className="score-grid">
              {data.scores.map(item => {
                const value = scores[item.criterionId] ?? "";
                const badScore = Number.isNaN(evaluationScore(value));
                const scoreError = `score-error-${item.criterionId}`;
                return <article className={`score-card ${styles.criterion}`} key={item.criterionId}>
                  <div><h3>{item.name}</h3>{item.axis && axisLabels[item.axis] && <small>{axisLabels[item.axis]}</small>}</div>
                  <label>الدرجة من 100<input aria-label={`درجة ${item.name}`} inputMode="numeric" dir="ltr" value={value} aria-invalid={badScore || undefined} aria-describedby={badScore ? scoreError : undefined} onChange={event => setScores({ ...scores, [item.criterionId]: event.target.value })} /></label>
                  {badScore && <small id={scoreError} role="alert">أدخل عددًا صحيحًا من 0 إلى 100.</small>}
                  <details open={!!(item.notes || notes[item.criterionId]) || undefined}>
                    <summary>ملاحظة المعيار</summary>
                    <textarea aria-label={`ملاحظة ${item.name}`} rows={2} value={notes[item.criterionId] ?? ""} onChange={event => setNotes({ ...notes, [item.criterionId]: event.target.value })} />
                  </details>
                </article>;
              })}
            </div>
            <label className="field">ملاحظات المدرب (اختياري)<textarea value={general} rows={3} onChange={event => setGeneral(event.target.value)} /></label>
          </fieldset>
          <div className={styles.evaluationActions}>
            <p role="status">{message || `الدرجات المسجّلة ${assessed} من ${data.scores.length}`}</p>
            {error && <p className={styles.evaluationError} role="alert">{error}</p>}
            {pending ? <div className={styles.evaluationButtons}>
              <button ref={checkButton} className="primary-button" type="button" disabled={busy} onClick={() => void run(async signal => { await verify(pending, signal); if (!signal.aborted) confirm(pending); })}>التحقق من التقييم</button>
              <button className="secondary-button" type="button" disabled={busy} onClick={() => setDialog("reload")}>عرض النسخة المحفوظة</button>
            </div> : <>
              {assessed === 0 && <small>أدخل درجة واحدة على الأقل للنشر.</small>}
              <div className={styles.evaluationButtons}>
                <button className="primary-button" type="button" disabled={locked || invalid || !assessed} onClick={() => setDialog("family")}>نشر لولي الأمر</button>
                <button className="secondary-button" type="button" disabled={locked || invalid || !assessed} onClick={() => setDialog("internal")}>اعتماد داخلي</button>
                <button className="secondary-button" type="button" disabled={locked || invalid} onClick={() => submit()}>{busy ? "جارٍ الحفظ…" : "حفظ المسودة"}</button>
              </div>
            </>}
          </div>
        </section>}
      </>}
    <ConfirmationDialog open={dialog !== null}
      title={dialog === "reload" ? "عرض النسخة المحفوظة" : dialog === "internal" ? "اعتماد التقييم داخليًا" : "نشر التقييم لولي الأمر"}
      message={dialog === "reload" ? "ستُستبدل القيم المفتوحة بالنسخة المحفوظة. لن يُرسل أي تعديل." : dialog === "internal" ? "سيبقى التقييم متاحًا لفريق العمل فقط ولن يمكن تعديله بعد الاعتماد." : "سيظهر التقييم المنشور لولي الأمر ولن يمكن تعديله بعد النشر."}
      confirmLabel={dialog === "reload" ? "عرض النسخة" : dialog === "internal" ? "تأكيد الاعتماد" : "تأكيد النشر"}
      tone="primary" busy={busy} onConfirm={() => { if (dialog === "reload") reload(); else if (dialog) submit(dialog); }} onCancel={() => setDialog(null)} />
  </>;
}

async function readEvaluation(path: string, id: string, signal: AbortSignal) {
  const data = await evaluationGet<EvaluationDetails>(path, "تعذر تحميل التقييم.", signal);
  if (data.id?.toLowerCase() !== id.toLowerCase() || !data.sportEnrollmentId ||
      !Array.isArray(data.scores) || !Number.isInteger(data.version) || data.version < 0) {
    throw new DashboardRequestError("تعذر التحقق من التقييم المطلوب.", 404);
  }
  return data;
}

type Report = {
  id: string;
  player: string;
  sport: string;
  evaluationDate: string;
  reportingPeriod: string;
  evaluator: string;
  photoReference?: string | null;
  footballPosition?: string | null;
  generalNotes?: string | null;
  age: number;
  heightCm?: number | null;
  weightKg?: number | null;
  preferredFoot?: string | null;
  isFootballReport: boolean;
  overallScore?: number | null;
  scoredCriteria: number;
  totalApplicableCriteria: number;
  completenessPercentage: number;
  availableAxes: number;
  performanceMean?: { value: number | null; scoredCriteria: number; applicableCriteria: number; coveragePercent: number };
  axes: { axis: string; value?: number | null }[];
  criteria: { name: string; score?: number | null; notes?: string | null; axis?: string | null }[];
};

export function EvaluationReport({ id, guardian = false, childId, staffBack = evaluationBack }: {
  id: string; guardian?: boolean; childId?: string; staffBack?: typeof evaluationBack;
}) {
  const [attempt, setAttempt] = useState(0);
  const path = `/api/v1/${guardian ? "guardian/" : ""}evaluations/${encodeURIComponent(id)}/report`;
  const request = `${path}:${attempt}`;
  const [result, setResult] = useState<{ request: string; report?: Report; error?: string; retry?: boolean }>();
  useEffect(() => {
    const controller = new AbortController();
    const unavailable = "تعذر تحميل التقرير أو أنه غير متاح لك.";
    async function load() {
      try {
        const response = await fetch(path, { cache: "no-store", signal: controller.signal });
        if (controller.signal.aborted) return;
        if (!response.ok) {
          const denied = [401, 403, 404].includes(response.status);
          setResult({ request, error: denied ? "هذا التقرير غير متاح لحسابك." : unavailable, retry: !denied });
          return;
        }
        const report: Report = await response.json();
        if (controller.signal.aborted) return;
        if (typeof report.id !== "string" || report.id.toLowerCase() !== id.toLowerCase()
          || typeof report.player !== "string" || !Array.isArray(report.axes) || !Array.isArray(report.criteria)) {
          setResult({ request, error: unavailable, retry: true });
          return;
        }
        setResult({ request, report });
      } catch {
        if (!controller.signal.aborted) setResult({ request, error: unavailable, retry: true });
      }
    }
    void load();
    return () => controller.abort();
  }, [id, path, request]);

  const current = result?.request === request ? result : undefined;
  const content = current?.error ? <>
    <ErrorState message={current.error} />
    {current.retry && <button type="button" className="button button-secondary" onClick={() => setAttempt(n => n + 1)}>إعادة المحاولة</button>}
  </> : current?.report
    ? <PlayerReport report={current.report} staff={!guardian} />
    : <LoadingState message="جارٍ تحميل تقرير اللاعب…" />;

  if (!guardian) return <>
    <PageHeader title="تقرير اللاعب" context="التقييمات / تقرير اللاعب" back={staffBack} />
    {content}
  </>;
  const backHref = childId ? `/guardian/children/${encodeURIComponent(childId)}#evaluations` : "/guardian#children";
  return <div className={reportStyles.page}>
    <header className={reportStyles.header}>
      <Link href={backHref}>{childId ? "العودة إلى ملف اللاعب" : "العودة إلى الأبناء"}</Link>
      <h1>تقرير اللاعب</h1>
    </header>
    {content}
  </div>;
}

function ReportAvatar({ report }: { report: Report }) {
  return report.photoReference
    ? <Image className="player-avatar" src={report.photoReference} alt={`صورة ${report.player}`} width={64} height={64} />
    : <div className="player-avatar" aria-hidden="true">{report.player.slice(0, 1)}</div>;
}

function PlayerReport({ report, staff }: { report: Report; staff: boolean }) {
  const { performanceMean, overallScore, isFootballReport, footballPosition } = report;
  const axes = isFootballReport ? Object.keys(axisLabels).flatMap(axis => {
    const value = report.axes.find(item => item.axis === axis)?.value;
    return validAssessment(value) ? [{ axis, value }] : [];
  }) : [];
  const hasFullChart = axes.length === Object.keys(axisLabels).length;
  const criteria = staff ? report.criteria : report.criteria.filter(item => validAssessment(item.score));
  const coverage = performanceMean?.applicableCriteria ?? report.totalApplicableCriteria;
  const scored = performanceMean?.scoredCriteria ?? report.scoredCriteria;
  const date = new Date(report.evaluationDate);
  const period = report.reportingPeriod?.trim();
  const customPeriod = period && (Number.isNaN(date.valueOf()) || period !== reportingMonth.format(date));

  return <article className={staff ? `${reportStyles.report} ${reportStyles.staff}` : reportStyles.report}>
    <section className={reportStyles.summary} aria-label="ملخص التقييم">
      <div className={reportStyles.identity}>
        <ReportAvatar report={report} />
        <div>
          <h2>{report.player}</h2>
          <p>{report.sport}{isFootballReport && footballPosition ? ` · ${footballPosition}` : ""}</p>
        </div>
      </div>
      <p className={reportStyles.meta}>{formatDateAr(report.evaluationDate)} · {report.evaluator}</p>
      {customPeriod && <p className={reportStyles.meta}>{period}</p>}
      <dl className={reportStyles.scores}>
        <div><dt>متوسط الأداء</dt><dd>{validAssessment(performanceMean?.value) ? <><bdi>{performanceMean.value}</bdi><small> من 100</small></> : "غير متاح"}</dd></div>
        {validAssessment(overallScore) && <div><dt>التقييم المركب</dt><dd><bdi>{overallScore}</bdi></dd></div>}
      </dl>
      {coverage > 0 && <p className={reportStyles.coverage}>الدرجات المسجّلة <bdi>{scored} من {coverage}</bdi></p>}
    </section>

    {axes.length > 0 && <section className={reportStyles.section} aria-labelledby="report-axes">
      <h2 id="report-axes">محاور الأداء</h2>
      <div className={hasFullChart ? reportStyles.chartLayout : undefined}>
        {hasFullChart && <RadarChart axes={axes} />}
        <dl className={reportStyles.axes} aria-label="القيم النصية للمحاور">
          {axes.map(item => <div key={item.axis}><dt>{axisLabels[item.axis]}</dt><dd><bdi>{item.value}</bdi></dd></div>)}
        </dl>
      </div>
    </section>}

    <ReportMeasurements report={report} showFoot={isFootballReport} />

    <section className={reportStyles.section} aria-labelledby="report-criteria">
      <h2 id="report-criteria">الدرجات حسب المعيار</h2>
      {criteria.length ? <ul className={reportStyles.criteria}>
        {criteria.map((item, index) => <li key={`${item.name}-${index}`}>
          <div>
            <h3>{item.name}</h3>
            {validAssessment(item.score)
              ? <strong><bdi dir="ltr">{item.score} / 100</bdi></strong>
              : <span className={reportStyles.unscored}>لم يُقيّم</span>}
          </div>
          {staff && isFootballReport && item.axis && axisLabels[item.axis] && <small>{axisLabels[item.axis]}</small>}
          {item.notes?.trim() && <p>{item.notes}</p>}
        </li>)}
      </ul> : <p className={reportStyles.empty}>{staff ? "لا توجد معايير في هذا التقرير." : "لا توجد درجات منشورة في هذا التقرير."}</p>}
    </section>
    {report.generalNotes?.trim() && <section className={`${reportStyles.section} ${reportStyles.notes}`} aria-labelledby="report-notes">
      <h2 id="report-notes">{staff ? "ملاحظات المقيّم" : "ملاحظات المدرب"}</h2>
      <p>{report.generalNotes}</p>
    </section>}
  </article>;
}

function ReportMeasurements({ report, showFoot }: { report: Report; showFoot: boolean }) {
  const age = report.age === 1 ? "سنة واحدة" : report.age === 2 ? "سنتان"
    : `${report.age} ${report.age >= 3 && report.age <= 10 ? "سنوات" : "سنة"}`;
  return <div className="info-cards">
    <article><span>العمر</span><b>{age}</b></article>
    <article><span>الطول</span><b>{report.heightCm ? `${report.heightCm} سم` : "غير مسجل"}</b></article>
    <article><span>الوزن</span><b>{report.weightKg ? `${report.weightKg} كجم` : "غير مسجل"}</b></article>
    {showFoot && <article><span>القدم المفضلة</span><b>{(report.preferredFoot && footLabels[report.preferredFoot]) || "غير مسجلة"}</b></article>}
  </div>;
}

export function validAssessment(value: unknown): value is number { return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= 100; }

function RadarChart({ axes }: { axes: { axis: string; value: number }[] }) {
  const point=(index:number,level:number)=>({x:100+Math.cos(-Math.PI/2+index*Math.PI/3)*82*level/100,y:100+Math.sin(-Math.PI/2+index*Math.PI/3)*82*level/100});
  const points = (level: (index: number) => number) => axes.map((_, i) => {
    const { x, y } = point(i, level(i));
    return `${x},${y}`;
  }).join(" ");
  return <figure className={reportStyles.radar}><svg viewBox="-43 -5 286 210" role="img" aria-label="مخطط محاور أداء كرة القدم"><title>المخطط السداسي لأداء اللاعب</title>
    {[25,50,75,100].map(level=><polygon key={level} points={points(() => level)} className="radar-ring"/>)}
    {axes.map(({ axis }, index) => {
      const { x, y } = point(index, 100);
      const side = index === 0 || index === 3 ? 0 : index < 3 ? 1 : -1;
      return <g key={axis}>
        <line x1="100" y1="100" x2={x} y2={y} className="radar-line" />
        <text aria-hidden="true" direction="ltr" className={reportStyles.chartLabel} x={x + side * 6}
          y={y + (index === 0 ? -7 : index === 3 ? 16 : 4)}
          textAnchor={side === 0 ? "middle" : side === 1 ? "start" : "end"}>{axisLabels[axis]}</text>
      </g>;
    })}
    <polygon points={points(i => axes[i].value)} className="radar-value"/>
  </svg></figure>;
}

export function GuardianEvaluationHistory({ playerId }: { playerId: string }) { const [rows, setRows] = useState<{ id: string; sport: string; evaluationDate: string; evaluator: string; overallScore?: number; completenessPercentage: number }[]>([]); useEffect(() => { fetch(`/api/v1/guardian/children/${playerId}/evaluations`).then(r => r.ok ? r.json() : []).then(setRows); }, [playerId]); return <section className="guardian-evaluations"><h3>تقرير اللاعب</h3>{rows.length === 0 ? <small>لا توجد تقييمات منشورة بعد.</small> : rows.map(row => <p key={row.id}><span>{row.sport} · {row.evaluationDate} · {row.evaluator}</span><Link href={`/guardian/evaluations/${row.id}`}>عرض التقرير{row.overallScore != null ? ` · ${row.overallScore}` : ""}</Link></p>)}</section>; }
