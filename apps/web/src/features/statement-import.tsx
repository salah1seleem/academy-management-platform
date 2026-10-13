"use client";
import Link from "next/link";
import { useRef, useState } from "react";
import { AdminDataTable, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { formatMoneyAr } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";
import { statementApi as api, statementRoute as route, statementResult, useStatementRead, type StatementOptions } from "./settlement-comparisons";
import styles from "./settlement-comparisons.module.css";

type Line = { number: number; id?: string; reused: boolean; lineReference: string; paymentReference: string; result: string; gross: string; fee: string; net: string; currency: string; expected?: string; expectedCurrency?: string };
type Preview = { quoteHash: string; count: number; reused: number; matched: number; differences: number; items: Line[] };
type Saved = { count: number; created: number; reused: number; items: { id: string; lineReference: string; result: string }[] };
export function StatementImport() {
  const { data, error } = useStatementRead<StatementOptions>(`${api}/options`);
  return <><PageHeader title="استيراد كشف للمقارنة" context="المالية / مطابقة كشوف التحصيل" /><Link className="secondary-button" href={route}>العودة للمطابقة</Link>
    <aside className={styles.notice}><strong>معاينة واضحة، ثم حفظ الدفعة كاملة</strong><p>قارن حتى 250 سطرًا من حساب وفرع ومزود واحد. لا يؤكد الاستيراد دفعًا ولا يغيّر إيصالًا أو اشتراكًا أو رصيدًا. الرسوم معلومات للمراجعة، وليست مصروفًا مُرحّلًا.</p></aside>
    {error ? <ErrorState message={error} /> : !data ? <LoadingState /> : <ImportForm options={data} />}</>;
}
function ImportForm({ options }: { options: StatementOptions }) {
  const [values, setValues] = useState({ branchId: "", sourceKind: "Provider", accountTag: "", statementReference: "", provider: "", csvText: "" });
  const [fileName, setFileName] = useState(""), [error, setError] = useState(""), [busy, setBusy] = useState(false), [preview, setPreview] = useState<Preview | null>(null), [issued, setIssued] = useState(false), [saved, setSaved] = useState<Saved | null>(null);
  const fileGeneration = useRef(0);
  const field = (key: keyof typeof values) => ({ value: values[key], onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setValues(v => ({ ...v, [key]: e.target.value })) });
  async function loadFile(file?: File) {
    const generation = ++fileGeneration.current; setError(""); setFileName(""); setBusy(false); setValues(v => ({ ...v, csvText: "" }));
    if (!file) return;
    if (!file.name.toLowerCase().endsWith(".csv") || file.size > 131072) { setError("اختر ملف CSV بترميز UTF-8 لا يتجاوز 128 KiB."); return; }
    setBusy(true);
    try { const text = new TextDecoder("utf-8", { fatal: true }).decode(await file.arrayBuffer()); if (generation === fileGeneration.current) { setValues(v => ({ ...v, csvText: text })); setFileName(file.name); } }
    catch { if (generation === fileGeneration.current) setError("تعذر قراءة الملف. احفظه CSV بترميز UTF-8 ثم حاول مجددًا."); }
    finally { if (generation === fileGeneration.current) setBusy(false); }
  }
  async function compare() {
    if (busy || !values.csvText) return; setError(""); setBusy(true);
    try { setPreview(await (await csrfRequest(`${api}/import/preview`, "POST", values)).json()); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر معاينة الكشف."); }
    finally { setBusy(false); }
  }
  async function save() {
    if (busy || !preview) return; setBusy(true); setIssued(true); setError("");
    try { setSaved(await (await csrfRequest(`${api}/import`, "POST", { ...values, quoteHash: preview.quoteHash })).json()); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر تأكيد الحفظ. أعد المحاولة بنفس الكشف."); }
    finally { setBusy(false); }
  }
  if (saved) return <section className="detail-card"><h2>تم حفظ مقارنة الكشف</h2><p role="status">{saved.created} سطر جديد · {saved.reused} سطر محفوظ سابقًا · دون تغيير التحصيلات</p><AdminDataTable label="نتائج الكشف المحفوظة" rows={saved.items} rowKey={r => r.id} columns={[{ key: "line", header: "مرجع السطر", primary: true, render: r => r.lineReference }, { key: "result", header: "نتيجة المقارنة", render: r => statementResult[r.result] }, { key: "action", header: "التفاصيل", render: r => <Link href={`${route}/${r.id}`}>عرض المقارنة</Link> }]} /><Link className="primary-button" href={route}>العودة للمقارنات</Link></section>;
  return <section className="detail-card">{error && <ErrorState message={error} />}<form onSubmit={e => { e.preventDefault(); void compare(); }}>
    <fieldset disabled={busy || !!preview} className={`form-shell ${styles.fields}`}><legend>1. حدّد مصدر الكشف</legend>
      <label>الفرع<select required {...field("branchId")}><option value="">اختر الفرع</option>{options.branches.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label>
      <label>مصدر الكشف<select {...field("sourceKind")}><option value="Provider">مزود الدفع</option><option value="Bank">البنك</option></select></label>
      <label>اسم الحساب المختصر<input required maxLength={60} autoComplete="off" {...field("accountTag")} /></label><label>مرجع الكشف<input required maxLength={100} {...field("statementReference")} /></label>
      <label>المزود المسجل<select required {...field("provider")}><option value="">اختر المزود</option>{options.providers.map(p => <option key={p} value={p}>{p}</option>)}</select></label>
      <label className={styles.upload}>ملف الكشف CSV<input aria-label="ملف الكشف CSV" className={styles.fileInput} type="file" accept=".csv,text/csv" onChange={e => void loadFile(e.target.files?.[0])} /><span aria-hidden="true" className={styles.uploadButton}>اختيار ملف CSV</span></label>
    </fieldset><div className={styles.notice}><strong>2. استخدم القالب، دون أسرار أو بيانات بطاقة</strong><p>كل صف: مرجع سطر فريد، مرجع دفع، تاريخ <bdi dir="ltr">YYYY-MM-DD</bdi>، إجمالي، رسوم، صافي، عملة، ملاحظة اختيارية. المبالغ بنقطة عشرية. المثال اصطناعي، ولا يُرفع ملف البنك الأصلي قبل تحويله لهذه الأعمدة.</p><a className="secondary-button" href={`${api}/import/template`} download>تنزيل قالب CSV</a>{fileName && <p role="status">الملف المختار: {fileName}</p>}</div>
    {!preview && <button className="primary-button" disabled={busy || !values.csvText}>معاينة الكشف</button>}</form>
    {preview && <section className={styles.preview}><h2>3. راجع نتائج الكشف</h2><div className="cash-session-stats"><div><small>كل الأسطر</small><strong>{preview.count}</strong></div><div><small>مطابق يدويًا</small><strong>{preview.matched}</strong></div><div><small>تحتاج مراجعة</small><strong>{preview.differences}</strong></div></div><p>{preview.reused} سطر محفوظ سابقًا لن يتكرر. تُحفظ الاختلافات كما هي دون اعتبارها دفعًا مؤكدًا.</p>
      <AdminDataTable label="معاينة كشف التسوية" rows={preview.items} rowKey={r => String(r.number)} columns={[
        { key: "line", header: "السطر والمرجع", primary: true, render: r => <><strong>{r.lineReference}</strong><small dir="ltr">{r.paymentReference}</small>{r.reused && <small>محفوظ سابقًا</small>}</> },
        { key: "result", header: "نتيجة المقارنة", render: r => statementResult[r.result] }, { key: "gross", header: "الإجمالي", render: r => formatMoneyAr(r.gross, r.currency) }, { key: "fee", header: "الرسوم", render: r => formatMoneyAr(r.fee, r.currency) }, { key: "net", header: "الصافي", render: r => formatMoneyAr(r.net, r.currency) }, { key: "expected", header: "التحصيل المسجل", render: r => r.expected ? formatMoneyAr(r.expected, r.expectedCurrency) : "لا يوجد تطابق" },
      ]} /><div className="form-actions"><button className="primary-button" disabled={busy} onClick={() => void save()}>{issued ? "إعادة تأكيد الحفظ بنفس الكشف" : "حفظ نتائج الكشف كاملة"}</button>{!issued && <button className="secondary-button" disabled={busy} onClick={() => setPreview(null)}>تعديل الكشف</button>}</div>{issued && <p>إذا طلب الخادم معاينة جديدة، ارجع للقائمة ثم افتح الاستيراد مجددًا. لا تُغيّر الكشف أثناء إعادة تأكيد طلب غير معلوم النتيجة.</p>}
    </section>}
  </section>;
}
