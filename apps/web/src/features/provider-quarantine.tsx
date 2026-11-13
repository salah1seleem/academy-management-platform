"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { useDateFilterDefaults, type DateRange } from "../components/date-filter-defaults";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge } from "../components/dashboard-shell";
import { formatMoneyAr } from "../lib/formatters";
import styles from "./provider-quarantine.module.css";

const root = "/api/v1/finance/provider-quarantine";
const route = "/dashboard/finance/provider-quarantine";
const ownerOnly = "متابعة اختلافات الدفع متاحة للمالك فقط.";
const unavailable = "تعذر تحميل سجل اختلافات الدفع.";

export type ProviderIssue = {
  id: string; paymentRequestId: string; provider: string; providerEventId: string;
  reasonCode: string; receivedStatus: string; receivedReference: string; expectedReference: string;
  receivedAmount: string; expectedAmount: string; receivedCurrency: string; expectedCurrency: string;
  createdAtUtc: string; paymentStatus: string; player: string; branch: string; receiptId?: string;
};

type Scope = DateRange & { page: number; pageSize: number };
type IssuePage = { total: number; page: number; pageSize: number; items: ProviderIssue[] };
type ReadFailure = { status: number; message: string; retryable: boolean };

const reasons: Record<string, string> = {
  "amount-mismatch": "المبلغ مختلف", "currency-mismatch": "العملة مختلفة",
  "reference-mismatch": "مرجع الدفع مختلف", "provider-mismatch": "مصدر الدفع مختلف",
  "request-terms-mismatch": "تفاصيل الطلب غير متطابقة", "event-reuse": "رقم الحدث مستخدم بتفاصيل أخرى",
  "unsupported-status": "حالة الإشارة غير معروفة", "registration-format-conflict": "نوع التسجيل غير متوافق",
};
const paymentStates: Record<string, string> = {
  Pending: "بانتظار تأكيد الدفع", Confirmed: "تم تأكيد الطلب", Failed: "لم يتم الدفع",
  Cancelled: "ملغى", Expired: "انتهت صلاحية الطلب", Created: "طلب جديد",
};
const signalStates: Record<string, string> = { succeeded: "أبلغت الإشارة بنجاح الدفع", failed: "أبلغت الإشارة بفشل الدفع", cancelled: "أبلغت الإشارة بإلغاء الدفع" };
const issueClock = new Intl.DateTimeFormat("ar-EG-u-nu-latn", {
  timeZone: "Africa/Cairo", year: "numeric", month: "short", day: "numeric", hour: "numeric", minute: "2-digit",
});

export const issueReason = (code: string) => reasons[code] ?? "اختلاف يتطلب المتابعة";
const amount = (value: string, currency: string) => formatMoneyAr(value, currency, true);

function issueTime(value: string) {
  const time = new Date(value);
  return Number.isFinite(time.valueOf()) ? issueClock.format(time) : "وقت الرصد غير متاح";
}

function listScope(search: Pick<URLSearchParams, "get">, defaults: DateRange): Scope {
  function date(key: keyof DateRange) {
    const value = search.get(key);
    if (value === "") return value;
    if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return defaults[key];
    const parsed = new Date(`${value}T00:00:00Z`);
    return Number.isFinite(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value ? value : defaults[key];
  }
  const page = Number(search.get("page"));
  const size = Number(search.get("pageSize"));
  return {
    from: date("from"), to: date("to"), page: Number.isInteger(page) && page >= 1 && page <= 100000 ? page : 1,
    pageSize: [10, 25, 50].includes(size) ? size : 25,
  };
}

const scopeQuery = (scope: Scope) => new URLSearchParams({ from: scope.from, to: scope.to, page: String(scope.page), pageSize: String(scope.pageSize) }).toString();

function readFailure(status: number): ReadFailure {
  if (status === 401) return { status, message: "انتهت الجلسة. سجّل الدخول مجددًا.", retryable: false };
  if (status === 403) return { status, message: ownerOnly, retryable: false };
  if (status === 404) return { status, message: "اختلاف الدفع غير متاح.", retryable: false };
  if (status === 400 || status === 422) return { status, message: "راجع تاريخي الفترة وأعد الاختيار.", retryable: false };
  return { status, message: unavailable, retryable: true };
}

function useIssueRead<T>(path: string | null) {
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ path: string; revision: number; data?: T; failure?: ReadFailure }>();
  useEffect(() => {
    if (!path) return;
    const readPath = path;
    const abort = new AbortController();
    const signal = AbortSignal.any([abort.signal, AbortSignal.timeout(12000)]);
    async function load() {
      const response = await fetch(readPath, { cache: "no-store", signal });
      const next = response.ok ? { data: await response.json() as T } : { failure: readFailure(response.status) };
      if (!abort.signal.aborted) setResult({ path: readPath, revision, ...next });
    }
    void load().catch(() => {
      if (!abort.signal.aborted) setResult({ path, revision, failure: readFailure(503) });
    });
    return () => abort.abort();
  }, [path, revision]);
  return { current: result?.path === path && result.revision === revision ? result : undefined, reload: () => setRevision(value => value + 1) };
}

function ReadError({ failure, reload }: { failure: ReadFailure; reload: () => void }) {
  return <div className={styles.readError}>
    <ErrorState message={failure.message} />
    {failure.retryable && <button className="secondary-button" type="button" onClick={reload}>إعادة المحاولة</button>}
  </div>;
}

export function ProviderQuarantine() {
  const search = useSearchParams();
  const defaults = useDateFilterDefaults();
  const [scope, setScope] = useState(() => listScope(search, defaults));
  const { from, to, page, pageSize } = scope;
  const validRange = !from || !to || from <= to;
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize), ...(from && { from }), ...(to && { to }) });
  const { current, reload } = useIssueRead<IssuePage>(validRange ? `${root}?${query}` : null);
  const { data, failure } = current ?? {};
  const listDenied = failure?.status === 401 || failure?.status === 403;

  function update(next: Scope) {
    setScope(next);
    window.history.replaceState(null, "", `${route}?${scopeQuery(next)}`);
  }

  return <>
    <PageHeader title="متابعة اختلافات الدفع" context="الحسابات والماليات"
      description="راجع سجل مزود الدفع قبل أي تأكيد. صورة التحويل أو صفحة العودة لا تثبت التحصيل." />
    {!listDenied && <p className={styles.listNotice}>الإشارات في هذا السجل لم تؤكد تحصيلًا.</p>}
    {!listDenied && <section className={`filter-bar ${styles.filter}`} aria-label="فترة رصد اختلافات الدفع">
      <label>من تاريخ<input type="date" value={from} aria-invalid={!validRange} onChange={e => update({ ...scope, from: e.target.value, page: 1 })} /></label>
      <label>إلى تاريخ<input type="date" value={to} aria-invalid={!validRange} onChange={e => update({ ...scope, to: e.target.value, page: 1 })} /></label>
      {(from || to) && <button type="button" className="secondary-button" onClick={() => update({ ...scope, from: "", to: "", page: 1 })}>مسح الفلاتر</button>}
    </section>}
    {!validRange ? <ErrorState message="تاريخ البداية يجب أن يسبق تاريخ النهاية أو يساويه." />
      : failure ? <ReadError failure={failure} reload={reload} />
      : !data ? <LoadingState message="جارٍ تحميل اختلافات الدفع…" />
      : !data.items.length ? <EmptyState title="لا توجد اختلافات في هذه الفترة" />
      : <AdminDataTable label="اختلافات الدفع" rows={data.items} rowKey={row => row.id}
        serverPagination={{ page: data.page - 1, pageSize: data.pageSize, total: data.total,
          onPageChange: index => update({ ...scope, page: index + 1 }),
          onPageSizeChange: size => update({ ...scope, page: 1, pageSize: size }),
        }} columns={[
          { key: "player", header: "اللاعب والفرع", primary: true, render: row => <>
            <Link href={`${route}/${row.id}?${scopeQuery({ ...scope, page: data.page })}`} aria-label={`عرض اختلاف دفع ${row.player}، ${issueReason(row.reasonCode)}`}>{row.player}</Link>
            <small>{row.branch}</small>
          </> },
          { key: "reason", header: "سبب المتابعة", render: row => issueReason(row.reasonCode) },
          { key: "expected", header: "المبلغ المتوقع", render: row => amount(row.expectedAmount, row.expectedCurrency) },
          { key: "received", header: "المبلغ الوارد", render: row => amount(row.receivedAmount, row.receivedCurrency) },
          { key: "time", header: "وقت الرصد", render: row => <time dateTime={row.createdAtUtc}>{issueTime(row.createdAtUtc)}</time> },
        ]} />}
  </>;
}

export function ProviderQuarantineDetail({ id }: { id: string }) {
  const search = useSearchParams();
  const defaults = useDateFilterDefaults();
  const back = { label: "العودة إلى اختلافات الدفع", href: `${route}?${scopeQuery(listScope(search, defaults))}` };
  const { current, reload } = useIssueRead<ProviderIssue>(`${root}/${encodeURIComponent(id)}`);
  return <>
    <PageHeader title="تفاصيل اختلاف الدفع" context="الحسابات والماليات" back={back} />
    {current?.failure ? <ReadError failure={current.failure} reload={reload} />
      : current?.data ? <IssueDetail issue={current.data} /> : <LoadingState message="جارٍ تحميل تفاصيل الاختلاف…" />}
  </>;
}

function IssueDetail({ issue }: { issue: ProviderIssue }) {
  const sameReference = issue.expectedReference === issue.receivedReference;
  return <section className={`detail-card ${styles.detail}`} aria-label="ملخص اختلاف الدفع">
    <div className={styles.heading}>
      <div><h2>{issueReason(issue.reasonCode)}</h2><p>{issue.player} · {issue.branch}</p></div>
      <div className={styles.state}>
        <span>حالة طلب الدفع</span>
        <StatusBadge status={issue.paymentStatus} label={paymentStates[issue.paymentStatus] ?? "تحتاج مراجعة"} />
      </div>
    </div>
    <dl className={styles.amounts}>
      <div><dt>المبلغ المتوقع</dt><dd>{amount(issue.expectedAmount, issue.expectedCurrency)}</dd></div>
      <div><dt>المبلغ الوارد</dt><dd>{amount(issue.receivedAmount, issue.receivedCurrency)}</dd></div>
    </dl>
    <p className={styles.notice}>هذه الإشارة لم تؤكد الدفع. راجع سجل مزود الدفع قبل أي إجراء.</p>
    <div className={styles.actions}>
      {issue.receiptId && <Link className="primary-button" href={`/dashboard/subscriptions/receipts/${issue.receiptId}`}>عرض إيصال التحصيل</Link>}
      <Link className={issue.receiptId ? "secondary-button" : "primary-button"} href={`/dashboard/subscriptions/payments/${issue.paymentRequestId}`}>فتح طلب الدفع</Link>
    </div>
    <time className={styles.timestamp} dateTime={issue.createdAtUtc}>رُصد في {issueTime(issue.createdAtUtc)} · بتوقيت القاهرة</time>
    <details className={styles.source}>
      <summary>تفاصيل الإشارة والمراجع</summary>
      <dl>
        <div><dt>مصدر الإشارة</dt><dd>{issue.provider === "InternalTest" ? "بوابة الدفع التجريبية" : "مزود الدفع المسجّل"}</dd></div>
        <div><dt>الحالة الواردة</dt><dd>{signalStates[issue.receivedStatus] ?? "حالة غير معروفة"}</dd></div>
        <div><dt>{sameReference ? "مرجع الطلب والإشارة" : "مرجع الطلب"}</dt><dd><bdi dir="ltr">{issue.expectedReference || "—"}</bdi></dd></div>
        {!sameReference && <div><dt>المرجع الوارد</dt><dd><bdi dir="ltr">{issue.receivedReference || "—"}</bdi></dd></div>}
        <div><dt>رقم الحدث لدى المزود</dt><dd><bdi dir="ltr">{issue.providerEventId}</bdi></dd></div>
      </dl>
    </details>
  </section>;
}
