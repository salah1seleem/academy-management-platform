"use client";
import { useEffect, useRef, useState } from "react";
import { CheckCheck, Download, FileSpreadsheet, ShieldCheck, UploadCloud } from "lucide-react";
import Link from "next/link";
import { AdminDataTable, ErrorState, PageHeader } from "../components/dashboard-shell";
type Issue = { sheet: string; sheetLabel?: string; row: number; column: string; columnLabel?: string; message: string };
type PreviewRow = { row: number; legacyPlayerId: string; legacyEnrollmentId: string; playerName: string; guardianName: string; guardianPhone: string; agreedNet: string | null; currency: string; existingNew: string };
type Preview = { schemaVersion: string; fileHash: string; referenceStamp: string; sourceSystem: string; cutoffDate: string; rowCount: number; playerCount: number; householdCount: number; financiallyIncomplete: number; canApprove: boolean; errors: Issue[]; rows: PreviewRow[]; guardianMatches?: { householdKey: string; guardianName: string; phone: string; candidates: string[] }[] };
type Job = { id: string; sourceSystem: string; status: string; rowCount: number; publishedRows: number; replayedRows: number; financiallyIncomplete: number; attempts: number; message?: string };
type Published = { id: string; legacyEnrollmentId: string; player: string; playerCode: string; sport: string; branch: string; groupName: string; priceSnapshot: string | null; currencySnapshot: string };
export function AcademyImport() {
  const input = useRef<HTMLInputElement>(null), request = useRef<AbortController | null>(null);
  const fileRef = useRef<File | null>(null), intent = useRef("");
  const generation = useRef(0);
  const [preview, setPreview] = useState<Preview | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState("");
  const [job, setJob] = useState<Job | null>(null), [published, setPublished] = useState<Published[]>([]), [approved, setApproved] = useState(false), [balancesApproved, setBalancesApproved] = useState(false), [financialReview, setFinancialReview] = useState(false), [resolutions, setResolutions] = useState<Record<string, string>>({});
  useEffect(() => () => request.current?.abort(), []);
  useEffect(() => { const controller = new AbortController(); void fetch("/api/v1/people/import/jobs", { signal: controller.signal, cache: "no-store" }).then(async r => { if (r.ok) { const value = await r.json(); if (!controller.signal.aborted && generation.current === 0 && Array.isArray(value) && value[0]?.id) setJob(value[0]); } }).catch(() => {}); return () => controller.abort(); }, []);
  const jobId = job?.id, jobState = job?.status;
  useEffect(() => {
    if (!jobId || !["Queued", "Running"].includes(jobState ?? "")) return;
    const controller = new AbortController(); const timer = setInterval(() => { void fetch(`/api/v1/people/import/jobs/${jobId}`, { signal: controller.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw new Error(); const next = await r.json(); if (!controller.signal.aborted) { setJob(next); setError(""); } }).catch(() => { if (!controller.signal.aborted) setError("تعذر تحديث حالة الدفعة. لم نطلب استيرادًا جديدًا؛ أعد فتح الصفحة لمتابعتها."); }); }, 2000);
    return () => { clearInterval(timer); controller.abort(); };
  }, [jobId, jobState]);
  useEffect(() => {
    if (!jobId || jobState !== "Completed") return;
    const controller = new AbortController(); void fetch(`/api/v1/people/import/jobs/${jobId}/records`, { signal: controller.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw new Error(); const rows = await r.json(); if (!controller.signal.aborted && Array.isArray(rows)) setPublished(rows); }).catch(() => { if (!controller.signal.aborted) setError("اكتمل الاستيراد لكن تعذر تحميل تفاصيله. أعد فتح الصفحة لمراجعتها دون إعادة رفع الملف."); }); return () => controller.abort();
  }, [jobId, jobState]);
  async function upload(file?: File) {
    if (!file || busy) return;
    generation.current++;
    setPreview(null); setError(""); setJob(null); setPublished([]); setApproved(false); setBalancesApproved(false); setFinancialReview(false); setResolutions({}); fileRef.current = file; intent.current = crypto.randomUUID();
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
  async function commit() {
    if (!preview?.canApprove || !fileRef.current || busy || !approved) return;
    const controller = new AbortController(); request.current = controller; setBusy(true); setError("");
    try {
      const csrf = await fetch("/api/v1/auth/csrf", { signal: controller.signal }); if (!csrf.ok) throw new Error(); const { token } = await csrf.json();
      const form = new FormData(); form.set("file", fileRef.current); form.set("ExpectedFileHash", preview.fileHash); form.set("ExpectedReferenceStamp", preview.referenceStamp); form.set("GuardianLinksApproved", String(approved)); form.set("OpeningBalancesApproved", String(balancesApproved)); form.set("AcceptFinancialReview", String(financialReview)); form.set("GuardianResolutions", JSON.stringify(Object.entries(resolutions).filter(([, guardianId]) => guardianId).map(([householdKey, guardianId]) => ({ householdKey, guardianId }))));
      const response = await fetch("/api/v1/people/import/commit", { method: "POST", body: form, headers: { "X-CSRF-TOKEN": token, "Idempotency-Key": intent.current }, signal: controller.signal }); const value = await response.json();
      if (!response.ok) throw new Error(value.title ?? value.message ?? "تعذر اعتماد الملف. أعد المعاينة.");
      setJob(value); setPreview(null); fileRef.current = null;
    } catch (e) { if (!controller.signal.aborted) setError(e instanceof Error && /[\u0600-\u06ff]/.test(e.message) ? e.message : "تعذر تأكيد الاستيراد. أعد المحاولة بنفس الصفحة لتجنب التكرار."); }
    finally { if (!controller.signal.aborted) setBusy(false); }
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
    {job && <section className="detail-card" aria-label="نتيجة دفعة الاستيراد"><h2>{job.status === "Completed" ? "اكتمل الاستيراد" : job.status === "Queued" ? "الدفعة في انتظار التنفيذ" : job.status === "Running" ? "جارٍ اعتماد الدفعة كاملة" : "لم تُنشر الدفعة"}</h2><p>{job.sourceSystem} · {job.rowCount} تسجيل</p>{["Queued", "Running"].includes(job.status) ? <p role="status">يستمر التنفيذ بأمان عند إغلاق الصفحة. لن تظهر بيانات جزئية قبل اكتمال المعاملة.</p> : job.message ? <ErrorState message={job.message} /> : <p role="status">تم إنشاء {job.publishedRows} تسجيل، والتعرف على {job.replayedRows} تسجيل سابق دون تكرار. لا تحصيلات أو إيصالات مصطنعة.</p>}{job.financiallyIncomplete > 0 && <p className="notice">{job.financiallyIncomplete} تسجيل يحتاج مراجعة مالية. الصافي غير المعروف ليس صفرًا.</p>}
      {published.length > 0 && <AdminDataTable label="نتيجة الاستيراد المعتمدة" rows={published} rowKey={r => r.legacyEnrollmentId} columns={[{ key: "player", header: "اللاعب", primary: true, render: r => <Link href={`/dashboard/players/${r.id}`}><b>{r.player}</b><small>{r.playerCode}</small></Link> }, { key: "service", header: "التسجيل", render: r => <>{r.sport}<small>{r.branch} · {r.groupName}</small></> }, { key: "net", header: "اتفاق تاريخي — ليس قبضًا", render: r => r.priceSnapshot == null ? "غير معروف — مراجعة مالية" : `${r.priceSnapshot} ${r.currencySnapshot}` }]} />}
    </section>}
    {preview && <section className="list-card" aria-label="نتيجة معاينة الاستيراد"><header className="import-result-heading"><div><h2>{preview.canApprove ? "اكتملت المعاينة دون أخطاء" : "الملف يحتاج تصحيحًا"}</h2><p>{preview.sourceSystem || "مصدر غير محدد"} · {preview.cutoffDate || "تاريخ قطع غير محدد"}</p></div>{preview.errors.length > 0 ? <button className="secondary-button" onClick={downloadErrors}><Download size={16} /> تنزيل تقرير الأخطاء</button> : <CheckCheck aria-label="البيانات اجتازت فحص المعاينة" />}</header>
      <div className="import-counts"><article><strong>{preview.rowCount}</strong><span>تسجيل</span></article><article><strong>{preview.playerCount}</strong><span>طفل مختلف</span></article><article><strong>{preview.householdCount}</strong><span>أسرة برمز</span></article><article><strong>{preview.financiallyIncomplete}</strong><span>تحتاج مراجعة مالية</span></article></div>
      {preview.errors.length > 0 && <><p className="notice">لم يُحفظ أي سجل. نعرض أول 300 مشكلة؛ صحّح الملف وأعد رفعه.</p><AdminDataTable label="مشكلات الاستيراد" rows={preview.errors} rowKey={r => `${r.sheet}-${r.row}-${r.column}-${r.message}`} columns={[{ key: "row", header: "الورقة والصف", primary: true, render: r => <>{r.sheetLabel ?? r.sheet} · {r.row}<small>{r.columnLabel ?? r.column}</small></> }, { key: "problem", header: "التصحيح المطلوب", render: r => r.message }]} /></>}
      {preview.canApprove && <section className="import-approval"><h3>اعتماد الدفعة كاملة</h3><p>راجع الطفل والأسرة والمجموعة والسعر. تسجيل الخدمة لا يعني إثبات دفع سابق، ولا يرسل رسائل أو يتجاوز تحقق الدخول.</p>
        {(preview.guardianMatches ?? []).map(m => <label key={m.householdKey}>مراجعة رابط {m.guardianName} · <span dir="ltr">{m.phone}</span><select value={resolutions[m.householdKey] ?? ""} onChange={e => setResolutions({ ...resolutions, [m.householdKey]: e.target.value })}><option value="">اختر بعد التحقق من علاقة ولي الأمر</option>{m.candidates.map(id => <option key={id} value={id}>{m.guardianName} — ملف قائم بالأكاديمية</option>)}</select>{m.candidates.length === 0 && <small>البيانات متعارضة؛ يلزم تصحيحها قبل الربط.</small>}</label>)}
        <label className="checkbox-line"><input type="checkbox" checked={approved} onChange={e => setApproved(e.target.checked)} />راجعت علاقات أولياء الأمور وأعتمد الربط بهذه الدفعة، وليس بالهاتف وحده.</label>
        <label className="checkbox-line"><input type="checkbox" checked={balancesApproved} onChange={e => setBalancesApproved(e.target.checked)} />راجعت الأرصدة الافتتاحية وسند اعتمادها إن كانت موجودة — ليست قبض اليوم.</label>
        {preview.financiallyIncomplete > 0 && <label className="checkbox-line"><input type="checkbox" checked={financialReview} onChange={e => setFinancialReview(e.target.checked)} />أقر أن {preview.financiallyIncomplete} تسجيل يحتاج مراجعة مالية قبل الاعتماد المالي النهائي.</label>}
        <button className="primary-button" disabled={busy || !approved || preview.financiallyIncomplete > 0 && !financialReview} onClick={() => void commit()}>{busy ? "جارٍ اعتماد الطلب…" : "اعتماد وبدء الاستيراد"}</button>
      </section>}
      {preview.rows.length > 0 && <><h3>التسجيلات التي اجتازت الفحص</h3><p className="muted">هذه معاينة وليست نتيجة استيراد معتمدة. السعر التاريخي محفوظ دون إعادة الخصم.</p><AdminDataTable label="معاينة التسجيلات" rows={preview.rows} rowKey={r => r.legacyEnrollmentId} columns={[
        { key: "player", header: "اللاعب", primary: true, render: r => <><b>{r.playerName}</b><small>{r.legacyPlayerId}</small></> },
        { key: "guardian", header: "ولي الأمر", render: r => <>{r.guardianName}<small dir="ltr">{r.guardianPhone}</small></> },
        { key: "net", header: "الصافي التاريخي", render: r => r.agreedNet === null ? "غير معروف — مراجعة مالية" : `${r.agreedNet} ${r.currency}` },
      ]} /></>}
    </section>}
  </>;
}
