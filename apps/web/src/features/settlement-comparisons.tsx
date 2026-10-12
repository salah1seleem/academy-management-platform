"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { monthToDate } from "../lib/date-range";
import { formatMoneyAr } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";
import styles from "./settlement-comparisons.module.css";

const api = "/api/v1/finance/settlement-comparisons", route = "/dashboard/finance/settlement-comparisons";
const todayCairo = () => monthToDate().to;
export const statementResult: Record<string, string> = { Matched: "مطابق يدويًا", AmountMismatch: "المبلغ مختلف", CurrencyMismatch: "العملة مختلفة", NetMismatch: "الصافي لا يساوي الإجمالي ناقص الرسوم", Unmatched: "لا يوجد تحصيل مطابق للمرجع", Ambiguous: "المرجع غير حاسم" };
type Row = { id: string; branchId: string; collectionId?: string; sourceKind: string; accountTag: string; statementReference: string; lineReference: string; provider: string; paymentReference: string; settlementDate: string; gross: string; fee: string; net: string; currency: string; expected?: string; expectedCurrency?: string; result: string; note?: string; actor?: string; createdAtUtc?: string; receiptId?: string };
function useRead<T>(path: string) {
  const [data, setData] = useState<T | null>(null), [error, setError] = useState("");
  useEffect(() => { let active = true; const abort = new AbortController(); void fetch(path, { signal: abort.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw new Error(r.status === 403 ? "مطابقة الكشف متاحة للمالك فقط." : "تعذر تحميل المطابقة."); return r.json(); }).then(d => { if (active) setData(d); }).catch(e => { if (active && e.name !== "AbortError") setError(e.message); }); return () => { active = false; abort.abort(); }; }, [path]);
  return { data, error };
}
function Notice() { return <aside className={styles.notice}><strong>مقارنة كشف مُدخل يدويًا، وليست تأكيد دفع</strong><p>لسطر قبض اشتراك إلكتروني واحد، دون اتصال ببنك. لا تغيّر الإيصال أو الاشتراك؛ الرسوم لا تُرحّل مصروفًا تلقائيًا. استخدم اسم حساب مختصرًا دون أسرار أو بيانات بطاقة.</p></aside>; }
export function SettlementComparisons() {
  const [range, setRange] = useState(() => monthToDate()), [page, setPage] = useState(1);
  const query = new URLSearchParams({ page: String(page), ...(range.from ? { from: range.from } : {}), ...(range.to ? { to: range.to } : {}) }).toString();
  return <><PageHeader title="مطابقة كشوف التحصيل" context="المالية / مراجعة المالك" /><Link className="primary-button" href={`${route}/new`}>مقارنة سطر كشف</Link><Notice /><section className="filter-bar"><label>من تاريخ<input type="date" value={range.from} onChange={e => { setRange({ ...range, from: e.target.value }); setPage(1); }} /></label><label>إلى تاريخ<input type="date" value={range.to} onChange={e => { setRange({ ...range, to: e.target.value }); setPage(1); }} /></label><button className="secondary-button" onClick={() => { setRange({ from: "", to: "" }); setPage(1); }}>مسح الفلاتر</button></section><Rows key={query} query={query} onPage={setPage} /></>;
}
function Rows({ query, onPage }: { query: string; onPage: (page: number) => void }) {
  const { data, error } = useRead<{ total: number; page: number; pageSize: number; items: Row[] }>(`${api}?${query}`);
  if (error) return <ErrorState message={error} />; if (!data) return <LoadingState />;
  return <><p role="status">{data.total} مقارنة محفوظة</p>{!data.items.length ? <EmptyState title="لا توجد مقارنات ضمن الفترة" /> : <AdminDataTable label="مطابقة كشوف التحصيل" rows={data.items} rowKey={r => r.id} columns={[
    { key: "source", header: "الكشف والسطر", primary: true, render: r => <><strong>{r.statementReference}</strong><small>{r.lineReference} · {r.accountTag}</small></> }, { key: "result", header: "نتيجة المقارنة", render: r => statementResult[r.result] ?? "تحتاج مراجعة" }, { key: "gross", header: "الإجمالي", render: r => formatMoneyAr(r.gross, r.currency) }, { key: "fee", header: "الرسوم", render: r => formatMoneyAr(r.fee, r.currency) }, { key: "net", header: "الصافي", render: r => formatMoneyAr(r.net, r.currency) }, { key: "action", header: "التفاصيل", render: r => <Link href={`${route}/${r.id}`}>عرض المقارنة</Link> },
  ]} />}<nav className="form-actions" aria-label="صفحات المطابقة"><button className="secondary-button" disabled={data.page <= 1} onClick={() => onPage(data.page - 1)}>السابق</button><span>{data.page} / {Math.max(1, Math.ceil(data.total / data.pageSize))}</span><button className="secondary-button" disabled={data.page * data.pageSize >= data.total} onClick={() => onPage(data.page + 1)}>التالي</button></nav></>;
}
export function SettlementComparisonDetail({ id }: { id: string }) { return <Detail key={id} id={id} />; }
function Detail({ id }: { id: string }) {
  const { data: d, error } = useRead<Row>(`${api}/${id}`);
  return <><PageHeader title="تفاصيل مقارنة الكشف" context="المالية / مطابقة التحصيل" /><Link href={route}>العودة للمطابقة</Link><Notice />{error ? <ErrorState message={error} /> : !d ? <LoadingState /> : <section className="detail-card"><h2>{statementResult[d.result] ?? "تحتاج مراجعة"}</h2><p>{d.statementReference} · {d.lineReference} · {d.accountTag}</p><div className="cash-session-stats">{[["إجمالي الكشف", d.gross], ["الرسوم الواردة", d.fee], ["صافي الكشف", d.net]].map(([name, value]) => <div key={name}><small>{name}</small><strong>{formatMoneyAr(value, d.currency)}</strong></div>)}</div><p>التحصيل المسجل: {d.expected ? formatMoneyAr(d.expected, d.expectedCurrency) : "لم يُعثر على تحصيل مطابق"}</p><dl><dt>مرجع الدفع في الكشف</dt><dd dir="ltr">{d.paymentReference}</dd><dt>تاريخ التسوية في الكشف</dt><dd>{d.settlementDate}</dd><dt>المراجع</dt><dd>{d.actor}</dd>{d.note && <><dt>ملاحظة</dt><dd>{d.note}</dd></>}</dl>{d.receiptId && <Link className="secondary-button" href={`/dashboard/subscriptions/receipts/${d.receiptId}`}>الإيصال الأصلي</Link>}</section>}</>;
}
type Options = { branches: { id: string; name: string }[]; providers: string[] };
type Preview = { quoteHash: string; result: string; expected?: string; expectedCurrency?: string; gross: string; fee: string; net: string; currency: string };
export function SettlementComparisonForm() {
  const { data, error } = useRead<Options>(`${api}/options`);
  return <><PageHeader title="مقارنة سطر كشف" context="المالية / مطابقة التحصيل" /><Link href={route}>العودة للمطابقة</Link><Notice />{error ? <ErrorState message={error} /> : !data ? <LoadingState /> : <StatementForm options={data} />}</>;
}
function StatementForm({ options }: { options: Options }) {
  const [values, setValues] = useState({ branchId: "", sourceKind: "Provider", accountTag: "", statementReference: "", lineReference: "", provider: "", paymentReference: "", settlementDate: todayCairo(), grossAmount: "", feeAmount: "0", netAmount: "", currency: "EGP", note: "" });
  const [preview, setPreview] = useState<Preview | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState(""), [issued, setIssued] = useState(false), [saved, setSaved] = useState<string | null>(null);
  const field = (name: keyof typeof values) => ({ value: values[name], onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => setValues({ ...values, [name]: e.target.value }) });
  async function compare() { setBusy(true); setError(""); try { setPreview(await (await csrfRequest(`${api}/preview`, "POST", values)).json()); } catch (e) { setError(e instanceof Error ? e.message : "تعذر عرض المقارنة."); } finally { setBusy(false); } }
  async function confirm() { if (!preview || busy) return; setBusy(true); setIssued(true); setError(""); try { const row = await (await csrfRequest(api, "POST", { ...values, quoteHash: preview.quoteHash })).json() as Row; setSaved(row.id); } catch (e) { setError(e instanceof Error ? e.message : "تعذر تأكيد الحفظ؛ أعد المحاولة بنفس البيانات."); } finally { setBusy(false); } }
  if (saved) return <section className="detail-card"><p role="status">تم حفظ نتيجة المقارنة دون تغيير التحصيل أو الاشتراك.</p><Link className="primary-button" href={`${route}/${saved}`}>عرض المقارنة المحفوظة</Link><Link href={route}>العودة للقائمة</Link></section>;
  return <section className="detail-card">{error && <ErrorState message={error} />}<form onSubmit={e => { e.preventDefault(); void compare(); }}><fieldset disabled={busy || preview !== null} className={`form-shell ${styles.fields}`}><legend>بيانات السطر كما وردت في الكشف</legend>
    <label>الفرع<select required {...field("branchId")}><option value="">اختر الفرع</option>{options.branches.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label>
    <label>مصدر الكشف<select {...field("sourceKind")}><option value="Provider">مزود الدفع</option><option value="Bank">البنك</option></select></label>
    <label>اسم الحساب المختصر<input required maxLength={60} {...field("accountTag")} /></label><label>مرجع الكشف<input required maxLength={100} {...field("statementReference")} /></label><label>مرجع السطر<input required maxLength={100} {...field("lineReference")} /></label>
    <label>المزود المسجل<select required {...field("provider")}><option value="">اختر المزود</option>{options.providers.map(p => <option key={p} value={p}>{p}</option>)}</select></label><label>مرجع الدفع في الكشف<input required maxLength={100} {...field("paymentReference")} /></label><label>تاريخ التسوية في الكشف<input type="date" required max={todayCairo()} {...field("settlementDate")} /></label>
    {[["grossAmount", "إجمالي الكشف"], ["feeAmount", "الرسوم الواردة"], ["netAmount", "صافي الكشف"]].map(([name, label]) => <label key={name}>{label}<input type="number" min="0" max="999999999" step="0.01" required {...field(name as keyof typeof values)} /></label>)}
    <label>العملة<input required maxLength={3} {...field("currency")} /></label><label>ملاحظة اختيارية<textarea maxLength={300} {...field("note")} /></label>
  </fieldset>{!preview && <button className="primary-button" disabled={busy}>معاينة المقارنة</button>}</form>
    {preview && <div className="detail-card"><h2>{statementResult[preview.result]}</h2><p>التحصيل المسجل: {preview.expected ? formatMoneyAr(preview.expected, preview.expectedCurrency) : "لا يوجد تطابق"}</p><p>إجمالي الكشف {formatMoneyAr(preview.gross, preview.currency)} · الرسوم {formatMoneyAr(preview.fee, preview.currency)} · الصافي {formatMoneyAr(preview.net, preview.currency)}</p><p>الحفظ يثبت المقارنة فقط؛ لا اعتماد دفع أو مصروف أو رصيد جديد.</p><div className="form-actions"><button className="primary-button" disabled={busy} onClick={() => void confirm()}>{issued ? "إعادة تأكيد الحفظ بنفس البيانات" : "حفظ نتيجة المقارنة"}</button>{!issued && <button className="secondary-button" disabled={busy} onClick={() => setPreview(null)}>تعديل البيانات</button>}</div></div>}
  </section>;
}
