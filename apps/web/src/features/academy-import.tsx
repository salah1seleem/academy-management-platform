"use client";
import { Fragment, useEffect, useRef, useState } from "react";
import { CheckCheck, Download, UploadCloud } from "lucide-react";
import Link from "next/link";
import { ApprovedWorkspace } from "../components/approved-workspace";
import styles from "./academy-import.module.css";

import { MoneyValue } from "../components/money-value";
import { AdminDataTable, ErrorState, PageHeader } from "../components/dashboard-shell";
type Issue = { sheet: string; sheetLabel?: string; row: number; column: string; columnLabel?: string; message: string };
type PreviewRow = { row: number; legacyPlayerId: string; legacyEnrollmentId: string; playerName: string; guardianName: string; guardianPhone: string; agreedNet: string | null; currency: string; existingNew: string };
type ImportEffect={sheet:string;create:number;reuse:number};
type ImportSchema={rehearsalAvailable?:boolean;maxRows:number;maxFileBytes:number;connectedSheets:{name:string;optional:boolean;columns:{key:string;label:string;required:boolean}[]}[]};
type Preview = { accountLinks?:{role:string;name:string;phone:string}[]; guardianCount?:number;guardianRelationships?:number;historical?:{periods:number;sessions:number;attendance:number;evaluations:number;published:number;drafts:number;scores:number}; historicalPayments?:number;historicalPaymentTotal?:number; effects?:ImportEffect[];coachAssignments?:number;tournamentParticipations?:number; schemaVersion: string; fileHash: string; referenceStamp: string; sourceSystem: string; cutoffDate: string; rowCount: number; playerCount: number; householdCount: number; financiallyIncomplete: number; canApprove: boolean; errors: Issue[]; rows: PreviewRow[]; guardianMatches?: { householdKey: string; guardianName: string; phone: string; candidates: string[] }[] };
type Job = { replayedRequest?:boolean; result?:{effects?:ImportEffect[];coachAssignments:number;tournamentParticipations:number;players:number;enrollments:number;historicalPayments?:number;historicalPaymentTotal?:number}; id: string; sourceSystem: string; status: string; rowCount: number; publishedRows: number; replayedRows: number; financiallyIncomplete: number; attempts: number; message?: string };
type Published = { id: string; legacyEnrollmentId: string; player: string; playerCode: string; sport: string; branch: string; groupName: string; priceSnapshot: string | null; currencySnapshot: string };
export function AcademyImport({ academyId, onCompleted }: { academyId?: string; onCompleted?:()=>void } = {}) {
  const notifiedCompletion=useRef<string|null>(null);
  const apiBase = academyId ? `/api/v1/platform/academies/${academyId}/import` : "/api/v1/people/import";
  const csrfPath = academyId ? "/api/v1/platform/auth/csrf" : "/api/v1/auth/csrf";
  const Wrapper = academyId ? Fragment : ApprovedWorkspace;
  const [newImport,setNewImport]=useState(false);
  const resultRef=useRef<HTMLElement>(null);
  const [selectedFile,setSelectedFile]=useState<File|null>(null),[downloadBusy,setDownloadBusy]=useState(false);
  const [schema,setSchema]=useState<ImportSchema|null>(null);
  const input = useRef<HTMLInputElement>(null), request = useRef<AbortController | null>(null);
  const fileRef = useRef<File | null>(null), intent = useRef("");
  const generation = useRef(0);
  const [preview, setPreview] = useState<Preview | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState("");
  const [job, setJob] = useState<Job | null>(null), [published, setPublished] = useState<Published[]>([]), [approved, setApproved] = useState(false), [balancesApproved, setBalancesApproved] = useState(false), [paymentsApproved,setPaymentsApproved] = useState(false), [financialReview, setFinancialReview] = useState(false), [resolutions, setResolutions] = useState<Record<string, string>>({});
  useEffect(()=>{const controller=new AbortController();fetch(`${apiBase}/schema`,{signal:controller.signal,cache:"no-store"}).then(r=>r.ok?r.json():null).then(value=>{if(!controller.signal.aborted&&Array.isArray(value?.connectedSheets))setSchema(value);}).catch(()=>{});return()=>controller.abort();},[apiBase]);
  useEffect(() => () => request.current?.abort(), []);
  useEffect(() => { const controller = new AbortController(); void fetch(`${apiBase}/jobs`, { signal: controller.signal, cache: "no-store" }).then(async r => { if (r.ok) { const value = await r.json(); if (!controller.signal.aborted && generation.current === 0 && Array.isArray(value) && value[0]?.id) setJob(value[0]); } }).catch(() => {}); return () => controller.abort(); }, [apiBase]);
  const jobId = job?.id, jobState = job?.status;
  useEffect(()=>{if(jobId&&jobState==="Completed"&&notifiedCompletion.current!==jobId){notifiedCompletion.current=jobId;onCompleted?.();}},[jobId,jobState,onCompleted]);
  useEffect(() => {
    if (!jobId || !["Queued", "Running"].includes(jobState ?? "")) return;
    const controller = new AbortController(); const timer = setInterval(() => { void fetch(`${apiBase}/jobs/${jobId}`, { signal: controller.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw new Error(); const next = await r.json(); if (!controller.signal.aborted) { setJob(next); setError(""); } }).catch(() => { if (!controller.signal.aborted) setError("تعذر تحديث حالة الدفعة. لم نطلب استيرادًا جديدًا؛ أعد فتح الصفحة لمتابعتها."); }); }, 2000);
    return () => { clearInterval(timer); controller.abort(); };
  }, [apiBase, jobId, jobState]);
  useEffect(() => {
    if (!jobId || jobState !== "Completed") return;
    const controller = new AbortController(); void fetch(`${apiBase}/jobs/${jobId}/records`, { signal: controller.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw new Error(); const rows = await r.json(); if (!controller.signal.aborted && Array.isArray(rows)) setPublished(rows); }).catch(() => { if (!controller.signal.aborted) setError("اكتمل الاستيراد لكن تعذر تحميل تفاصيله. أعد فتح الصفحة لمراجعتها دون إعادة رفع الملف."); }); return () => controller.abort();
  }, [apiBase, jobId, jobState]);
  useEffect(()=>{if(preview||error)resultRef.current?.scrollIntoView?.({behavior:"smooth",block:"start"});},[preview,error]);
  async function download(kind:"connected-template"|"connected-sample") {
    if(downloadBusy)return;setDownloadBusy(true);setError("");
    try {const r=await fetch(`${apiBase}/${kind}`,{cache:"no-store"});
      if(r.status===401)throw new Error("انتهت جلسة الدخول. سجّل الدخول مجددًا ثم أعد التنزيل.");
      if(!r.ok){const value=await r.json().catch(()=>({}));throw new Error(value.title??value.message??"تعذر تنزيل الملف. راجع إعدادات الأكاديمية ثم أعد المحاولة.");}
      if(!r.headers.get("content-type")?.includes("spreadsheetml.sheet"))throw new Error("تعذر تنزيل ملف Excel صالح. أعد المحاولة.");
      const blob=await r.blob(),url=URL.createObjectURL(blob),a=document.createElement("a");a.href=url;a.download=kind==="connected-template"?"i-academy-one-blank.xlsx":"i-academy-one-completed-50.xlsx";a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
    }catch(e){setError(e instanceof Error&&/[\u0600-\u06ff]/.test(e.message)?e.message:"تعذر الاتصال أثناء التنزيل. أعد المحاولة.");}finally{setDownloadBusy(false);}
  }
  function selectFile(file?:File){if(!file)return;generation.current++;setNewImport(false);setSelectedFile(file);setPreview(null);setJob(null);setPublished([]);setError("");fileRef.current=null;}
  async function upload(file?: File) {
    if (!file || busy) return;
    setNewImport(false);
    generation.current++;
    setPreview(null); setError(""); setJob(null); setPublished([]); setApproved(false); setBalancesApproved(false); setPaymentsApproved(false); setFinancialReview(false); setResolutions({}); fileRef.current = file; intent.current = crypto.randomUUID();
    if (!file.name.toLowerCase().endsWith(".xlsx") || file.size > 5 * 1024 * 1024 || file.size === 0) { setError("اختر ملف xlsx صالحًا بحد أقصى 5 ميجابايت."); return; }
    setBusy(true); const controller = new AbortController(); request.current = controller;
    try {
      const csrf = await fetch(csrfPath, { signal: controller.signal }); if (!csrf.ok) throw new Error("تعذر بدء المعاينة الآمنة.");
      const { token } = await csrf.json(); const data = new FormData(); data.set("file", file);
      if(academyId){
        const preparation=await fetch(`/api/v1/platform/academies/${academyId}/prepare-operation`,{method:"POST",headers:{"X-CSRF-TOKEN":token},signal:controller.signal});
        if(!preparation.ok){const failure=await preparation.json();throw new Error(failure.message??failure.title??"تعذر تجهيز الأكاديمية للاستيراد.");}
      }
      const response = await fetch(`${apiBase}/preview`, { method: "POST", body: data, headers: { "X-CSRF-TOKEN": token }, signal: controller.signal });
      if (response.status === 401) throw new Error("انتهت جلسة الدخول. سجّل الدخول مجددًا ثم أعد معاينة الملف؛ لم يُعتمد الاستيراد.");
      if (response.status === 403) throw new Error("لا تملك صلاحية معاينة الاستيراد في هذا الحساب. راجع مسؤول الأكاديمية أو المنصة.");
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
      const csrf = await fetch(csrfPath, { signal: controller.signal }); if (!csrf.ok) throw new Error(); const { token } = await csrf.json();
      const form = new FormData(); form.set("file", fileRef.current); form.set("ExpectedFileHash", preview.fileHash); form.set("ExpectedReferenceStamp", preview.referenceStamp); form.set("GuardianLinksApproved", String(approved)); form.set("OpeningBalancesApproved", String(balancesApproved)); form.set("HistoricalPaymentsApproved",String(paymentsApproved)); form.set("AcceptFinancialReview", String(financialReview)); form.set("GuardianResolutions", JSON.stringify(Object.entries(resolutions).filter(([, guardianId]) => guardianId).map(([householdKey, guardianId]) => ({ householdKey, guardianId }))));
      const response = await fetch(`${apiBase}/commit`, { method: "POST", body: form, headers: { "X-CSRF-TOKEN": token, "Idempotency-Key": intent.current }, signal: controller.signal });
      if (response.status === 401) throw new Error("انتهت جلسة الدخول. سجّل الدخول مجددًا وراجع آخر دفعة قبل إعادة الاعتماد.");
      if (response.status === 403) throw new Error("لا تملك صلاحية اعتماد الاستيراد في هذا الحساب. راجع مسؤول الأكاديمية أو المنصة.");
      const value = await response.json();
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
  return <Wrapper><PageHeader title="استيراد بيانات الأكاديمية" context="الإعدادات والمساعدة / نقل البيانات" description="قالب خاص بأكاديميتك، ثم معاينة واضحة قبل اعتماد أي تغيير." />
    <ol className={styles.steps} aria-label="مراحل الاستيراد">{["القالب والرفع","التحقق","المعاينة والتأكيد","التنفيذ","الملخص"].map((label,index)=><li key={label} aria-current={(newImport?0:job?.status==="Completed"?4:job?3:preview?2:busy?1:0)===index?"step":undefined}>{label}</li>)}</ol>
    {job&&<div className="import-view-actions"><button type="button" className="secondary-button" disabled={busy||["Queued","Running"].includes(job.status)} onClick={()=>setNewImport(!newImport)}>{newImport?"العودة إلى آخر دفعة":"استيراد ملف جديد"}</button></div>}
    {(!job||newImport)&&<section className={styles.setup} onFocusCapture={()=>{generation.current++;}} onPointerDownCapture={()=>{generation.current++;}}><h2>بيانات البداية المترابطة لأكاديمية واحدة</h2><p>المدربون ← المجموعات ← الحصص · أولياء الأمور ← الأبناء ← الاشتراكات · اللاعبون ← البطولات</p>
      <div className={styles.downloads}><button type="button" disabled={downloadBusy} onClick={()=>void download("connected-template")}><Download size={26}/><span><strong>تنزيل قالب فارغ</strong><small>ملف XLSX يشمل اللاعبين وكل أوراق الربط</small></span></button><button type="button" disabled={downloadBusy} onClick={()=>void download("connected-sample")}><Download size={26}/><span><strong>تنزيل نموذج مكتمل</strong><small>50 لاعبًا بأسماء مصرية خيالية، وأسر ومجموعات ومدربون وبطولات</small></span></button></div>
      {schema&&<details className={styles.schema}><summary>الأوراق والأكواد التي تربطها ({schema.connectedSheets.length})</summary><AdminDataTable label="أوراق ملف الأكاديمية" rows={schema.connectedSheets} rowKey={s=>s.name} columns={[{key:"sheet",header:"الورقة",primary:true,render:s=>s.name},{key:"columns",header:"الحقول الأساسية",render:s=>s.columns.filter(c=>c.key!=="ExistingId").slice(0,8).map(c=>c.label+(c.required?" *":"")).join("، ")||"تعليمات وأمثلة الأكواد"},{key:"optional",header:"الاستخدام",render:s=>s.optional?"اختياري بحسب بياناتك":"أساسي عند الربط"}]}/><p>النجمة تحدد الحقل المطلوب. أكواد قصيرة تربط الأوراق؛ الأرقام التعريفية الداخلية ليست مطلوبة. تاريخ واضح وهاتف كنص لحفظ الصفر الأول.</p></details>}
      <button className={styles.upload} aria-label={busy?"جارٍ فحص الملف…":"استيراد ملف"} aria-describedby="import-upload-limit" disabled={busy} onClick={()=>input.current?.click()}><UploadCloud size={28}/><strong>{busy?"جارٍ فحص الملف…":"استيراد ملف"}</strong><small id="import-upload-limit">اختر ملف .xlsx · حتى {schema?schema.maxRows:1000} تسجيل · الحد {schema?Math.floor(schema.maxFileBytes/1024/1024):5} ميجابايت</small></button>
      <input ref={input} className="import-file-input" type="file" accept=".xlsx" aria-label="ملف بيانات الأكاديمية" disabled={busy} onChange={e => selectFile(e.target.files?.[0])} />
      {selectedFile&&<section className={styles.fileStatus} aria-label="الملف المختار"><p role="status"><strong>{selectedFile.name}</strong> · {Math.ceil(selectedFile.size/1024)} كيلوبايت</p><p>{busy?"جارٍ إرسال الملف وفحصه…":preview?"تم فحص الملف. راجع النتيجة أدناه.":"تم اختيار الملف، ولم يُحفظ أي سجل بعد."}</p><button className="primary-button" disabled={busy} onClick={()=>void upload(selectedFile)}>{busy?"جارٍ فحص الملف…":preview?"إعادة فحص الملف":"متابعة لفحص الملف"}</button></section>}

    </section>}
    {error && <section ref={resultRef} tabIndex={-1}><ErrorState message={error} /></section>}
    {job && !newImport && <section className="detail-card" aria-label="نتيجة دفعة الاستيراد"><h2>{job.status === "Completed" ? "اكتمل الاستيراد" : job.status === "Queued" ? "الدفعة في انتظار التنفيذ" : job.status === "Running" ? "جارٍ اعتماد الدفعة كاملة" : "لم تُنشر الدفعة"}</h2><p>{job.sourceSystem} · {job.rowCount} تسجيل</p>{job.replayedRequest&&<p role="status">هذا الملف معتمد بالفعل؛ نعرض نتيجة الدفعة السابقة دون إنشاء تسجيلات جديدة.</p>}{["Queued", "Running"].includes(job.status) ? <p role="status">يستمر التنفيذ بأمان عند إغلاق الصفحة. لن تظهر بيانات جزئية قبل اكتمال المعاملة.</p> : job.message ? <ErrorState message={job.message} /> : <p role="status">تم إنشاء {job.publishedRows} تسجيل، والتعرف على {job.replayedRows} تسجيل سابق دون تكرار. بيانات الاشتراك وحدها لا تنشئ قبضًا.</p>}{job.financiallyIncomplete > 0 && <p className="notice">{job.financiallyIncomplete} تسجيل يحتاج مراجعة مالية. الصافي غير المعروف ليس صفرًا.</p>}
      {job.status==="Completed"&&(job.result?.historicalPayments??0)>0&&<p>{job.publishedRows===0&&job.replayedRows>0?"تم التحقق من":"تتضمن الدفعة"} {job.result!.historicalPayments} سند تحصيل سابق صريح، بإجمالي <MoneyValue amount={job.result!.historicalPaymentTotal??0} currency={published[0]?.currencySnapshot??"EGP"}/>. {job.publishedRows===0&&job.replayedRows>0?"لم يُنشأ قبض جديد عند إعادة الرفع.":"لم ينشئ سعر الاشتراك تحصيلًا إضافيًا."}</p>}
      {job.status==="Completed"&&job.result&&<ImportEffects effects={job.result.effects??[]} coachAssignments={job.result.coachAssignments} tournamentParticipations={job.result.tournamentParticipations}/>}
      {published.length > 0 && <AdminDataTable label="نتيجة الاستيراد المعتمدة" rows={published} rowKey={r => r.legacyEnrollmentId} columns={[{ key: "player", header: "اللاعب", primary: true, render: r => academyId ? <><b>{r.player}</b><small>{r.playerCode}</small></> : <Link href={`/dashboard/players/${r.id}`}><b>{r.player}</b><small>{r.playerCode}</small></Link> }, { key: "service", header: "التسجيل", render: r => <>{r.sport}<small>{r.branch} · {r.groupName}</small></> }, { key: "net", header: "اتفاق تاريخي — ليس قبضًا", render: r => r.priceSnapshot == null ? "غير معروف — مراجعة مالية" : <MoneyValue amount={Number(r.priceSnapshot)} currency={r.currencySnapshot} /> }]} />}
    </section>}
    {preview && <section ref={resultRef} tabIndex={-1} className="list-card" aria-label="نتيجة معاينة الاستيراد"><header className="import-result-heading"><div><h2>{preview.canApprove ? "اكتملت المعاينة دون أخطاء" : "الملف يحتاج تصحيحًا"}</h2><p>{preview.sourceSystem || "مصدر غير محدد"} · {preview.cutoffDate || "تاريخ قطع غير محدد"}</p></div>{preview.errors.length > 0 ? <button className="secondary-button" onClick={downloadErrors}><Download size={16} /> تنزيل تقرير الأخطاء</button> : <CheckCheck aria-label="البيانات اجتازت فحص المعاينة" />}</header>
      <div className="import-counts"><article><strong>{preview.rowCount}</strong><span>تسجيل</span></article><article><strong>{preview.playerCount}</strong><span>طفل مختلف</span></article><article><strong>{preview.householdCount}</strong><span>{preview.guardianCount?"ولي أمر أساسي":"أسرة برمز"}</span></article><article><strong>{preview.financiallyIncomplete}</strong><span>تحتاج مراجعة مالية</span></article></div>
      {preview.historical&&<details className="detail-card"><summary>التاريخ الاختياري والعلاقات</summary><p>{preview.guardianCount} ولي أمر · {preview.guardianRelationships} علاقة · {preview.historical.periods} فترة اشتراك · {preview.historical.sessions} حصة فعلية · {preview.historical.attendance} سجل حضور</p><p>{preview.historical.evaluations} تقييمًا: {preview.historical.published} منشور و{preview.historical.drafts} مسودة · {preview.historical.scores} درجة</p></details>}
      {(preview.effects?.length??0)>0&&<ImportEffects effects={preview.effects!} coachAssignments={preview.coachAssignments??0} tournamentParticipations={preview.tournamentParticipations??0}/>}
      {preview.errors.length > 0 && <><p className="notice">لم يُحفظ أي سجل. نعرض أول 300 مشكلة؛ صحّح الملف وأعد رفعه.</p><AdminDataTable label="مشكلات الاستيراد" rows={preview.errors} rowKey={r => `${r.sheet}-${r.row}-${r.column}-${r.message}`} columns={[{ key: "row", header: "الورقة والصف", primary: true, render: r => <>{r.sheetLabel ?? r.sheet} · {r.row}<small>{r.columnLabel ?? r.column}</small></> }, { key: "problem", header: "التصحيح المطلوب", render: r => r.message }]} /></>}
      {preview.canApprove && <section className="import-approval"><h3>اعتماد الدفعة كاملة</h3><p>راجع الطفل والأسرة والمجموعة والسعر. تسجيل الخدمة لا يعني إثبات دفع سابق، ولا يرسل رسائل أو يتجاوز تحقق الدخول.</p>
        {(preview.guardianMatches ?? []).map(m => <label key={m.householdKey}>مراجعة رابط {m.guardianName} · <span dir="ltr">{m.phone}</span><select value={resolutions[m.householdKey] ?? ""} onChange={e => setResolutions({ ...resolutions, [m.householdKey]: e.target.value })}><option value="">اختر بعد التحقق من علاقة ولي الأمر</option>{m.candidates.map(id => <option key={id} value={id}>{m.guardianName} — ملف قائم بالأكاديمية</option>)}</select>{m.candidates.length === 0 && <small>البيانات متعارضة؛ يلزم تصحيحها قبل الربط.</small>}</label>)}
        {(preview.accountLinks?.length??0)>0&&<details className="detail-card" open><summary>مراجعة ربط {preview.accountLinks!.length} حسابًا قائمًا</summary><p>الأسماء والهواتف مطابقة للملف. اعتماد الدفعة يضيف عضوياتها في هذه الأكاديمية. تأكد أن أصحابها هم الأشخاص المقصودون.</p><ul>{preview.accountLinks!.map(x=><li key={`${x.role}:${x.phone}`}>{x.role} · {x.name} · <span dir="ltr">{x.phone}</span></li>)}</ul></details>}
        <label className="checkbox-line"><input type="checkbox" checked={approved} onChange={e => setApproved(e.target.checked)} />راجعت الأسماء والهواتف وربط الحسابات القائمة والأبناء بأولياء أمورهم، وأعتمد العلاقات وعضويات هذه الدفعة.</label>
        <details><summary>أرصدة من قالب سابق</summary><label className="checkbox-line"><input type="checkbox" checked={balancesApproved} onChange={e => setBalancesApproved(e.target.checked)} />راجعت الأرصدة الافتتاحية وسند اعتمادها إن كانت موجودة — ليست قبض اليوم.</label></details>
        {(preview.historicalPayments??0)>0 && <label className="checkbox-line"><input type="checkbox" checked={paymentsApproved} onChange={e=>setPaymentsApproved(e.target.checked)}/>راجعت سندات {preview.historicalPayments} تحصيلًا سابقًا بإجمالي <MoneyValue amount={preview.historicalPaymentTotal??0} currency={preview.rows[0]?.currency??"EGP"}/>، وأعتمد تسجيل القبض بتاريخه دون تكرار تحصيل الاشتراك.</label>}
        {preview.financiallyIncomplete > 0 && <label className="checkbox-line"><input type="checkbox" checked={financialReview} onChange={e => setFinancialReview(e.target.checked)} />أقر أن {preview.financiallyIncomplete} تسجيل يحتاج مراجعة مالية قبل الاعتماد المالي النهائي.</label>}
        <button className="primary-button" disabled={busy || !approved || (preview.historicalPayments??0)>0&&!paymentsApproved || preview.financiallyIncomplete > 0 && !financialReview} onClick={() => void commit()}>{busy ? "جارٍ اعتماد الطلب…" : "اعتماد وبدء الاستيراد"}</button>
      </section>}
      {preview.rows.length > 0 && <><h3>التسجيلات التي اجتازت الفحص</h3><p className="muted">هذه معاينة وليست نتيجة استيراد معتمدة. السعر التاريخي محفوظ دون إعادة الخصم.</p><AdminDataTable label="معاينة التسجيلات" rows={preview.rows} rowKey={r => r.legacyEnrollmentId} columns={[
        { key: "player", header: "اللاعب", primary: true, render: r => <><b>{r.playerName}</b><small>{r.legacyPlayerId}</small></> },
        { key: "guardian", header: "ولي الأمر", render: r => <>{r.guardianName}<small dir="ltr">{r.guardianPhone}</small></> },
        { key: "net", header: "الصافي التاريخي", render: r => r.agreedNet === null ? "غير معروف — مراجعة مالية" : <MoneyValue amount={Number(r.agreedNet)} currency={r.currency} /> },
      ]} /></>}
    </section>}
  </Wrapper>;
}

function ImportEffects({effects,coachAssignments,tournamentParticipations}:{effects:ImportEffect[];coachAssignments:number;tournamentParticipations:number}){return <section className={styles.effects} aria-label="العلاقات وأثر الاستيراد"><div className="import-counts"><article><strong>{coachAssignments}</strong><span>إسناد مدرب لمجموعة</span></article><article><strong>{tournamentParticipations}</strong><span>مشاركة في البطولات</span></article></div><AdminDataTable label="إنشاء أو إعادة استخدام المراجع" rows={effects} rowKey={e=>e.sheet} columns={[{key:"sheet",header:"الورقة",primary:true,render:e=>e.sheet},{key:"create",header:"إنشاء",render:e=>e.create},{key:"reuse",header:"إعادة استخدام",render:e=>e.reuse}]}/></section>;}
