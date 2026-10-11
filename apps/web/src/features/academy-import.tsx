"use client";
import { useEffect, useRef, useState } from "react";
import { CheckCheck, Download, FileSpreadsheet, ShieldCheck, UploadCloud } from "lucide-react";
import { AdminDataTable, ErrorState, PageHeader } from "../components/dashboard-shell";
type Issue = { sheet: string; sheetLabel?: string; row: number; column: string; columnLabel?: string; message: string };
type PreviewRow = { row: number; legacyPlayerId: string; legacyEnrollmentId: string; playerName: string; guardianName: string; guardianPhone: string; agreedNet: string | null; currency: string; existingNew: string };
type Preview = { schemaVersion: string; fileHash: string; referenceStamp: string; sourceSystem: string; cutoffDate: string; rowCount: number; playerCount: number; householdCount: number; financiallyIncomplete: number; canApprove: boolean; errors: Issue[]; rows: PreviewRow[] };
export function AcademyImport() {
  const input = useRef<HTMLInputElement>(null), request = useRef<AbortController | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState("");
  useEffect(() => () => request.current?.abort(), []);
  async function upload(file?: File) {
    if (!file || busy) return;
    setPreview(null); setError("");
    if (!file.name.toLowerCase().endsWith(".xlsx") || file.size > 5 * 1024 * 1024 || file.size === 0) { setError("اختر ملف xlsx صالحًا بحد أقصى 5 ميجابايت."); return; }
    setBusy(true); const controller = new AbortController(); request.current = controller;
    try {
      const csrf = await fetch("/api/v1/auth/csrf", { signal: controller.signal }); if (!csrf.ok) throw new Error("تعذر بدء المعاينة الآمنة.");
      const { token } = await csrf.json(); const data = new FormData(); data.set("file", file);
      const response = await fetch("/api/v1/people/import/preview", { method: "POST", body: data, headers: { "X-CSRF-TOKEN": token }, signal: controller.signal });
      const value = await response.json(); if (!response.ok) throw new Error(value.title ?? "تعذر فحص الملف. تحقق من الصلاحية والقالب ثم حاول مجددًا.");
      if (!Array.isArray(value.errors) || !Array.isArray(value.rows)) throw new Error("تعذر قراءة نتيجة الفحص.");
      setPreview(value);
    } catch (caught) { if (!controller.signal.aborted) setError(caught instanceof Error && /[\u0600-\u06ff]/.test(caught.message) ? caught.message : "تعذر الاتصال أثناء المعاينة. أعد المحاولة؛ لم يتم اعتماد الملف."); }
    finally { if (!controller.signal.aborted) setBusy(false); if (input.current) input.current.value = ""; }
  }
  function downloadErrors() {
    if (!preview) return;
    const cell = (value: string | number) => `"${String(value).replace(/^[=+@-]/, "'$&").replaceAll('"', '""')}"`;
    const rows = [["الورقة", "الصف", "الحقل", "المشكلة"], ...preview.errors.map(e => [e.sheetLabel ?? e.sheet, e.row, e.columnLabel ?? e.column, e.message])];
    const url = URL.createObjectURL(new Blob(["\uFEFF" + rows.map(r => r.map(cell).join(",")).join("\n")], { type: "text/csv;charset=utf-8" })); const a = document.createElement("a"); a.href = url; a.download = "academity-import-errors.csv"; a.click(); URL.revokeObjectURL(url);
  }
  return <><PageHeader title="استيراد بيانات الأكاديمية" context="الإعدادات والمساعدة / نقل البيانات" description="قالب خاص بأكاديميتك، ثم معاينة واضحة قبل اعتماد أي تغيير." />
    <section className="import-start-card"><div className="import-start-icon"><FileSpreadsheet size={32} /></div><div><h2>ابدأ من ملف واحد</h2><p>حتى 1000 تسجيل. الطفل نفسه يحتفظ بكوده عبر الرياضات، والأسرة تربط بكود واضح بعد مراجعة الإدارة.</p></div><div className="form-actions"><a className="secondary-button" href="/api/v1/people/import/template"><Download size={18} /> تنزيل قالب أكاديميتي</a><button className="primary-button" disabled={busy} onClick={() => input.current?.click()}><UploadCloud size={18} /> {busy ? "جارٍ فحص الملف…" : "استيراد ملف"}</button><input ref={input} className="import-file-input" type="file" accept=".xlsx" aria-label="ملف بيانات الأكاديمية" disabled={busy} onChange={e => void upload(e.target.files?.[0])} /></div></section>
    <ol className="import-steps"><li><span>1</span><b>نزّل القالب</b><small>المراجع والأسعار والسعة الحالية</small></li><li><span>2</span><b>املأ البيانات</b><small>أكواد ثابتة وهاتف كنص وتاريخ ISO</small></li><li><span>3</span><b>راجع المعاينة</b><small>صحّح المشكلات قبل الاعتماد</small></li></ol>
    <p className="import-privacy-note"><ShieldCheck size={18} />المعاينة لا تنشئ لاعبين أو تحصيلات ولا ترسل رسائل. لا تستخدم بيانات أطفال حقيقية في Demo.</p>
    {error && <ErrorState message={error} />}{busy && <p role="status" className="notice">نفحص البنية والعلاقات والأرصدة. احتفظ بالصفحة مفتوحة.</p>}
    {preview && <section className="list-card" aria-label="نتيجة معاينة الاستيراد"><header className="import-result-heading"><div><h2>{preview.canApprove ? "اكتملت المعاينة دون أخطاء" : "الملف يحتاج تصحيحًا"}</h2><p>{preview.sourceSystem || "مصدر غير محدد"} · {preview.cutoffDate || "تاريخ قطع غير محدد"}</p></div>{preview.errors.length > 0 ? <button className="secondary-button" onClick={downloadErrors}><Download size={16} /> تنزيل تقرير الأخطاء</button> : <CheckCheck aria-label="البيانات اجتازت فحص المعاينة" />}</header>
      <div className="import-counts"><article><strong>{preview.rowCount}</strong><span>تسجيل</span></article><article><strong>{preview.playerCount}</strong><span>طفل مختلف</span></article><article><strong>{preview.householdCount}</strong><span>أسرة برمز</span></article><article><strong>{preview.financiallyIncomplete}</strong><span>تحتاج مراجعة مالية</span></article></div>
      {preview.errors.length > 0 && <><p className="notice">لم يُحفظ أي سجل. نعرض أول 300 مشكلة؛ صحّح الملف وأعد رفعه.</p><AdminDataTable label="مشكلات الاستيراد" rows={preview.errors} rowKey={r => `${r.sheet}-${r.row}-${r.column}-${r.message}`} columns={[{ key: "row", header: "الورقة والصف", primary: true, render: r => <>{r.sheetLabel ?? r.sheet} · {r.row}<small>{r.columnLabel ?? r.column}</small></> }, { key: "problem", header: "التصحيح المطلوب", render: r => r.message }]} /></>}
      {preview.rows.length > 0 && <><h3>التسجيلات التي اجتازت الفحص</h3><p className="muted">هذه معاينة وليست نتيجة استيراد معتمدة. السعر التاريخي محفوظ دون إعادة الخصم.</p><AdminDataTable label="معاينة التسجيلات" rows={preview.rows} rowKey={r => r.legacyEnrollmentId} columns={[
        { key: "player", header: "اللاعب", primary: true, render: r => <><b>{r.playerName}</b><small>{r.legacyPlayerId}</small></> },
        { key: "guardian", header: "ولي الأمر", render: r => <>{r.guardianName}<small dir="ltr">{r.guardianPhone}</small></> },
        { key: "net", header: "الصافي التاريخي", render: r => r.agreedNet === null ? "غير معروف — مراجعة مالية" : `${r.agreedNet} ${r.currency}` },
      ]} /></>}
    </section>}
  </>;
}
