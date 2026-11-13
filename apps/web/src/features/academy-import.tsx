"use client";
import { Fragment, useEffect, useId, useRef, useState } from "react";
import { CheckCheck, Download, UploadCloud } from "lucide-react";
import Link from "next/link";
import { ApprovedWorkspace } from "../components/approved-workspace";
import styles from "./academy-import.module.css";

import { MoneyValue } from "../components/money-value";
import { AdminDataTable, ErrorState, PageHeader } from "../components/dashboard-shell";
import { apiProblemMessage, requestToken } from "../lib/api-problem";
import { formatDateAr } from "../lib/formatters";
type Issue = { sheet: string; sheetLabel?: string; row: number; column: string; columnLabel?: string; message: string };
type PreviewRow = { row: number; legacyPlayerId: string; legacyEnrollmentId: string; playerName: string; guardianName: string; guardianPhone: string; start?: string; end?: string | null; agreedNet: string | null; currency: string; existingNew: string };
type ImportEffect={sheet:string;create:number;reuse:number};
type ImportRegistrationLabel = { sport: string; group: string; branch: string };
type ImportSchema={rehearsalAvailable?:boolean;maxRows:number;maxFileBytes:number;connectedSheets:{name:string;optional:boolean;columns:{key:string;label:string;required:boolean}[]}[]};
type HistoricalPaymentSummary = { historicalPayments?: number; historicalPaymentTotal?: number; historicalPaymentCurrency?: string | null };
type Preview = HistoricalPaymentSummary & { cleanRows?:{playerCount:number;excludedRows:number;relatedRows?:number}; registrationLabels?: Record<string, ImportRegistrationLabel>; accountLinks?:{role:string;name:string;phone:string}[]; guardianCount?:number;guardianRelationships?:number;historical?:{periods:number;sessions:number;attendance:number;evaluations:number;published:number;drafts:number;scores:number}; effects?:ImportEffect[];coachAssignments?:number;tournamentParticipations?:number; schemaVersion: string; fileHash: string; referenceStamp: string; sourceSystem: string; cutoffDate: string; rowCount: number; playerCount: number; householdCount: number; financiallyIncomplete: number; canApprove: boolean; errors: Issue[]; rows: PreviewRow[]; guardianMatches?: { householdKey: string; guardianName: string; phone: string; candidates: string[] }[] };
type Job = { replayedRequest?:boolean; result?:HistoricalPaymentSummary & {effects?:ImportEffect[];coachAssignments:number;tournamentParticipations:number;players:number;enrollments:number}; id: string; sourceSystem: string; status: string; rowCount: number; publishedRows: number; replayedRows: number; financiallyIncomplete: number; attempts: number; createdAtUtc?: string; message?: string };
type Published = { id: string; legacyEnrollmentId: string; player: string; playerCode: string; sport: string; branch: string; groupName: string; startDate?: string; endDate?: string | null; priceSnapshot: string | null; currencySnapshot: string };
type ImportProps = { academyId?: string; onCompleted?: () => void };
type JobReadIssue = { id: string; message: string; unavailable?: boolean; retrying?: boolean };
class ImportAccessError extends Error {}

function importIssueKey(issue: Issue) {
  return JSON.stringify([issue.sheet, issue.row, issue.columnLabel ?? issue.column, issue.message]);
}

function isJob(value: unknown): value is Job {
  if (!value || typeof value !== "object") return false;
  const job = value as Partial<Job>;
  return typeof job.id === "string" && /^[a-z0-9-]+$/i.test(job.id)
    && ["Queued", "Running", "Completed", "Failed", "Expired"].includes(job.status ?? "");
}

function importCount(value: unknown) {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= 0 ? value : undefined;
}

const jobLabels: Record<string, string> = {
  Completed: "مكتملة", Queued: "في انتظار التنفيذ", Running: "قيد التنفيذ", Failed: "لم تُنشر", Expired: "انتهت المهلة",
};

export function AcademyImport(props: ImportProps = {}) {
  return <ImportWorkspace key={props.academyId ?? "academy"} {...props} />;
}

function ImportWorkspace({ academyId, onCompleted }: ImportProps) {
  const notifiedCompletion = useRef(new Set<string>());
  const apiBase = academyId ? `/api/v1/platform/academies/${academyId}/import` : "/api/v1/people/import";
  const csrfPath = academyId ? "/api/v1/platform/auth/csrf" : "/api/v1/auth/csrf";
  const Wrapper = academyId ? Fragment : ApprovedWorkspace;
  const [newImport,setNewImport]=useState(false);
  const resultRef=useRef<HTMLElement>(null);
  const jobResultRef = useRef<HTMLElement>(null);
  const [selectedFile,setSelectedFile]=useState<File|null>(null),[downloadBusy,setDownloadBusy]=useState(false);
  const [schema,setSchema]=useState<ImportSchema|null>(null);
  const input = useRef<HTMLInputElement>(null), request = useRef<AbortController | null>(null);
  const fileRef = useRef<File | null>(null), intent = useRef("");
  const generation = useRef(0);
  const staged = useRef<{ key: string; form: FormData } | null>(null);
  const approvalInFlight = useRef(false);
  const [uncertain, setUncertain] = useState(false);
  const [jobReadIssue, setJobReadIssue] = useState<JobReadIssue | null>(null);
  const [readRevision, setReadRevision] = useState(0);
  const [history, setHistory] = useState<Job[]>([]);
  const [historyIssue, setHistoryIssue] = useState("");
  const [historyRevision, setHistoryRevision] = useState(0);
  const [readingHistory, setReadingHistory] = useState(false);
  const [preview, setPreview] = useState<Preview | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState("");
  const issues = [...new Map(preview?.errors.map(issue => [importIssueKey(issue), issue])).values()];
  const [loginRequired, setLoginRequired] = useState(false);
  const [job, setJob] = useState<Job | null>(null), [published, setPublished] = useState<Published[] | null>(null), [approved, setApproved] = useState(false), [balancesApproved, setBalancesApproved] = useState(false), [paymentsApproved,setPaymentsApproved] = useState(false), [financialReview, setFinancialReview] = useState(false), [resolutions, setResolutions] = useState<Record<string, string>>({});
  const canCommit = preview?.canApprove === true && approved &&
    (preview.guardianMatches ?? []).every(match => match.candidates.includes(resolutions[match.householdKey])) &&
    (!(preview.historicalPayments ?? 0) || paymentsApproved) &&
    (preview.financiallyIncomplete === 0 || financialReview);
  useEffect(()=>{const controller=new AbortController();fetch(`${apiBase}/schema`,{signal:controller.signal,cache:"no-store"}).then(r=>r.ok?r.json():null).then(value=>{if(!controller.signal.aborted&&Array.isArray(value?.connectedSheets))setSchema(value);}).catch(()=>{});return()=>controller.abort();},[apiBase]);
  useEffect(() => () => request.current?.abort(), []);
  useEffect(() => {
    const controller = new AbortController();
    void fetch(`${apiBase}/jobs`, { signal: controller.signal, cache: "no-store" }).then(async response => {
      if (!response.ok) throw new Error();
      const value: unknown = await response.json();
      if (!Array.isArray(value) || !value.every(isJob)) throw new Error();
      if (controller.signal.aborted) return;
      setHistory(value);
      setHistoryIssue("");
      if (generation.current === 0 && value[0]) setJob(current => current ?? value[0]);
    }).catch(() => {
      if (!controller.signal.aborted) setHistoryIssue("تعذر تحميل سجل دفعات الاستيراد.");
    }).finally(() => {
      if (!controller.signal.aborted) setReadingHistory(false);
    });
    return () => controller.abort();
  }, [apiBase, historyRevision]);
  const jobId = job?.id, jobState = job?.status;
  const activeReadIssue = jobId && jobReadIssue?.id === jobId ? jobReadIssue : null;
  const visibleHistory = history.map(entry => entry.id === jobId && job ? job : entry);
  useEffect(() => {
    if (jobId && jobState === "Completed" && !notifiedCompletion.current.has(jobId)) {
      notifiedCompletion.current.add(jobId);
      onCompleted?.();
    }
  }, [jobId, jobState, onCompleted]);
  useEffect(() => {
    if (!jobId || !["Queued", "Running"].includes(jobState ?? "")) return;
    const controller = new AbortController();
    const id = jobId;
    let timer = window.setTimeout(readStatus, readRevision ? 0 : 2000);
    async function readStatus() {
      try {
        const response = await fetch(`${apiBase}/jobs/${jobId}`, { signal: controller.signal, cache: "no-store" });
        if ([401, 403, 404, 429].includes(response.status)) {
          if (!controller.signal.aborted) {
            if (response.status !== 429) setHistory(entries => entries.filter(entry => entry.id !== id));
            setJobReadIssue({
              id, unavailable: response.status !== 429,
              message: response.status === 429 ? "انتظر قليلًا قبل تحديث حالة الدفعة." : "تعذر فتح الدفعة في الحساب الحالي.",
            });
          }
          return;
        }
        if (!response.ok) throw new Error();
        const next: unknown = await response.json();
        if (!isJob(next) || next.id !== jobId) throw new Error();
        if (controller.signal.aborted) return;
        setJob(previous => ({ ...next, replayedRequest: previous?.replayedRequest || next.replayedRequest }));
        setHistory(entries => entries.map(entry => entry.id === id ? next : entry));
        setJobReadIssue(null);
        if (!["Queued", "Running"].includes(next.status)) return;
      } catch {
        if (controller.signal.aborted) return;
        setJobReadIssue({ id, message: "تعذر تحديث حالة الدفعة." });
      }
      if (!controller.signal.aborted) timer = window.setTimeout(readStatus, 2000);
    }
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [apiBase, jobId, jobState, readRevision]);

  useEffect(() => {
    if (!jobId || jobState !== "Completed") return;
    const controller = new AbortController();
    void fetch(`${apiBase}/jobs/${jobId}/records`, { signal: controller.signal, cache: "no-store" }).then(async response => {
      if ([401, 403, 404].includes(response.status)) {
        if (!controller.signal.aborted) {
          setPublished(null);
          setHistory(entries => entries.filter(entry => entry.id !== jobId));
          setJobReadIssue({ id: jobId, unavailable: true, message: "تعذر فتح تفاصيل الدفعة في الحساب الحالي." });
        }
        return;
      }
      if (!response.ok) throw new Error();
      const rows = await response.json();
      if (!Array.isArray(rows) || rows.some(row => !row || typeof row.legacyEnrollmentId !== "string" || typeof row.id !== "string")) throw new Error();
      if (!controller.signal.aborted) {
        setPublished(rows);
        setJobReadIssue(null);
      }
    }).catch(() => {
      if (!controller.signal.aborted) {
        setPublished(null);
        setJobReadIssue({ id: jobId, message: "اكتمل الاستيراد، وتعذر تحميل تفاصيل التسجيلات." });
      }
    });
    return () => controller.abort();
  }, [apiBase, jobId, jobState, readRevision]);
  useEffect(()=>{if(preview||error)resultRef.current?.scrollIntoView?.({behavior:"smooth",block:"start"});},[preview,error]);
  async function download(kind:"connected-template"|"connected-sample"|"valid-players"|"player-corrections") {
    if (busy || downloadBusy) return;
    const file = fileRef.current;
    const fromPreview = kind === "valid-players" || kind === "player-corrections";
    if (fromPreview && (!file || !preview?.cleanRows)) return;
    const controller = new AbortController();
    request.current = controller;
    setDownloadBusy(true); setError(""); setLoginRequired(false);
    try {
      let r: Response;
      if (fromPreview && file && preview) {
        const token = await importToken(controller.signal, false);
        const form = new FormData();
        form.set("file", file);
        form.set("ExpectedFileHash", preview.fileHash);
        const exportPath = kind === "player-corrections" ? "preview/corrections" : "preview?downloadValidRows=true";
        r = await fetch(`${apiBase}/${exportPath}`, {
          method: "POST", body: form, headers: { "X-CSRF-TOKEN": token }, signal: controller.signal,
        });
      } else {
        r = await fetch(`${apiBase}/${kind}`, {cache:"no-store",signal:controller.signal});
      }
      if (r.status === 401) {setLoginRequired(true);throw new Error("انتهت جلسة الدخول. سجّل الدخول مجددًا ثم أعد التنزيل.");}
      if (!r.ok) throw new Error(await apiProblemMessage(r, "تعذر تنزيل الملف. راجع إعدادات الأكاديمية ثم أعد المحاولة."));
      if(!r.headers.get("content-type")?.includes("spreadsheetml.sheet"))throw new Error("تعذر تنزيل ملف Excel صالح. أعد المحاولة.");
      const blob = await r.blob();
      if (controller.signal.aborted) return;
      const names = { "connected-template": "i-academy-one-blank.xlsx", "connected-sample": "i-academy-one-completed-50.xlsx", "valid-players": "academy-valid-players.xlsx", "player-corrections": "academy-player-corrections.xlsx" };
      const url = URL.createObjectURL(blob), link = document.createElement("a");
      link.href = url; link.download = names[kind]; link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      if (!controller.signal.aborted) setError(e instanceof Error && /[\u0600-\u06ff]/.test(e.message) ? e.message : "تعذر الاتصال أثناء التنزيل. أعد المحاولة.");
    } finally {
      if (!controller.signal.aborted) setDownloadBusy(false);
    }
  }
  function selectFile(file?:File){if(!file || busy || downloadBusy || uncertain || approvalInFlight.current)return;generation.current++;setNewImport(false);setSelectedFile(file);setPreview(null);setJob(null);setPublished(null);setError("");fileRef.current=null;}
  function requireImportAccess(status: number, approval: boolean) {
    if (status === 401) {
      setLoginRequired(true);
      throw new ImportAccessError(approval
        ? "انتهت جلسة الدخول. سجّل الدخول مجددًا وراجع آخر دفعة قبل إعادة الاعتماد."
        : "انتهت جلسة الدخول. سجّل الدخول مجددًا ثم أعد معاينة الملف؛ لم يُعتمد الاستيراد.");
    }
    if (status === 403) throw new ImportAccessError(approval
      ? "لا تملك صلاحية اعتماد الاستيراد في هذا الحساب. راجع مسؤول الأكاديمية أو المنصة."
      : "لا تملك صلاحية معاينة الاستيراد في هذا الحساب. راجع مسؤول الأكاديمية أو المنصة.");
  }
  async function importToken(signal: AbortSignal, approval: boolean) {
    const { status, token } = await requestToken(csrfPath, signal);
    requireImportAccess(status, approval);
    if (!token) throw new Error("تعذر بدء طلب آمن. أعد المحاولة.");
    return token;
  }
  async function upload(file?: File) {
    if (!file || busy || downloadBusy || uncertain || approvalInFlight.current) return;
    setLoginRequired(false);
    setNewImport(false);
    generation.current++;
    setPreview(null); setError(""); setJob(null); setPublished(null); setApproved(false); setBalancesApproved(false); setPaymentsApproved(false); setFinancialReview(false); setResolutions({}); fileRef.current = file; intent.current = crypto.randomUUID();
    if (!file.name.toLowerCase().endsWith(".xlsx") || file.size === 0) { setError("اختر ملف xlsx صالحًا وغير فارغ."); return; }
    const maxBytes = schema?.maxFileBytes ?? 5 * 1024 * 1024;
    if (file.size > maxBytes) { setError("حجم الملف أكبر من الحد المسموح. قلّل حجم الملف وأعد اختياره."); return; }
    setBusy(true); const controller = new AbortController(); request.current = controller;
    try {
      const token = await importToken(controller.signal, false);
      const data = new FormData(); data.set("file", file);
      if(academyId){
        const preparation=await fetch(`/api/v1/platform/academies/${academyId}/prepare-operation`,{method:"POST",headers:{"X-CSRF-TOKEN":token},signal:controller.signal});
        requireImportAccess(preparation.status, false);
        if (!preparation.ok) throw new Error(await apiProblemMessage(preparation, "تعذر تجهيز الأكاديمية للاستيراد. أعد المحاولة."));
      }
      const response = await fetch(`${apiBase}/preview`, { method: "POST", body: data, headers: { "X-CSRF-TOKEN": token }, signal: controller.signal });
      requireImportAccess(response.status, false);
      if (!response.ok) throw new Error(await apiProblemMessage(response, "تعذر فحص الملف. تحقق من القالب وأعد المحاولة."));
      const value = await response.json();
      if (!Array.isArray(value.errors) || !Array.isArray(value.rows)) throw new Error("تعذر قراءة نتيجة الفحص.");
      setPreview(value);
    } catch (caught) { if (!controller.signal.aborted) setError(caught instanceof Error && /[\u0600-\u06ff]/.test(caught.message) ? caught.message : "تعذر الاتصال أثناء المعاينة. أعد المحاولة؛ لم يتم اعتماد الملف."); }
    finally { if (!controller.signal.aborted) setBusy(false); if (input.current) input.current.value = ""; }
  }
  function showJob(value: Job) {
    setJob(value);
    setPublished(null);
    setJobReadIssue(null);
    setReadRevision(0);
    setPreview(null);
    setSelectedFile(null);
    fileRef.current = null;
    staged.current = null;
    setUncertain(false);
  }

  function showApprovedJob(value: Job) {
    showJob(value);
    setHistory(entries => [value, ...entries.filter(entry => entry.id !== value.id)].slice(0, 20));
  }

  async function commit() {
    if (!preview || !fileRef.current || busy || downloadBusy || !canCommit || approvalInFlight.current) return;
    approvalInFlight.current = true;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setError("");
    setLoginRequired(false);
    let unconfirmed = uncertain;
    let sent = false;
    try {
      const token = await importToken(controller.signal, true);
      if (!staged.current) {
        const form = new FormData();
        form.set("file", fileRef.current);
        form.set("ExpectedFileHash", preview.fileHash);
        form.set("ExpectedReferenceStamp", preview.referenceStamp);
        form.set("GuardianLinksApproved", String(approved));
        form.set("OpeningBalancesApproved", String(balancesApproved));
        form.set("HistoricalPaymentsApproved", String(paymentsApproved));
        form.set("AcceptFinancialReview", String(financialReview));
        form.set("GuardianResolutions", JSON.stringify(Object.entries(resolutions).filter(([, guardianId]) => guardianId).map(([householdKey, guardianId]) => ({ householdKey, guardianId }))));
        staged.current = { key: intent.current, form };
      }
      const submission = staged.current;
      sent = true;
      const response = await fetch(`${apiBase}/commit`, {
        method: "POST", body: submission.form, headers: { "X-CSRF-TOKEN": token, "Idempotency-Key": submission.key }, signal: controller.signal,
      });
      if (!response.ok) {
        sent = false;
        unconfirmed ||= response.status >= 500 || response.status === 408;
        requireImportAccess(response.status, true);
        const problem = response.status === 409 ? await response.clone().json().catch(() => null) : null;
        if (problem?.code === "stale") {
          unconfirmed = false;
          setPreview(null);
          fileRef.current = null;
        }
        throw new Error(await apiProblemMessage(response, "تعذر اعتماد الملف. راجع بيانات المعاينة."));
      }
      const value: unknown = await response.json();
      if (!isJob(value)) throw new Error();
      if (!controller.signal.aborted) showApprovedJob(value);
    } catch (caught) {
      if (controller.signal.aborted) return;
      const unresolved = unconfirmed || sent;
      setUncertain(unresolved);
      if (!unresolved) staged.current = null;
      setError(unresolved && !(caught instanceof ImportAccessError)
        ? "تعذر تأكيد اعتماد الدفعة. تحقّق من حالتها قبل إعادة الإرسال."
        : caught instanceof Error && /[\u0600-\u06ff]/.test(caught.message) ? caught.message : "تعذر اعتماد الملف. أعد المحاولة.");
    } finally {
      approvalInFlight.current = false;
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  async function verifyApproval() {
    if (!uncertain || !staged.current || approvalInFlight.current || busy || downloadBusy) return;
    const { key } = staged.current;
    approvalInFlight.current = true;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setError("");
    setLoginRequired(false);
    try {
      const response = await fetch(`${apiBase}/jobs/by-request?${new URLSearchParams({ requestKey: key })}`, { cache: "no-store", signal: controller.signal });
      requireImportAccess(response.status, true);
      if (response.status === 404) throw new Error("لم تظهر دفعة مرتبطة بهذا الطلب بعد. يمكنك إعادة إرسال الطلب نفسه.");
      if (!response.ok) throw new Error("تعذر التحقق من حالة الاعتماد. أعد المحاولة.");
      const value: unknown = await response.json();
      if (!isJob(value)) throw new Error("تعذر قراءة حالة الدفعة.");
      if (!controller.signal.aborted) showApprovedJob(value);
    } catch (caught) {
      if (!controller.signal.aborted) setError(caught instanceof Error && /[\u0600-\u06ff]/.test(caught.message) ? caught.message : "تعذر التحقق من حالة الاعتماد. أعد المحاولة.");
    } finally {
      approvalInFlight.current = false;
      if (!controller.signal.aborted) setBusy(false);
    }
  }
  function downloadErrors() {
    if (!preview) return;
    const cell = (value: string | number) => `"${String(value).replace(/^[=+@-]/, "'$&").replaceAll('"', '""')}"`;
    const rows = [["الورقة", "الصف", "الحقل", "المشكلة"], ...issues.map(e => [e.sheetLabel ?? e.sheet, e.row, e.columnLabel ?? e.column, e.message])];
    const url = URL.createObjectURL(new Blob(["\uFEFF" + rows.map(r => r.map(cell).join(",")).join("\n")], { type: "text/csv;charset=utf-8" })); const a = document.createElement("a"); a.href = url; a.download = "academy-import-errors.csv"; a.click(); URL.revokeObjectURL(url);
  }
  return <Wrapper><PageHeader title="استيراد بيانات الأكاديمية" context="الإعدادات والمساعدة / نقل البيانات" description="قالب خاص بأكاديميتك، ثم معاينة واضحة قبل اعتماد أي تغيير." />
    {(!job || newImport) && <ol className={styles.steps} aria-label="مراحل الاستيراد">{["القالب والرفع","التحقق","المعاينة والتأكيد","التنفيذ","الملخص"].map((label,index)=><li key={label} aria-current={(preview?2:busy?1:0)===index?"step":undefined}>{label}</li>)}</ol>}
    {job && newImport && <div className="import-view-actions"><button type="button" className="secondary-button" onClick={() => setNewImport(false)}>العودة إلى نتيجة الدفعة</button></div>}
    {(!job||newImport)&&<section className={styles.setup} onFocusCapture={()=>{generation.current++;}} onPointerDownCapture={()=>{generation.current++;}}><h2>بيانات البداية المترابطة لأكاديمية واحدة</h2><p>المدربون ← المجموعات ← الحصص · أولياء الأمور ← الأبناء ← الاشتراكات · اللاعبون ← البطولات</p>
      <div className={styles.downloads}><button type="button" disabled={downloadBusy||busy} onClick={()=>void download("connected-template")}><Download size={26}/><span><strong>تنزيل قالب فارغ</strong><small>ملف XLSX يشمل اللاعبين وكل أوراق الربط</small></span></button><button type="button" disabled={downloadBusy||busy} onClick={()=>void download("connected-sample")}><Download size={26}/><span><strong>تنزيل نموذج مكتمل</strong><small>50 لاعبًا بأسماء مصرية خيالية، وأسر ومجموعات ومدربون وبطولات</small></span></button></div>
      {schema&&<details className={styles.schema}><summary>الحقول المطلوبة والاختيارية</summary><AdminDataTable label="أوراق ملف الأكاديمية" rows={schema.connectedSheets} rowKey={s=>s.name} columns={[{key:"sheet",header:"الورقة",primary:true,render:s=>s.name},{key:"columns",header:"الحقول",render:s=>s.columns.filter(c=>c.key!=="ExistingId").map(c=>c.label+(c.required?" *":"")).join("، ")||"تعليمات وأمثلة الأكواد"},{key:"optional",header:"الاستخدام",render:s=>s.optional?"اختياري بحسب بياناتك":"أساسي عند الربط"}]}/><p>النجمة تحدد الحقل المطلوب. اكتب أرقام الموبايل في خلايا نصية لحفظ الصفر الأول.</p></details>}
      <button className={styles.upload} aria-label="استيراد ملف" aria-describedby="import-upload-limit" disabled={busy||downloadBusy||uncertain} onClick={()=>input.current?.click()}><UploadCloud size={28}/><strong>استيراد ملف</strong><small id="import-upload-limit">اختر ملف .xlsx · حتى {schema?schema.maxRows:1000} تسجيل · الحد {schema?Math.floor(schema.maxFileBytes/1024/1024):5} ميجابايت</small></button>
      <input ref={input} className="import-file-input" type="file" accept=".xlsx" aria-label="ملف بيانات الأكاديمية" disabled={busy||downloadBusy||uncertain} onChange={e => selectFile(e.target.files?.[0])} />
      {selectedFile&&<section className={styles.fileStatus} aria-label="الملف المختار"><p role="status"><strong>{selectedFile.name}</strong> · {Math.ceil(selectedFile.size/1024)} كيلوبايت</p><p>{busy&&!preview?"جارٍ فحص الملف…":preview?"تم فحص الملف. راجع النتيجة أدناه.":"تم اختيار الملف، ولم يُحفظ أي سجل بعد."}</p><button className="primary-button" disabled={busy||downloadBusy||uncertain} onClick={()=>void upload(selectedFile)}>{busy&&!preview?"جارٍ فحص الملف…":preview?"إعادة فحص الملف":"متابعة لفحص الملف"}</button></section>}

    </section>}
    {error && <section ref={resultRef} tabIndex={-1}>
      <ErrorState message={error} />
      {loginRequired && <Link className="secondary-button" href={academyId ? "/platform/login" : "/login"} target="_blank" rel="noopener">تسجيل الدخول في نافذة أخرى</Link>}
    </section>}
    {job && !newImport && <section ref={jobResultRef} tabIndex={-1} className="detail-card" aria-label="نتيجة دفعة الاستيراد">
      <h2>{activeReadIssue?.unavailable ? "نتيجة الاستيراد غير متاحة" : job.status === "Completed" ? "اكتمل الاستيراد" : job.status === "Queued" ? "الدفعة في انتظار التنفيذ" : job.status === "Running" ? "جارٍ اعتماد الدفعة" : "لم تُنشر الدفعة"}</h2>
      {!activeReadIssue?.unavailable && <>
      <p className="muted"><bdi>{job.sourceSystem}</bdi>{job.createdAtUtc && <> · {formatDateAr(job.createdAtUtc)}</>}</p>
      {job.replayedRequest && <p role="status">هذا الملف معتمد بالفعل. هذه نتيجة الدفعة السابقة.</p>}
      {["Queued", "Running"].includes(job.status) && <p role="status">يمكنك العودة لهذه الصفحة لمتابعة التنفيذ.</p>}
      {job.status !== "Completed" && job.message && <ErrorState message={job.message} />}
      {job.status === "Completed" && <ImportResultSummary job={job} recordCurrency={published?.[0]?.currencySnapshot} />}
      {job.financiallyIncomplete > 0 && <p className="notice">{job.financiallyIncomplete} تسجيل يحتاج مراجعة مالية. الصافي غير المعروف ليس صفرًا.</p>}
      {job.status === "Completed" && !activeReadIssue && !published && <p role="status">جارٍ تحميل التسجيلات المحفوظة…</p>}
      {job.status === "Completed" && published?.length === 0 && <p>لا توجد تسجيلات محفوظة لهذه الدفعة.</p>}
      {!!published?.length && <PublishedRecords key={`records-${job.id}`} records={published} platform={Boolean(academyId)} />}
      {job.status === "Completed" && job.result && <ImportEffects key={`effects-${job.id}`} data={job.result} />}
      </>}
      {activeReadIssue && <div className={styles.recovery}>
        <ErrorState message={activeReadIssue.message} />
        <button type="button" className="secondary-button" disabled={activeReadIssue.retrying} onClick={() => {
          setJobReadIssue({ ...activeReadIssue, retrying: true });
          setReadRevision(value => value + 1);
        }}>
          {job.status === "Completed" ? "إعادة تحميل تفاصيل الدفعة" : "تحديث حالة الدفعة"}
        </button>
      </div>}
    </section>}
    {!newImport && !selectedFile && (visibleHistory.length > 0 || historyIssue) && <section className={styles.history} aria-label="سجل دفعات الاستيراد">
      {visibleHistory.length > 0 && <details className="detail-card">
        <summary>دفعات الاستيراد السابقة</summary>
        <ul className={styles.historyList}>
          {visibleHistory.map(entry => <li key={entry.id}>
            <button type="button" className="secondary-button" aria-current={entry.id === jobId ? "true" : undefined} onClick={() => {
              if (entry.id !== jobId) showJob(entry);
              jobResultRef.current?.focus();
            }}>
              <bdi>{entry.sourceSystem}</bdi> · {entry.rowCount} تسجيل · {jobLabels[entry.status]}
              {entry.createdAtUtc && <small>{formatDateAr(entry.createdAtUtc)}</small>}
            </button>
          </li>)}
        </ul>
      </details>}
      {historyIssue && <div className={styles.recovery}>
        <ErrorState message={historyIssue} />
        <button type="button" className="secondary-button" disabled={readingHistory} onClick={() => {
          setReadingHistory(true);
          setHistoryRevision(value => value + 1);
        }}>إعادة تحميل سجل الدفعات</button>
      </div>}
    </section>}
    {job && !newImport && <div className="import-view-actions"><button type="button" className="secondary-button" disabled={busy || ["Queued", "Running"].includes(job.status)} onClick={() => setNewImport(true)}>استيراد ملف جديد</button></div>}
    {preview && <section ref={resultRef} tabIndex={-1} className="list-card" aria-label="نتيجة معاينة الاستيراد"><header className="import-result-heading"><div><h2>{preview.canApprove ? "اكتملت المعاينة دون أخطاء" : "الملف يحتاج تصحيحًا"}</h2>{preview.canApprove&&<p>تاريخ القطع {formatDateAr(preview.cutoffDate)} · المصدر <bdi>{preview.sourceSystem}</bdi></p>}</div>{preview.errors.length > 0 ? <button type="button" className="secondary-button" onClick={downloadErrors}><Download size={16} /> تنزيل تقرير الأخطاء</button> : preview.canApprove && <CheckCheck aria-label="البيانات اجتازت فحص المعاينة" />}</header>
      {!preview.canApprove && preview.errors.length === 0 && <p className="notice">لم يُحفظ أي سجل. تعذر عرض تفاصيل الفحص. أعد فحص الملف.</p>}
      {preview.canApprove&&<div className="import-counts"><article><strong>{preview.rowCount}</strong><span>تسجيل</span></article><article><strong>{preview.playerCount}</strong><span>طفل مختلف</span></article><article><strong>{preview.householdCount}</strong><span>{preview.guardianCount?"ولي أمر أساسي":"أسرة برمز"}</span></article><article><strong>{preview.financiallyIncomplete}</strong><span>تحتاج مراجعة مالية</span></article></div>}
      {preview.canApprove&&preview.historical&&<details className="detail-card"><summary>التاريخ الاختياري والعلاقات</summary><p>{preview.guardianCount} ولي أمر · {preview.guardianRelationships} علاقة · {preview.historical.periods} فترة اشتراك · {preview.historical.sessions} حصة فعلية · {preview.historical.attendance} سجل حضور</p><p>{preview.historical.evaluations} تقييمًا: {preview.historical.published} منشور و{preview.historical.drafts} مسودة · {preview.historical.scores} درجة</p></details>}
      {preview.canApprove && <ImportEffects key={preview.fileHash} data={preview} preview />}
      {preview.errors.length > 0 && <>
        <p className="notice">لم يُحفظ أي سجل.{!preview.cleanRows && " صحّح الحقول المذكورة في الملف الأصلي وأعد فحصه."}{preview.errors.length >= 300 && " يعرض الفحص حتى 300 مشكلة في المرة الواحدة."}</p>
        {preview.cleanRows && <section className="detail-card" aria-label="استيراد اللاعبين السليمين أولًا">
          <h3>{preview.cleanRows.playerCount} سليمًا · {preview.cleanRows.excludedRows} للتصحيح</h3>
          {(preview.cleanRows.relatedRows ?? 0) > 0 && <p>سجلات مرتبطة تنتقل إلى ملف التصحيح ({preview.cleanRows.relatedRows}).</p>}
          <p>افحص ملف السليمين واعتمده أولًا. صحّح الملف الثاني، ثم افحصه واعتمده.</p>
          <div className={styles.playerExports} aria-busy={downloadBusy}>
            <button className="secondary-button" disabled={downloadBusy||busy} onClick={()=>void download("valid-players")}>
              <Download size={16} /> تنزيل ملف اللاعبين السليمين
            </button>
            <button className="secondary-button" disabled={downloadBusy||busy} onClick={()=>void download("player-corrections")}>
              <Download size={16} /> تنزيل صفوف التصحيح
            </button>
          </div>
        </section>}
        <ImportIssues issues={issues} />
      </>}
      {preview.canApprove && <section className="import-approval"><h3>اعتماد الدفعة كاملة</h3><p>راجع روابط الحسابات قبل الاعتماد. التحصيل السابق يحتاج سندًا صريحًا، ودخول الحسابات يخضع للتحقق المعتاد.</p>
        <fieldset className={styles.approvalFields} disabled={busy || uncertain} aria-label="إقرارات الاستيراد">
        {(preview.guardianMatches ?? []).map(m => <label key={m.householdKey}>مراجعة رابط {m.guardianName} · <span dir="ltr">{m.phone}</span><select value={resolutions[m.householdKey] ?? ""} onChange={e => setResolutions({ ...resolutions, [m.householdKey]: e.target.value })}><option value="">اختر بعد التحقق من علاقة ولي الأمر</option>{m.candidates.map(id => <option key={id} value={id}>{m.guardianName} — ملف قائم بالأكاديمية</option>)}</select>{m.candidates.length === 0 && <small>البيانات متعارضة؛ يلزم تصحيحها قبل الربط.</small>}</label>)}
        {(preview.accountLinks?.length??0)>0&&<details className="detail-card" open><summary>مراجعة ربط {preview.accountLinks!.length} حسابًا قائمًا</summary><p>الأسماء والهواتف مطابقة للملف. اعتماد الدفعة يضيف عضوياتها في هذه الأكاديمية. تأكد أن أصحابها هم الأشخاص المقصودون.</p><ul>{preview.accountLinks!.map(x=><li key={`${x.role}:${x.phone}`}>{x.role} · {x.name} · <span dir="ltr">{x.phone}</span></li>)}</ul></details>}
        <label className="checkbox-line"><input type="checkbox" checked={approved} onChange={e => setApproved(e.target.checked)} />راجعت الأسماء والهواتف وربط الحسابات القائمة والأبناء بأولياء أمورهم، وأعتمد العلاقات وعضويات هذه الدفعة.</label>
        <details><summary>أرصدة من قالب سابق</summary><label className="checkbox-line"><input type="checkbox" checked={balancesApproved} onChange={e => setBalancesApproved(e.target.checked)} />راجعت الأرصدة الافتتاحية وسند اعتمادها إن كانت موجودة — ليست قبض اليوم.</label></details>
        {(preview.historicalPayments??0)>0 && <label className="checkbox-line"><input type="checkbox" checked={paymentsApproved} onChange={e=>setPaymentsApproved(e.target.checked)}/><span>راجعت سندات {preview.historicalPayments} تحصيلًا سابقًا وأعتمد تسجيل القبض بتاريخه. <HistoricalPaymentTotal data={preview} recordCurrency={preview.rows[0]?.currency} /></span></label>}
        {preview.financiallyIncomplete > 0 && <label className="checkbox-line"><input type="checkbox" checked={financialReview} onChange={e => setFinancialReview(e.target.checked)} />أقر أن {preview.financiallyIncomplete} تسجيل يحتاج مراجعة مالية قبل الاعتماد المالي النهائي.</label>}
        </fieldset>
        {uncertain ? <div className={styles.recovery}>
          <button type="button" className="primary-button" disabled={busy || downloadBusy} onClick={() => void verifyApproval()}>التحقق من حالة الاعتماد</button>
          <button type="button" className="secondary-button" disabled={busy || downloadBusy} onClick={() => void commit()}>إعادة إرسال الطلب نفسه</button>
        </div> : <button type="button" className="primary-button" disabled={busy || downloadBusy || !canCommit} onClick={() => void commit()}>{busy ? "جارٍ اعتماد الطلب…" : "اعتماد وبدء الاستيراد"}</button>}
      </section>}
      {preview.rows.length > 0 && <><h3>{preview.canApprove ? "معاينة التسجيلات" : "التسجيلات المقروءة"}</h3>{preview.canApprove && <p className="muted">لم يُحفظ أي سجل.</p>}<AdminDataTable label="معاينة التسجيلات" rows={preview.rows} paginate={preview.rows.length > 25} rowKey={r => r.legacyEnrollmentId} columns={[
        { key: "player", header: "اللاعب", primary: true, render: r => <><b>{r.playerName}</b><small>{r.legacyPlayerId}</small></> },
        { key: "registration", header: "الرياضة والمجموعة", render: r => {
          const label = preview.registrationLabels?.[r.legacyEnrollmentId];
          const place = [label?.group, label?.branch].filter(Boolean).join(" · ");
          return <>{label?.sport || <bdi>{r.legacyEnrollmentId}</bdi>}{place && <small>{place}</small>}</>;
        } },
        { key: "guardian", header: "ولي الأمر", render: r => <>{r.guardianName}<small dir="ltr">{r.guardianPhone}</small></> },
        { key: "period", header: "فترة الاشتراك", render: r => <ImportPeriod start={r.start} end={r.end} /> },
        { key: "net", header: "الصافي التاريخي", render: r => r.agreedNet === null ? "غير معروف — مراجعة مالية" : <MoneyValue amount={Number(r.agreedNet)} currency={r.currency} /> },
      ]} /></>}
    </section>}
  </Wrapper>;
}

function ImportIssues({ issues }: { issues: Issue[] }) {
  const sheetId = useId();
  const [sheet, setSheet] = useState("");
  const [row, setRow] = useState("");
  const sheets = new Map(issues.map(issue => [issue.sheet, issue.sheetLabel ?? issue.sheet]));
  const countRows = (items: Issue[]) => new Set(items.filter(issue => issue.row > 0).map(issue => `${issue.sheet}:${issue.row}`)).size;
  const affectedRows = countRows(issues);
  const rowDigits = row.trim().replace(/[٠-٩۰-۹]/g, digit => String(digit.charCodeAt(0) - (digit < "۰" ? 0x660 : 0x6f0)));
  const rowNumber = /^[0-9]+$/.test(rowDigits) ? Number(rowDigits) : null;
  const filtered = issues.filter(issue => (!sheet || issue.sheet === sheet) &&
    (!rowDigits || (issue.row > 0 && issue.row === rowNumber)));
  const filtering = sheet !== "" || rowDigits !== "";
  const visibleRows = filtering ? countRows(filtered) : affectedRows;

  return <section className={styles.issues} aria-label="مراجعة أخطاء الملف">
    {(sheets.size > 1 || affectedRows > 1) && <div className={styles.issueFilters}>
      {sheets.size > 1 && <div className={styles.issueField}>
        <label htmlFor={sheetId}>الورقة</label>
        <select id={sheetId} value={sheet} onChange={event => setSheet(event.target.value)}>
          <option value="">كل الأوراق</option>
          {[...sheets].map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </select>
      </div>}
      {affectedRows > 1 && <label>رقم الصف في Excel<input type="text" inputMode="numeric" value={row} onChange={event => setRow(event.target.value)} /></label>}
      {filtering && <button type="button" className="secondary-button" onClick={() => { setSheet(""); setRow(""); }}>مسح التصفية</button>}
    </div>}
    <p className={styles.issueCount} role="status">
      <span>المشكلات المعروضة <strong>{filtered.length}</strong></span>
      {visibleRows > 0 && <span>الصفوف المتأثرة <strong>{visibleRows}</strong></span>}
    </p>
    {filtered.length === 0 ? <p>لا توجد مشكلات تطابق التصفية.</p> : <AdminDataTable
      label="مشكلات الاستيراد"
      paginate={filtered.length > 25}
      showTableControls={false}
      rows={filtered}
      rowKey={importIssueKey}
      columns={[
        { key: "row", header: "الورقة والصف", primary: true, width: "minmax(0, .6fr)", render: issue => <>{issue.sheetLabel ?? issue.sheet}{issue.row > 0 && <> · الصف {issue.row}</>}<small>{issue.columnLabel ?? issue.column}</small></> },
        { key: "problem", header: "التصحيح المطلوب", render: issue => issue.message },
      ]}
    />}
  </section>;
}

function ImportPeriod({ start, end }: { start?: string; end?: string | null }) {
  if (!start) return <>—</>;
  return <><span>البداية {formatDateAr(start)}</span>{end && <small>النهاية {formatDateAr(end)}</small>}</>;
}

function PublishedRecords({ records, platform }: { records: Published[]; platform: boolean }) {
  const many = records.length > 5;
  const table = <AdminDataTable label="نتيجة الاستيراد المعتمدة" rows={records} paginate={records.length > 25} rowKey={row => row.legacyEnrollmentId} columns={[
    { key: "player", header: "اللاعب", primary: true, render: row => platform ? <><b>{row.player}</b><small>{row.playerCode}</small></> : <Link href={`/dashboard/players/${row.id}`}><b>{row.player}</b><small>{row.playerCode}</small></Link> },
    { key: "service", header: "التسجيل", render: row => <>{row.sport}<small>{row.branch} · {row.groupName}</small></> },
    { key: "period", header: "فترة الاشتراك", render: row => <ImportPeriod start={row.startDate} end={row.endDate} /> },
    { key: "net", header: "الصافي التاريخي", render: row => row.priceSnapshot == null ? "غير معروف — مراجعة مالية" : <span className={styles.recordAmount}><MoneyValue amount={Number(row.priceSnapshot)} currency={row.currencySnapshot} /></span> },
  ]} />;
  return many ? <details className={styles.effects}>
    <summary>التسجيلات المحفوظة · {records.length}</summary>
    {table}
  </details> : table;
}

function HistoricalPaymentTotal({ data, recordCurrency }: { data?: HistoricalPaymentSummary; recordCurrency?: string }) {
  const total = data?.historicalPaymentTotal;
  if (typeof total !== "number" || !Number.isFinite(total) || total < 0) return <>إجمالي التحصيل السابق غير متاح.</>;
  const currency = [data?.historicalPaymentCurrency, recordCurrency].find(value => typeof value === "string" && /^[A-Z]{3}$/.test(value));
  return currency ? <>الإجمالي <MoneyValue amount={total} currency={currency} />.</>
    : <>الإجمالي {new Intl.NumberFormat("ar-EG-u-nu-latn", { maximumFractionDigits: 2 }).format(total)}، والعملة غير متاحة.</>;
}

function ImportResultSummary({ job, recordCurrency }: { job: Job; recordCurrency?: string }) {
  const published = importCount(job.publishedRows);
  const replayed = importCount(job.replayedRows) ?? 0;
  const payments = importCount(job.result?.historicalPayments);
  const replayedPayment = job.replayedRequest || (published === 0 && replayed > 0);
  return <>
    <p role="status" className={styles.resultCounts}>
      <span>{published === undefined ? "عدد التسجيلات الجديدة غير متاح" : `التسجيلات الجديدة ${published}`}</span>
      {replayed > 0 && <span>تسجيلات سبق استيرادها {replayed}</span>}
    </p>
    {payments ? <p>
      {replayedPayment ? "تم التحقق من" : "تتضمن الدفعة"} {payments} سند تحصيل سابق.{" "}
      <HistoricalPaymentTotal data={job.result} recordCurrency={recordCurrency} />
      {replayedPayment && " لم يُنشأ قبض جديد عند إعادة الرفع."}
    </p> : <p>بيانات الاشتراك وحدها لا تنشئ تحصيلًا أو إيصالًا.</p>}
  </>;
}

function ImportEffects({ data, preview = false }: {
  data: { effects?: ImportEffect[]; coachAssignments?: number; tournamentParticipations?: number }; preview?: boolean;
}) {
  const coaches = importCount(data.coachAssignments) ?? 0;
  const tournaments = importCount(data.tournamentParticipations) ?? 0;
  const effects = (Array.isArray(data.effects) ? data.effects : []).flatMap(effect => {
    if (!effect || typeof effect.sheet !== "string" || !effect.sheet.trim()) return [];
    const created = importCount(effect.create) ?? 0;
    const reused = importCount(effect.reuse) ?? 0;
    return created || reused ? [{ sheet: effect.sheet, created, reused }] : [];
  });
  if (!coaches && !tournaments && !effects.length) return null;
  return <details className={styles.effects}>
    <summary>{preview ? "الإضافة والربط من الملف" : "تفاصيل الاستيراد"}</summary>
    <dl className={styles.effectList}>
      {coaches > 0 && <div><dt>إسناد مدرب لمجموعة</dt><dd>{coaches}</dd></div>}
      {tournaments > 0 && <div><dt>مشاركة في البطولات</dt><dd>{tournaments}</dd></div>}
      {effects.map(({ sheet, created, reused }) => <div key={sheet}>
        <dt>{sheet}</dt>
        <dd>
          {created > 0 && <span>{preview ? "سيُضاف" : "أُضيف"} {created}</span>}
          {reused > 0 && <span>{preview ? "سيُستخدم من الموجود" : "استُخدم من الموجود"} {reused}</span>}
        </dd>
      </div>)}
    </dl>
  </details>;
}
