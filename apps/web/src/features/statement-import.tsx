"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { FilePicker } from "../components/file-picker";
import { apiTransportError } from "../lib/api-problem";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import {
  StatementReadFailure, statementApi as api, statementRoute as route, statementProviderLabel,
  statementResult, useStatementListNavigation, useStatementRead, type StatementOptions,
} from "./settlement-comparisons";
import styles from "./settlement-comparisons.module.css";

type Line = {
  number: number; reused: boolean; lineReference: string; paymentReference: string; settlementDate?: string;
  result: string; gross: string; fee: string; net: string; currency: string; expected?: string; expectedCurrency?: string;
};
type Preview = { quoteHash: string; count: number; reused: number; matched: number; differences: number; items: Line[] };
type Saved = { count: number; created: number; reused: number; items: { id: string; lineReference: string; result: string }[] };
type SelectedFile = { name: string; text: string };

export function StatementImport() {
  const { back, query } = useStatementListNavigation();
  const { data, failure, reload } = useStatementRead<StatementOptions>(`${api}/options`);
  return <>
    <PageHeader title="استيراد كشف للمقارنة" context="المالية / مطابقة كشوف التحصيل" back={back} />
    {failure ? <StatementReadFailure failure={failure} reload={reload} /> : !data ? <LoadingState />
      : !data.branches.length ? <EmptyState title="لا توجد فروع متاحة للمقارنة" />
      : !data.providers.length ? <EmptyState title="لا توجد تحصيلات إلكترونية مسجّلة للمقارنة" />
      : <ImportForm options={data} query={query} />}
  </>;
}

function ImportForm({ options, query }: { options: StatementOptions; query: string }) {
  const [values, setValues] = useState({ branchId: "", sourceKind: "Provider", accountTag: "", statementReference: "", provider: "" });
  const [file, setFile] = useState<SelectedFile | null>(null);
  const [pickerVersion, setPickerVersion] = useState(0);
  const [reading, setReading] = useState(false);
  const [busy, setBusy] = useState<"preview" | "save" | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [saved, setSaved] = useState<Saved | null>(null);
  const [error, setError] = useState("");
  const [uncertain, setUncertain] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const fileGeneration = useRef(0);
  const inFlight = useRef(false);
  const outcome = useRef<HTMLHeadingElement>(null);
  const errorRegion = useRef<HTMLDivElement>(null);

  useEffect(() => { if (preview || saved) outcome.current?.focus(); }, [preview, saved]);
  useEffect(() => { if (error) errorRegion.current?.focus(); }, [error]);

  const field = (key: keyof typeof values) => ({
    value: values[key],
    onChange: (event: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
      const value = event.target.value;
      setValues(previous => ({ ...previous, [key]: value }));
    },
  });

  function rejectFile(message: string) {
    setError(message);
    setPickerVersion(version => version + 1);
  }

  async function loadFile(selected?: File) {
    if (inFlight.current || preview) return;
    const generation = ++fileGeneration.current;
    setError("");
    setFile(null);
    setReading(false);
    if (!selected) return;
    if (!selected.name.toLowerCase().endsWith(".csv") || selected.size > 131072) {
      rejectFile("اختر ملف CSV بترميز UTF-8 لا يتجاوز 128 كيلوبايت.");
      return;
    }
    setReading(true);
    try {
      const text = new TextDecoder("utf-8", { fatal: true }).decode(await selected.arrayBuffer());
      if (generation !== fileGeneration.current) return;
      if (!text.trim()) rejectFile("الملف فارغ. أضف أسطر الكشف إلى القالب ثم اختره مجددًا.");
      else setFile({ name: selected.name, text });
    } catch {
      if (generation === fileGeneration.current) rejectFile("تعذر قراءة الملف. احفظه CSV بترميز UTF-8 ثم حاول مجددًا.");
    } finally {
      if (generation === fileGeneration.current) setReading(false);
    }
  }

  async function compare() {
    if (inFlight.current || reading || preview || !file) return;
    inFlight.current = true;
    setBusy("preview");
    setError("");
    try {
      const result = await (await csrfRequest(`${api}/import/preview`, "POST", { ...values, csvText: file.text })).json() as Preview;
      if (!result.quoteHash || !Array.isArray(result.items)) throw new Error();
      setPreview(result);
    } catch (cause) {
      if (cause instanceof DashboardRequestError && [401, 403].includes(cause.status)) setBlocked(true);
      setError(apiTransportError(cause, "تعذر معاينة الكشف. أعد المحاولة.").message);
    } finally {
      inFlight.current = false;
      setBusy(null);
    }
  }

  async function save() {
    if (inFlight.current || !preview || !file) return;
    inFlight.current = true;
    setBusy("save");
    setError("");
    try {
      const body = { ...values, csvText: file.text, quoteHash: preview.quoteHash };
      const result = await (await csrfRequest(`${api}/import`, "POST", body, {}, { announce: false })).json() as Saved;
      if (!Array.isArray(result.items) || result.count !== preview.count || result.items.length !== result.count) throw new Error();
      setSaved(result);
    } catch (cause) {
      const status = cause instanceof DashboardRequestError ? cause.status : 0;
      if ([401, 403].includes(status)) {
        setPreview(null);
        setBlocked(true);
      } else if ([400, 404, 409, 422].includes(status)) {
        setPreview(null);
        setUncertain(false);
      } else if (status !== 429) {
        setUncertain(true);
      }
      setError(apiTransportError(cause, "تعذر تأكيد الحفظ. أعد التأكيد بنفس الكشف.").message);
    } finally {
      inFlight.current = false;
      setBusy(null);
    }
  }

  if (saved) return <section className={`detail-card ${styles.detail}`}>
    <h2 ref={outcome} tabIndex={-1}>تم حفظ مقارنة الكشف</h2>
    <p role="status">{uncertain
      ? `تم التحقق من حفظ أسطر الكشف (${saved.count}).`
      : `الأسطر الجديدة ${saved.created} · المحفوظة سابقًا ${saved.reused}`}</p>
    <AdminDataTable label="نتائج الكشف المحفوظة" rows={saved.items} rowKey={row => row.id} columns={[
      { key: "line", header: "مرجع السطر", primary: true, render: row => <bdi>{row.lineReference}</bdi> },
      { key: "result", header: "نتيجة المقارنة", render: row => statementResult[row.result] ?? "تحتاج مراجعة" },
      { key: "action", header: "التفاصيل", render: row => <Link href={`${route}/${row.id}?${query}`}>عرض المقارنة</Link> },
    ]} />
  </section>;

  return <section className="detail-card">
    {error && <div ref={errorRegion} tabIndex={-1}><ErrorState message={error} /></div>}
    {preview && <section className={styles.preview}>
      <h2 ref={outcome} tabIndex={-1}>معاينة الكشف</h2>
      <dl className={styles.amounts}>
        <div><dt>إجمالي الأسطر</dt><dd>{preview.count}</dd></div>
        <div><dt>مطابق يدويًا</dt><dd>{preview.matched}</dd></div>
        <div><dt>تحتاج مراجعة</dt><dd>{preview.differences}</dd></div>
      </dl>
      {preview.reused > 0 && <p>الأسطر المحفوظة سابقًا {preview.reused}</p>}
      <p>الحفظ يسجّل نتائج المقارنة فقط، والرسوم للمراجعة.</p>
      <div className="form-actions">
        <button className="primary-button" type="button" disabled={busy !== null} onClick={() => void save()}>
          {busy === "save" ? "جارٍ الحفظ…" : uncertain ? "إعادة تأكيد الحفظ بنفس الكشف" : "حفظ نتائج الكشف كاملة"}
        </button>
        {!uncertain && <button className="secondary-button" type="button" disabled={busy !== null} onClick={() => setPreview(null)}>تعديل الكشف</button>}
      </div>
      <AdminDataTable label="معاينة كشف التسوية" rows={preview.items} rowKey={row => String(row.number)} columns={[
        { key: "line", header: "السطر والمرجع", primary: true, render: row => <>
          <strong><bdi>{row.lineReference}</bdi></strong>
          <small>الصف {row.number}{row.settlementDate && ` · ${formatDateAr(row.settlementDate)}`}</small>
          <small><bdi>{row.paymentReference}</bdi></small>{row.reused && <small>محفوظ سابقًا</small>}
        </> },
        { key: "result", header: "نتيجة المقارنة", render: row => statementResult[row.result] ?? "تحتاج مراجعة" },
        { key: "gross", header: "الإجمالي", render: row => formatMoneyAr(row.gross, row.currency) },
        { key: "fee", header: "الرسوم", render: row => formatMoneyAr(row.fee, row.currency) },
        { key: "net", header: "الصافي", render: row => formatMoneyAr(row.net, row.currency) },
        { key: "expected", header: "التحصيل المسجل", render: row => row.expected != null ? formatMoneyAr(row.expected, row.expectedCurrency) : "لا يوجد تطابق" },
      ]} />
    </section>}
    {!blocked && <form onSubmit={event => { event.preventDefault(); void compare(); }}>
      <fieldset disabled={busy !== null || preview !== null} className={`form-shell ${styles.fields}`}>
        <legend>بيانات الكشف</legend>
        <label>الفرع<select required {...field("branchId")}><option value="">اختر الفرع</option>{options.branches.map(branch => <option key={branch.id} value={branch.id}>{branch.name}</option>)}</select></label>
        <label>مصدر الكشف<select {...field("sourceKind")}><option value="Provider">مزود الدفع</option><option value="Bank">البنك</option></select></label>
        <label>اسم الحساب المختصر<input required maxLength={60} autoComplete="off" {...field("accountTag")} /></label>
        <label>مرجع الكشف<input required maxLength={100} dir="auto" {...field("statementReference")} /></label>
        <label>المزود المسجل<select required {...field("provider")}><option value="">اختر المزود</option>{options.providers.map(provider => <option key={provider} value={provider}>{statementProviderLabel(provider)}</option>)}</select></label>
        <div className={styles.csvFile}>
          <div>
            <p id="statement-csv-help">استخدم أعمدة القالب، حتى 250 سطرًا و128 كيلوبايت بترميز UTF-8. تجنّب أرقام الحسابات والبطاقات.</p>
            <a className="secondary-button" href={`${api}/import/template`} download>تنزيل قالب CSV</a>
          </div>
          <label>ملف الكشف CSV
            <FilePicker key={pickerVersion} aria-label="ملف الكشف CSV" aria-describedby="statement-csv-help" accept=".csv,text/csv"
              disabled={busy !== null || preview !== null} onChange={event => void loadFile(event.target.files?.[0])} />
          </label>
          <span role="status" className="sr-only">{file ? `الملف المختار: ${file.name}` : reading ? "جارٍ قراءة الملف…" : ""}</span>
        </div>
      </fieldset>
      {!preview && <button className="primary-button" type="submit" disabled={busy !== null || reading || !file}>
        {busy === "preview" ? "جارٍ تحميل المعاينة…" : "معاينة الكشف"}
      </button>}
    </form>}
  </section>;
}
