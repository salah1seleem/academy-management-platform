"use client";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { apiTransportError } from "../lib/api-problem";
import { monthToDate } from "../lib/date-range";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./settlement-comparisons.module.css";

export const statementApi = "/api/v1/finance/settlement-comparisons", statementRoute = "/dashboard/finance/settlement-comparisons";
const api = statementApi, route = statementRoute;
const todayCairo = () => monthToDate().to;
export const statementResult: Record<string, string> = { Matched: "مطابق يدويًا", AmountMismatch: "المبلغ مختلف", CurrencyMismatch: "العملة مختلفة", NetMismatch: "الصافي لا يساوي الإجمالي ناقص الرسوم", Unmatched: "لا يوجد تحصيل مطابق للمرجع", Ambiguous: "المرجع غير حاسم" };
type Row = { id: string; branchId: string; collectionId?: string; sourceKind: string; accountTag: string; statementReference: string; lineReference: string; provider: string; paymentReference: string; settlementDate: string; gross: string; fee: string; net: string; currency: string; expected?: string; expectedCurrency?: string; result: string; note?: string; actor?: string; createdAtUtc?: string; receiptId?: string };
type ListData = { total: number; page: number; pageSize: number; items: Row[] };
type Scope = { from: string; to: string; page: number };
type ReadError = { message: string; retryable: boolean };
const ownerOnly = "مطابقة الكشف متاحة للمالك فقط.";
const readUnavailable = "تعذر تحميل المطابقة.";

function listScope(params: Pick<URLSearchParams, "get">): Scope {
  const defaults = monthToDate();
  function date(key: "from" | "to") {
    const value = params.get(key);
    if (value === "") return "";
    if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return defaults[key];
    const parsed = new Date(`${value}T00:00:00Z`);
    return Number.isFinite(parsed.valueOf()) && parsed.toISOString().slice(0, 10) === value ? value : defaults[key];
  }
  const page = Number(params.get("page"));
  return { from: date("from"), to: date("to"), page: Number.isSafeInteger(page) && page >= 1 && page <= 100000 ? page : 1 };
}
const scopeQuery = ({ from, to, page }: Scope) => new URLSearchParams({ from, to, page: String(page) }).toString();

export function useStatementListNavigation() {
  const query = scopeQuery(listScope(useSearchParams()));
  return { query, back: { label: "العودة إلى المطابقة", href: `${route}?${query}` } };
}

export function useStatementRead<T>(path: string | null) {
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ path: string; revision: number; authorized: boolean; data?: T; failure?: ReadError }>();
  useEffect(() => {
    if (!path) return;
    const abort = new AbortController();
    void fetch(path, { signal: abort.signal, cache: "no-store" }).then(async response => {
      if (response.ok) {
        const data = await response.json() as T;
        if (!abort.signal.aborted) setResult({ path, revision, data, authorized: true });
        return;
      }
      const status = response.status;
      const message = status === 403 ? ownerOnly : status === 401 ? "انتهت الجلسة. سجّل الدخول مجددًا."
        : status === 404 ? "سجل المطابقة غير متاح." : readUnavailable;
      const retryable = ![401, 403, 404].includes(status);
      if (!abort.signal.aborted) setResult(previous => ({ path, revision, authorized: retryable && previous?.authorized === true, failure: { message, retryable } }));
    }).catch(() => {
      if (!abort.signal.aborted) setResult(previous => ({ path, revision, authorized: previous?.authorized === true, failure: { message: readUnavailable, retryable: true } }));
    });
    return () => abort.abort();
  }, [path, revision]);
  const current = result?.path === path && result?.revision === revision ? result : undefined;
  return { data: current?.data ?? null, authorized: result?.authorized === true, failure: current?.failure, reload: () => setRevision(value => value + 1) };
}

export function StatementReadFailure({ failure, reload }: { failure: ReadError; reload: () => void }) {
  return <div className={styles.readError}><ErrorState message={failure.message} />{failure.retryable && <button className="secondary-button" type="button" onClick={reload}>إعادة المحاولة</button>}</div>;
}

export function SettlementComparisons() {
  const search = useSearchParams();
  const [scope, setScope] = useState(() => listScope(search));
  const { from, to, page } = scope;
  const validRange = !from || !to || from <= to;
  const query = new URLSearchParams({ page: String(page), ...(from ? { from } : {}), ...(to ? { to } : {}) });
  const { data, authorized, failure, reload } = useStatementRead<ListData>(validRange ? `${api}?${query}` : null);
  const showControls = !failure || failure.retryable;
  function update(next: Scope) {
    setScope(next);
    window.history.replaceState(null, "", `${route}?${scopeQuery(next)}`);
  }
  const context = scopeQuery(scope);
  return <>
    <PageHeader title="مطابقة كشوف التحصيل" context="المالية / مراجعة المالك"
      description="قارن بيانات الكشف بالتحصيلات الإلكترونية المسجلة. الحفظ يسجّل نتيجة المقارنة فقط، وتبقى الرسوم للمراجعة."
      action={authorized ? { label: "مقارنة سطر كشف", href: `${route}/new?${context}` } : undefined}
      secondaryActions={authorized ? [{ label: "استيراد كشف CSV", href: `${route}/import?${context}` }] : []} />
    {showControls && <section className={`filter-bar ${styles.filterBar}`} aria-label="فترة التسوية">
      <label>من تاريخ<input type="date" value={from} onChange={e => update({ from: e.target.value, to, page: 1 })} /></label>
      <label>إلى تاريخ<input type="date" value={to} onChange={e => update({ from, to: e.target.value, page: 1 })} /></label>
      {(from || to) && <button className="secondary-button" type="button" onClick={() => update({ from: "", to: "", page: 1 })}>مسح الفلاتر</button>}
    </section>}
    {!validRange ? <ErrorState message="اختر تاريخ بداية يسبق تاريخ النهاية أو يساويه." />
      : failure ? <StatementReadFailure failure={failure} reload={reload} />
      : !data ? <LoadingState />
      : <Rows data={data} context={context} filtered={Boolean(from || to)} onPage={page => update({ from, to, page })} />}
  </>;
}
function Rows({ data, context, filtered, onPage }: { data: ListData; context: string; filtered: boolean; onPage: (page: number) => void }) {
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize));
  return <>
    {data.items.length === 0
      ? <EmptyState title={data.page > pages ? "لا توجد سجلات في هذه الصفحة" : filtered ? "لا توجد مقارنات ضمن الفترة" : "لا توجد مقارنات محفوظة"} />
      : <>
        <p role="status">النتائج: {data.total}</p>
        <AdminDataTable label="مطابقة كشوف التحصيل" rows={data.items} rowKey={r => r.id} paginate={false} columns={[
          { key: "source", header: "الكشف والسطر", primary: true, render: r => <><strong>{r.statementReference}</strong><small><bdi>{r.lineReference}</bdi> · <bdi>{r.accountTag}</bdi></small></> },
          { key: "date", header: "التسوية", render: r => formatDateAr(r.settlementDate) },
          { key: "result", header: "نتيجة المقارنة", render: r => statementResult[r.result] ?? "تحتاج مراجعة" },
          { key: "gross", header: "الإجمالي", render: r => formatMoneyAr(r.gross, r.currency) },
          { key: "fee", header: "الرسوم", render: r => formatMoneyAr(r.fee, r.currency) },
          { key: "net", header: "الصافي", render: r => formatMoneyAr(r.net, r.currency) },
          { key: "action", header: "التفاصيل", render: r => <Link href={`${route}/${r.id}?${context}`}>عرض المقارنة</Link> },
        ]} />
      </>}
    {data.page > pages
      ? <button type="button" className="secondary-button" onClick={() => onPage(1)}>العودة إلى الصفحة الأولى</button>
      : pages > 1 && <nav className={styles.pagination} aria-label="صفحات المطابقة">
        <button className="secondary-button" type="button" disabled={data.page <= 1} onClick={() => onPage(data.page - 1)}>السابق</button>
        <span>صفحة {data.page} من {pages}</span>
        <button className="secondary-button" type="button" disabled={data.page >= pages} onClick={() => onPage(data.page + 1)}>التالي</button>
      </nav>}
  </>;
}
export function SettlementComparisonDetail({ id }: { id: string }) { return <Detail key={id} id={id} />; }
function Detail({ id }: { id: string }) {
  const { back } = useStatementListNavigation();
  const { data: d, failure, reload } = useStatementRead<Row>(`${api}/${id}`);
  return <>
    <PageHeader title="تفاصيل مقارنة الكشف" context="المالية / مطابقة التحصيل" back={back} />
    {failure ? <StatementReadFailure failure={failure} reload={reload} /> : !d ? <LoadingState /> : <section className={`detail-card ${styles.detail}`}>
      <h2>{statementResult[d.result] ?? "تحتاج مراجعة"}</h2>
      <p className={styles.reference}><bdi>{d.statementReference}</bdi> · سطر <bdi>{d.lineReference}</bdi> · <bdi>{d.accountTag}</bdi></p>
      <StatementAmounts value={d} />
      <dl className={styles.metadata}>
        <div><dt>التحصيل المسجل</dt><dd>{d.expected != null ? formatMoneyAr(d.expected, d.expectedCurrency) : "لم يُعثر على تحصيل مطابق"}</dd></div>
        <div><dt>تاريخ التسوية</dt><dd><time dateTime={d.settlementDate}>{formatDateAr(d.settlementDate)}</time></dd></div>
        <div><dt>مرجع الدفع في الكشف</dt><dd><bdi>{d.paymentReference}</bdi></dd></div>
        {d.actor && <div><dt>المراجع</dt><dd>{d.actor}</dd></div>}
        {d.note && <div><dt>ملاحظة</dt><dd>{d.note}</dd></div>}
      </dl>
      {d.receiptId && <Link className="secondary-button" href={`/dashboard/subscriptions/receipts/${d.receiptId}`}>الإيصال الأصلي</Link>}
    </section>}
  </>;
}
function StatementAmounts({ value }: { value: Pick<Row, "gross" | "fee" | "net" | "currency"> }) {
  return <dl className={styles.amounts}>
    <div><dt>إجمالي الكشف</dt><dd>{formatMoneyAr(value.gross, value.currency)}</dd></div>
    <div><dt>الرسوم</dt><dd>{formatMoneyAr(value.fee, value.currency)}</dd></div>
    <div><dt>صافي الكشف</dt><dd>{formatMoneyAr(value.net, value.currency)}</dd></div>
  </dl>;
}
export type StatementOptions = { branches: { id: string; name: string }[]; providers: string[] };
const providerLabels: Record<string, string> = { InternalTest: "دفع تجريبي", Manual: "تحصيل يدوي" };
export const statementProviderLabel = (provider: string) => providerLabels[provider] ?? provider;
type Preview = { quoteHash: string; result: string; expected?: string; expectedCurrency?: string; gross: string; fee: string; net: string; currency: string };
export function SettlementComparisonForm() {
  const { back, query } = useStatementListNavigation();
  const { data, failure, reload } = useStatementRead<StatementOptions>(`${api}/options`);
  return <>
    <PageHeader title="مقارنة سطر كشف" context="المالية / مطابقة التحصيل" back={back} />
    {failure ? <StatementReadFailure failure={failure} reload={reload} /> : !data ? <LoadingState />
      : !data.branches.length ? <EmptyState title="لا توجد فروع متاحة للمقارنة" />
      : !data.providers.length ? <EmptyState title="لا توجد تحصيلات إلكترونية مسجّلة للمقارنة" />
      : <StatementForm options={data} query={query} />}
  </>;
}
function StatementForm({ options, query }: { options: StatementOptions; query: string }) {
  const [values, setValues] = useState({ branchId: "", sourceKind: "Provider", accountTag: "", statementReference: "", lineReference: "", provider: "", paymentReference: "", settlementDate: todayCairo(), grossAmount: "", feeAmount: "0", netAmount: "", currency: "EGP", note: "" });
  const [preview, setPreview] = useState<Preview | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [uncertain, setUncertain] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const [saved, setSaved] = useState<string | null>(null);
  const inFlight = useRef(false);
  const outcome = useRef<HTMLHeadingElement>(null);
  const errorRegion = useRef<HTMLDivElement>(null);
  useEffect(() => { if (preview || saved) outcome.current?.focus(); }, [preview, saved]);
  useEffect(() => { if (error) errorRegion.current?.focus(); }, [error]);
  const field = (name: keyof typeof values) => ({
    value: values[name],
    onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => {
      const value = e.target.value;
      setValues(previous => ({ ...previous, [name]: value }));
    },
  });
  async function compare() {
    if (inFlight.current || preview) return;
    inFlight.current = true;
    setBusy(true);
    setError("");
    try {
      const result = await (await csrfRequest(`${api}/preview`, "POST", values)).json() as Preview;
      if (!result.quoteHash) throw new Error();
      setPreview(result);
    } catch (cause) {
      if (cause instanceof DashboardRequestError && [401, 403].includes(cause.status)) setBlocked(true);
      setError(apiTransportError(cause, "تعذر عرض المقارنة. أعد المحاولة.").message);
    } finally {
      inFlight.current = false;
      setBusy(false);
    }
  }
  async function confirm() {
    if (!preview || inFlight.current) return;
    inFlight.current = true;
    setBusy(true);
    setError("");
    try {
      const row = await (await csrfRequest(api, "POST", { ...values, quoteHash: preview.quoteHash }, {}, { announce: false })).json() as Row;
      if (!row.id) throw new Error();
      setSaved(row.id);
    } catch (cause) {
      const status = cause instanceof DashboardRequestError ? cause.status : 0;
      if ([401, 403].includes(status)) {
        setPreview(null);
        setBlocked(true);
      } else if ([400, 404, 409, 422].includes(status)) {
        setPreview(null);
        setUncertain(false);
      } else {
        setUncertain(true);
      }
      setError(apiTransportError(cause, "تعذر تأكيد الحفظ. أعد التأكيد بنفس البيانات.").message);
    } finally {
      inFlight.current = false;
      setBusy(false);
    }
  }
  if (saved) return <section className={`detail-card ${styles.detail}`}>
    <h2 ref={outcome} tabIndex={-1}>تم حفظ المقارنة</h2>
    <p role="status">تم حفظ نتيجة المقارنة دون تغيير التحصيل أو الاشتراك.</p>
    <Link className="primary-button" href={`${route}/${saved}?${query}`}>عرض المقارنة المحفوظة</Link>
  </section>;
  return <section className="detail-card">
    {error && <div ref={errorRegion} tabIndex={-1}><ErrorState message={error} /></div>}
    {preview && <section className={styles.preview}>
      <h2 ref={outcome} tabIndex={-1}>{statementResult[preview.result] ?? "تحتاج مراجعة"}</h2>
      <StatementAmounts value={preview} />
      <p>التحصيل المسجل: {preview.expected != null ? formatMoneyAr(preview.expected, preview.expectedCurrency) : "لا يوجد تطابق"}</p>
      <p>الحفظ يسجّل نتيجة المقارنة فقط، والرسوم للمراجعة.</p>
      <div className="form-actions">
        <button className="primary-button" type="button" disabled={busy} onClick={() => void confirm()}>{busy ? "جارٍ الحفظ…" : uncertain ? "إعادة تأكيد الحفظ بنفس البيانات" : "حفظ نتيجة المقارنة"}</button>
        {!uncertain && <button className="secondary-button" type="button" disabled={busy} onClick={() => setPreview(null)}>تعديل البيانات</button>}
      </div>
    </section>}
    {!blocked && <form onSubmit={e => { e.preventDefault(); void compare(); }}>
      <fieldset disabled={busy || preview !== null} className={`form-shell ${styles.fields}`}><legend>بيانات السطر كما وردت في الكشف</legend>
        <label>الفرع<select required {...field("branchId")}><option value="">اختر الفرع</option>{options.branches.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label>
        <label>مصدر الكشف<select {...field("sourceKind")}><option value="Provider">مزود الدفع</option><option value="Bank">البنك</option></select></label>
        <label><span id="statement-account-label">اسم الحساب المختصر</span><input required maxLength={60} autoComplete="off" aria-labelledby="statement-account-label" aria-describedby="statement-account-help" {...field("accountTag")} /><small id="statement-account-help">اسم للتعرّف على الكشف. تجنّب أرقام الحسابات والبطاقات.</small></label>
        <label>مرجع الكشف<input required maxLength={100} dir="auto" {...field("statementReference")} /></label>
        <label>مرجع السطر<input required maxLength={100} dir="auto" {...field("lineReference")} /></label>
        <label>المزود المسجل<select required {...field("provider")}><option value="">اختر المزود</option>{options.providers.map(p => <option key={p} value={p}>{statementProviderLabel(p)}</option>)}</select></label>
        <label>مرجع الدفع في الكشف<input required maxLength={100} dir="auto" {...field("paymentReference")} /></label>
        <label>تاريخ التسوية في الكشف<input type="date" required min="2000-01-01" max={todayCairo()} {...field("settlementDate")} /></label>
        {([["grossAmount", "إجمالي الكشف"], ["feeAmount", "الرسوم الواردة"], ["netAmount", "صافي الكشف"]] as const).map(([name, label]) => <label key={name}>{label}<input type="number" dir="ltr" min={name === "grossAmount" ? "0.01" : "0"} max="999999999" step="0.01" required {...field(name)} /></label>)}
        <label>العملة<input required maxLength={3} pattern="[A-Z]{3}" dir="ltr" {...field("currency")} /></label>
        <label>ملاحظة اختيارية<textarea maxLength={300} {...field("note")} /></label>
      </fieldset>
      {!preview && <button className="primary-button" type="submit" disabled={busy}>{busy ? "جارٍ تحميل المعاينة…" : "معاينة المقارنة"}</button>}
    </form>}
  </section>;
}
