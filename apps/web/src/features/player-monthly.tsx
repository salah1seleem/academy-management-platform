"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { type MouseEvent, useEffect, useRef, useState } from "react";
import { FileDown } from "lucide-react";
import { Button, ContextHelp, EmptyState, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { GuardianShell } from "../components/guardian-shell";
import { apiProblemMessage } from "../lib/api-problem";
import { formatDateAr } from "../lib/formatters";
import { useGoalData } from "./player-goals";
import styles from "./player-monthly.module.css";

type Entry = { id: string; kind: string; on: string; title: string; state: string; value: number | null; unit: string | null; note: string | null; actor: string; planId: string | null };
type Report = {
  enrollment: { id: string; playerId: string; player: string; sport: string };
  enrollments: { id: string; sport: string; branch: string }[];
  enrollmentsHasMore: boolean;
  month: string;
  from: string;
  to: string;
  asOfDate: string;
  page: number;
  counts: { skills: number; badges: number; certificates: number };
  items: Entry[];
  hasMore: boolean;
};

const kinds: Record<string, string> = { Skill: "مراجعة مهارة", Badge: "شارة مجهود", Certificate: "شهادة إنجاز" };
const states: Record<string, string> = { Mastered: "أتقن", Learning: "قيد التعلم", NotAssessed: "لم يُقيّم", Revoked: "شارة مسحوبة", Granted: "شارة ممنوحة", Withdrawn: "شهادة مسحوبة", Issued: "شهادة صادرة" };
const units: Record<string, string> = { Checklist: "قائمة تحقق", Seconds: "ثانية", Metres: "متر", Count: "مرة", Encouragement: "نقاط تشجيعية غير مالية" };
const monthName = new Intl.DateTimeFormat("ar-EG", { month: "long", timeZone: "UTC" });
const months = Array.from({ length: 12 }, (_, index) => monthName.format(new Date(Date.UTC(2020, index, 1))));
const scopeHelp = "يجمع التقرير مراجعات المهارات والشارات والشهادات لهذا الاشتراك. تظهر السجلات المسحوبة بحالتها الحالية.";

function ReportFilters({ report, family, onApply }: { report: Report; family: boolean; onApply: (month: string, enrollment: string) => void }) {
  const [reportYear, reportMonth] = report.month.split("-").map(Number);
  const [currentYear, currentMonth] = report.asOfDate.split("-").map(Number);
  const [year, setYear] = useState(reportYear);
  const [month, setMonth] = useState(reportMonth);
  const options = report.enrollments.some(option => option.id === report.enrollment.id) ? report.enrollments : [
    { id: report.enrollment.id, sport: report.enrollment.sport, branch: "" },
    ...report.enrollments,
  ];
  return <form className={styles.monthlyFilters} onSubmit={event => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    onApply(`${year}-${String(month).padStart(2, "0")}`, String(form.get("enrollment") ?? report.enrollment.id));
  }}>
    <label>شهر التقرير<select value={month} onChange={event => setMonth(Number(event.target.value))}>
      {months.slice(0, year === currentYear ? currentMonth : 12).map((label, index) => <option value={index + 1} key={label}>{label}</option>)}
    </select></label>
    <label>سنة التقرير<select value={year} onChange={event => {
      const next = Number(event.target.value);
      setYear(next);
      if (next === currentYear) setMonth(value => Math.min(value, currentMonth));
    }}>
      {Array.from({ length: currentYear - 1899 }, (_, index) => currentYear - index).map(option => <option key={option} value={option}>{option}</option>)}
    </select></label>
    {family && options.length > 1 && <label className={styles.sportField}>التسجيل الرياضي<select name="enrollment" defaultValue={report.enrollment.id}>
      {options.map(option => <option key={option.id} value={option.id}>{[option.sport, option.branch].filter(Boolean).join(" · ")}</option>)}
    </select></label>}
    <Button type="submit">عرض التقرير</Button>
  </form>;
}

export function PlayerMonthly({ playerId, enrollmentId }: { playerId?: string; enrollmentId?: string }) {
  const params = useSearchParams();
  const family = Boolean(playerId);
  const profile = playerId ? `/guardian/children/${playerId}` : "";
  const base = family ? `/api/v1${profile}/monthly` : `/api/v1/evaluations/monthly/enrollments/${enrollmentId}`;
  const requestedMonth = params.get("month") ?? "";
  const month = /^(19\d\d|[2-9]\d{3})-(0[1-9]|1[0-2])$/.test(requestedMonth) ? requestedMonth : "";
  const requestedPage = Number(params.get("page") ?? 0);
  const page = Number.isInteger(requestedPage) && requestedPage >= 0 && requestedPage <= 1000 ? requestedPage : 0;
  const selected = family ? params.get("enrollmentId") ?? "" : "";
  const query = new URLSearchParams({ page: String(page) });
  if (month) query.set("month", month);
  if (selected) query.set("enrollmentId", selected);
  const { data, error, reload } = useGoalData<Report>(`${base}?${query}`, 0, "تعذر تحميل تقرير التطور.");
  const header = useRef<HTMLDivElement>(null);
  const exportHref = data ? `${base}?${new URLSearchParams({ enrollmentId: data.enrollment.id, month: data.month, format: "csv" })}` : "";
  const exportRequest = useRef<AbortController | null>(null);
  const [download, setDownload] = useState<{ href: string; state: "busy" | "error" | "started"; message?: string }>();

  useEffect(() => {
    const stopOnHide = () => { if (document.hidden) exportRequest.current?.abort(); };
    document.addEventListener("visibilitychange", stopOnHide);
    return () => {
      exportRequest.current?.abort();
      document.removeEventListener("visibilitychange", stopOnHide);
    };
  }, [exportHref]);

  async function exportMonth(event: MouseEvent<HTMLAnchorElement>) {
    event.preventDefault();
    if (!data || exportRequest.current || new URLSearchParams(window.location.search).toString() !== params.toString()) return;
    const controller = new AbortController();
    exportRequest.current = controller;
    setDownload({ href: exportHref, state: "busy" });
    const fallback = "تعذر تنزيل التقرير. أعد المحاولة.";
    try {
      const response = await fetch(exportHref, { cache: "no-store", signal: controller.signal });
      if (!response.ok) {
        const message = await apiProblemMessage(response, fallback);
        if (!controller.signal.aborted) setDownload({ href: exportHref, state: "error", message });
        return;
      }
      if (!response.headers.get("Content-Type")?.startsWith("text/csv")) throw new Error();
      const file = await response.blob();
      if (controller.signal.aborted || document.hidden) return;
      const objectUrl = URL.createObjectURL(file);
      const anchor = document.createElement("a");
      anchor.href = objectUrl;
      anchor.download = `player-development-${data.month}.csv`;
      anchor.click();
      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
      setDownload({ href: exportHref, state: "started" });
    } catch {
      if (!controller.signal.aborted) setDownload({ href: exportHref, state: "error", message: fallback });
    } finally {
      exportRequest.current = null;
      if (controller.signal.aborted) setDownload(previous => previous?.href === exportHref ? undefined : previous);
    }
  }

  const activeDownload = download?.href === exportHref ? download : undefined;

  function setScope(nextMonth: string, nextEnrollment: string, nextPage: number) {
    exportRequest.current?.abort();
    const url = new URL(window.location.href);
    if (nextMonth) url.searchParams.set("month", nextMonth); else url.searchParams.delete("month");
    if (nextPage) url.searchParams.set("page", String(nextPage)); else url.searchParams.delete("page");
    if (nextEnrollment) url.searchParams.set("enrollmentId", nextEnrollment); else url.searchParams.delete("enrollmentId");
    window.history.replaceState(null, "", url);
    header.current?.focus();
  }

  function source(entry: Entry, report: Report) {
    if (entry.kind === "Skill" && entry.planId) return { label: "فتح خطة المهارة", href: family ? `${profile}/skills/plans/${entry.planId}` : `/dashboard/evaluations/skills/plans/${entry.planId}` };
    if (entry.kind === "Certificate") return { label: "فتح الشهادة", href: family ? `${profile}/certificates/${entry.id}` : `/dashboard/evaluations/certificates/${entry.id}` };
    if (entry.kind === "Badge") return { label: "فتح سجل الشارات", href: family ? `${profile}/badges?enrollmentId=${report.enrollment.id}` : `/dashboard/evaluations/badges?enrollmentId=${report.enrollment.id}` };
    return null;
  }

  const body = <div className={family ? styles.monthlyFamily : styles.monthlyStaff}>
    <div ref={header} tabIndex={-1}>
      {family ? <header className={`guardian-page-header guardian-match-header ${styles.monthlyHeader}`}>
        <Link className="guardian-match-back" href={profile}>العودة إلى ملف اللاعب</Link>
        <h1>تقرير التطور الشهري</h1>
      </header> : <PageHeader title="تقرير التطور الشهري" context="التدريب والتطور"
        back={data?.enrollment.playerId ? { label: "العودة إلى ملف اللاعب", href: `/dashboard/players/${data.enrollment.playerId}?tab=development` } : undefined} />}
    </div>
    {error ? <section className={styles.monthlyPanel}>
      <ErrorState message={error} />
      <div className={styles.monthlyActions}>
        <Button variant="secondary" onClick={reload}>إعادة المحاولة</Button>
        {(month || selected || page > 0) && <Button variant="ghost" onClick={() => setScope("", family ? "" : enrollmentId ?? "", 0)}>عرض الشهر الحالي</Button>}
      </div>
    </section> : !data ? <LoadingState message="جارٍ تحميل التقرير…" /> : <>
      <section className={styles.monthlyPanel}>
        <h2>{data.enrollment.player}</h2>
        <p>{data.enrollment.sport} · {months[Number(data.month.slice(5)) - 1]} {data.month.slice(0, 4)}</p>
        <small>من {Number(data.from.slice(8, 10))} إلى {formatDateAr(data.to)}</small>
        <ReportFilters key={`${data.enrollment.id}:${data.month}`} report={data} family={family} onApply={(next, enrollment) => setScope(next, enrollment, 0)} />
        {data.enrollmentsHasMore && <small>تظهر أول 100 تسجيل رياضي.</small>}
      </section>
      <section className={styles.monthlyPanel}>
        <div className={styles.monthlyActions}>
          <a className="button button-secondary" href={exportHref} download
            aria-busy={activeDownload?.state === "busy" || undefined} onClick={event => void exportMonth(event)}>
            <FileDown aria-hidden size={18} />{activeDownload?.state === "busy" ? "جارٍ تجهيز الملف…" : "تنزيل CSV"}
          </a>
          <small>يشمل الملف الشهر كله. احتفظ به بشكل خاص، فالسحب اللاحق لا يغيّر نسخة سبق تنزيلها.</small>
        </div>
        {activeDownload?.state === "error" && <ErrorState message={activeDownload.message ?? "تعذر تنزيل التقرير."} />}
        {activeDownload?.state === "started" && <p role="status">بدأ تنزيل التقرير.</p>}
        {family ? <details><summary>عن التقرير</summary><p>{scopeHelp}</p></details> : <ContextHelp title="عن التقرير"><p>{scopeHelp}</p></ContextHelp>}
      </section>
      {data.items.length === 0 ? <section className={`${styles.monthlyPanel} ${styles.monthlyEmpty}`}>
        <EmptyState message={page > 0 ? "لا توجد نتائج في هذه الصفحة." : "لا توجد مراجعات أو شارات أو شهادات مسجلة لهذا الشهر."} />
      </section> : <>
        <dl className={styles.monthlyTotals}>
          <div><dt>مراجعات المهارات</dt><dd>{data.counts.skills}</dd></div>
          <div><dt>الشارات</dt><dd>{data.counts.badges}</dd></div>
          <div><dt>الشهادات</dt><dd>{data.counts.certificates}</dd></div>
        </dl>
        <div className={styles.monthlyTimeline}>{data.items.map(entry => {
          const detail = source(entry, data);
          return <SectionCard key={`${entry.kind}:${entry.id}`} title={entry.title}>
            <small>{kinds[entry.kind] ?? "سجل تطور"} · {formatDateAr(entry.on)}</small>
            <p><strong>{states[entry.state] ?? "راجع المصدر"}</strong>{entry.value != null && <> · <bdi>{entry.value}</bdi>{entry.unit && <> {units[entry.unit] ?? "حسب وحدة القياس في المصدر"}</>}</>}</p>
            {entry.note && <p>{entry.note}</p>}
            {entry.actor && <p>سجّله {entry.actor}</p>}
            {detail && <Link href={detail.href}>{detail.label}</Link>}
          </SectionCard>;
        })}</div>
      </>}
      {(page > 0 || data.hasMore) && <nav className={`${styles.monthlyPanel} ${styles.monthlyActions}`} aria-label="صفحات التقرير">
        <Button variant="secondary" disabled={page === 0} onClick={() => setScope(data.month, data.enrollment.id, page - 1)}>الصفحة السابقة</Button>
        <span>صفحة {page + 1}</span>
        <Button variant="secondary" disabled={!data.hasMore || page >= 1000} onClick={() => setScope(data.month, data.enrollment.id, page + 1)}>الصفحة التالية</Button>
      </nav>}

    </>}
  </div>;

  return family ? <GuardianShell>{body}</GuardianShell> : body;
}
