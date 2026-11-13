"use client";
import Link from "next/link";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { useSearchParams } from "next/navigation";
import { AdminDataTable, ConfirmationDialog, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { PaymentProofPreview } from "../components/payment-proof-preview";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { clearFinancialIntent, matchesFinancialIntent, pendingFinancialIntent, readFinancialIntentKey } from "../lib/pending-financial-intent";
import { paymentQueueBack, paymentQueuePath, paymentQueueQuery, paymentQueueReferencePattern, paymentQueueScope, paymentQueueStatuses, paymentQueueTypes, type PaymentQueueScope } from "../lib/payment-queue-navigation";
type Request = { id: string; reference?: string; canOpen?: boolean; canReview?: boolean | null; type: string; player: string; sport: string; branch: string; plan: string; amount: number | null; currency: string | null; status: string; hasProof: boolean; createdOn?: string };
type Queue = { total?: number; page?: number; pageSize?: number; from?: string; to?: string; pending: number; newEnrollments: number; renewals: number; products: number; tournaments?: number; canReviewRenewalProof?: boolean; items: Request[] };
const names: Record<string, string> = { Enrollment: "اشتراك جديد", Renewal: "تجديد", FamilyRenewal: "تجديد أسرة", Cancelled: "ملغي", Product: "منتج رياضي", Tournament: "رسم بطولة", Pending: "بانتظار المراجعة", Approved: "مقبول", Rejected: "مرفوض" };
const requestPages: Record<string, string> = {
  FamilyRenewal: "/dashboard/family-renewals",
  Tournament: "/dashboard/tournament-payments",
  Enrollment: "/dashboard/enrollment-requests",
  Product: "/dashboard/product-sales",
};
export function PaymentRequestQueue() {
  const defaults = useDateFilterDefaults();
  const params = useSearchParams();
  const [scope, setScope] = useState(() => paymentQueueScope(params, defaults));
  const [referenceDraft, setReferenceDraft] = useState(scope.reference);
  const [result, setResult] = useState<{ query: string; revision: number; data?: Queue; error?: string; retryable?: boolean }>();
  const [revision, setRevision] = useState(0);
  const [branches, setBranches] = useState<{ id: string; arabicName: string }[]>([]);
  const [hiddenColumns, setHiddenColumns] = useState<string[]>([]);
  const { type, status, from, to, branchId, page, pageSize, reference } = scope;
  const filters = paymentQueueQuery(scope);
  const query = filters.toString();
  const invalidRange = !!(from && to && from > to);
  const invalidReference = !!reference && !paymentQueueReferencePattern.test(reference);
  const current = result?.query === query && result.revision === revision ? result : undefined;
  const data = invalidRange || invalidReference ? undefined : current?.data;
  const total = data?.total ?? data?.items.length ?? 0;
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const detailsQuery = new URLSearchParams(filters);
  detailsQuery.set("origin", "payment-queue");

  function update(changes: Partial<PaymentQueueScope>) {
    const next = { ...scope, page: 1, ...changes };
    setScope(next);
    window.history.replaceState(null, "", `${paymentQueuePath}?${paymentQueueQuery(next)}`);
  }

  useEffect(() => {
    if (invalidRange || invalidReference) return;
    const controller = new AbortController();
    fetch(`/api/v1/manage/payment-requests?${query}`, { signal: controller.signal, cache: "no-store" }).then(async response => {
      if ([401, 403, 404, 422].includes(response.status)) {
        if (!controller.signal.aborted) setResult({ query, revision, error: response.status === 422
          ? "اختر فترة صحيحة لا تتجاوز 366 يومًا." : "طلبات الدفع غير متاحة في هذا النطاق." });
        return;
      }
      if (!response.ok) throw new Error();
      const data = await response.json() as Queue;
      if (!Array.isArray(data?.items) || data.total != null &&
        (!Number.isInteger(data.total) || data.total < 0 || data.page !== page || data.pageSize !== pageSize)) throw new Error();
      if (!controller.signal.aborted) setResult({ query, revision, data });
    }).catch(() => {
      if (!controller.signal.aborted) setResult({ query, revision, error: "تعذر تحميل طلبات الدفع.", retryable: true });
    });
    return () => controller.abort();
  }, [query, revision, invalidRange, invalidReference, page, pageSize]);
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/v1/manage/structure/options", { signal: controller.signal }).then(r => r.ok ? r.json() : null)
      .then(x => { if (!controller.signal.aborted) setBranches(x?.branches ?? []); }).catch(() => undefined);
    return () => controller.abort();
  }, []);
  return <><PageHeader title="طلبات الدفع" context="مراجعة التحويلات" />
    {status === "Pending" && <div className="request-counts" role="group" aria-label="طلبات بانتظار المراجعة">
      {[
        { key: "", label: "الكل", count: data?.pending },
        { key: "Enrollment", label: "اشتراكات جديدة", count: data?.newEnrollments },
        { key: "Renewal", label: "تجديد", count: data?.renewals },
        { key: "Product", label: "منتجات رياضية", count: data?.products },
        { key: "Tournament", label: "بطولات", count: data?.tournaments },
      ].map(item => <button type="button" key={item.key} aria-pressed={type === item.key} onClick={() => update({ type: item.key })}>{item.label} <b>{item.count ?? "—"}</b></button>)}
    </div>}
    <form className="report-filters payment-request-filters" onSubmit={event => { event.preventDefault(); update({ reference: referenceDraft.trim().slice(0, 37) }); }}>
      <label>رقم الطلب<input type="text" dir="ltr" autoComplete="off" spellCheck={false} value={referenceDraft} onChange={event => setReferenceDraft(event.target.value)} /></label>
      <button type="submit">بحث</button>
      {!!reference && <button type="button" aria-label="مسح رقم الطلب" onClick={() => { setReferenceDraft(""); update({ reference: "" }); }}>مسح البحث</button>}
    </form>
    <div className="report-filters payment-request-filters">
      <label>من تاريخ<input type="date" value={from} onChange={e => update({ from: e.target.value })} /></label>
      <label>إلى تاريخ<input type="date" value={to} onChange={e => update({ to: e.target.value })} /></label>
      <label>الفرع<select aria-label="الفرع" value={branchId} onChange={e => update({ branchId: e.target.value })}><option value="">كل الفروع</option>{branches.map(b => <option key={b.id} value={b.id}>{b.arabicName}</option>)}</select></label>
      <label>نوع الطلب<select aria-label="نوع الطلب" value={type} onChange={e => update({ type: e.target.value })}><option value="">الكل</option>{paymentQueueTypes.map(k => <option key={k} value={k}>{names[k]}</option>)}</select></label>
      <label>الحالة<select aria-label="الحالة" value={status} onChange={e => update({ status: e.target.value })}><option value="">الكل</option>{paymentQueueStatuses.map(k => <option key={k} value={k}>{names[k]}</option>)}</select></label>
      <button type="button" onClick={() => { setReferenceDraft(""); update({ type: "", status: "", from: "", to: "", branchId: "", reference: "" }); }}>مسح</button>
    </div>
    {invalidRange ? <ErrorState message="اختر تاريخ نهاية يوافق تاريخ البداية أو يأتي بعده." />
      : invalidReference ? <ErrorState message="أدخل رقم الطلب كاملًا كما يظهر في تفاصيله." />
      : current?.error ? <><ErrorState message={current.error}/>{current.retryable && <button type="button" className="button button-secondary" onClick={() => setRevision(value => value + 1)}>إعادة تحميل الطلبات</button>}</>
      : !data ? <LoadingState /> : <section className="list-card">{page > pages ? <EmptyState message="هذه الصفحة خارج نتائج طلبات الدفع." action={<button type="button" className="button button-secondary" onClick={() => update({ page: pages })}>عرض آخر صفحة</button>} />
      : !data.items.length ? <EmptyState message="لا توجد طلبات مطابقة." /> : <AdminDataTable label="طلبات الدفع" rows={data.items} rowKey={x=>`${x.type}-${x.id}`}
        columnVisibility={{ hidden: hiddenColumns, onChange: setHiddenColumns }}
        serverPagination={{ page: page - 1, pageSize, total, onPageChange: index => update({ page: index + 1 }), onPageSizeChange: pageSize => update({ pageSize }) }} columns={[
 {key:"player",header:"اللاعب أو الأسرة",primary:true,render:x=><><strong>{x.player}</strong><small>{x.plan}</small>{x.reference && <small>رقم الطلب <bdi dir="ltr" style={{overflowWrap:"anywhere"}}>{x.reference}</bdi></small>}</>},
 {key:"context",header:"الرياضة والفرع",render:x=>x.type === "FamilyRenewal" ? "حسب كل تسجيل" : <>{x.sport}<small>{x.branch}</small></>},
 {key:"type",header:"نوع الطلب",render:x=>names[x.type]??"طلب"},
 {key:"amount",header:"المبلغ",render:x=>typeof x.amount === "number" && Number.isFinite(x.amount) && x.currency ? formatMoneyAr(x.amount,x.currency) : "المبلغ غير محدد"},
 {key:"status",header:"المراجعة والإثبات",render:x=><>{names[x.status]??"حالة غير معروفة"}{x.status==="Pending"&&<small>{x.hasProof?"وصل الإثبات":"لم يصل إثبات"}</small>}</>},
 {key:"date",header:"تاريخ الطلب",render:x=>x.createdOn ? <time dateTime={x.createdOn}>{formatDateAr(x.createdOn)}</time> : "غير متاح"},
 {key:"action",header:"الإجراء",render:x=>{
   if (x.canOpen === false) return "تفاصيل غير متاحة";
   const path = requestPages[x.type] ?? "/dashboard/subscriptions/payments";
   const canReview = x.canReview ?? (x.type !== "Renewal" || data.canReviewRenewalProof !== false);
   const label = x.status === "Pending" && canReview ? "مراجعة" : "عرض الطلب";
   return <Link className="row-primary-action" href={`${path}/${x.id}?${detailsQuery}`}>{label}</Link>;
 }},
 ]}/>}</section>}<TransferSettings /></>;
}
type Settings = { instaPayAddress?: string|null; walletNumber?: string|null; recipientName?: string|null; configured: boolean; isActive?:boolean };
type TransferDraft={instaPayAddress:string;walletNumber:string;recipientName:string;isActive:boolean};
const transferDraft=(s:Settings):TransferDraft=>({instaPayAddress:s.instaPayAddress?.trim()??"",walletNumber:s.walletNumber?.trim()??"",recipientName:s.recipientName?.trim()??"",isActive:s.isActive??s.configured});
async function readTransferSettings(signal?:AbortSignal){const r=await fetch('/api/v1/payment-settings',{cache:'no-store',signal});if(!r.ok)throw Error('تعذر تحميل إعدادات التحويل.');return await r.json() as Settings;}
async function readTransferContext(signal?:AbortSignal){
 const permission=await fetch('/api/v1/my-permissions',{cache:'no-store',signal});
 if(!permission.ok)throw Error('تعذر التحقق من صلاحية إعدادات التحويل.');
 const values=await permission.json();const allowed=Array.isArray(values)&&values.includes('academy.settings.manage');
 return {allowed,settings:allowed?await readTransferSettings(signal):null};
}
export function TransferSettings({ expanded = false }: { expanded?: boolean }) {
 const [settings,setSettings]=useState<Settings|null>(null),[allowed,setAllowed]=useState<boolean|null>(null),[message,setMessage]=useState(""),[error,setError]=useState(""),[busy,setBusy]=useState(false),[pending,setPending]=useState<TransferDraft|null>(null),[revision,setRevision]=useState(0);
 const mounted=useRef(false),locked=useRef(false),intent=useRef<TransferDraft|null>(null);
 const accept=useCallback((next:{allowed:boolean;settings:Settings|null})=>{
  if(!mounted.current)return;
  setAllowed(next.allowed);setSettings(next.settings);setError('');setMessage('');setPending(null);intent.current=null;locked.current=false;setRevision(v=>v+1);
 },[]);
 const load=useCallback(async()=>{
  try{accept(await readTransferContext());}
  catch(e){if(mounted.current)setError(e instanceof Error?e.message:'تعذر تحميل إعدادات التحويل.');}
 },[accept]);
 useEffect(()=>{mounted.current=true;const c=new AbortController();
  readTransferContext(c.signal).then(next=>{if(!c.signal.aborted)accept(next);}).catch(e=>{if(!c.signal.aborted)setError(e instanceof Error?e.message:'تعذر تحميل إعدادات التحويل.');});
  return()=>{mounted.current=false;c.abort();};
 },[accept]);
 async function verify(expected:TransferDraft){
  const next=await readTransferSettings();if(!mounted.current)return;
  const actual=transferDraft(next);
  if((Object.keys(expected) as (keyof TransferDraft)[]).some(k=>actual[k]!==expected[k]))throw Error('لم تتأكد مطابقة إعدادات التحويل المحفوظة. أعد قراءة الإعدادات الحالية قبل تعديلها.');
  setSettings(next);setRevision(v=>v+1);setMessage('تم حفظ حسابات التحويل وتأكيد قراءتها.');setPending(null);intent.current=null;
 }
 async function save(e:FormEvent<HTMLFormElement>){
  e.preventDefault();if(locked.current||!settings)return;
  const f=new FormData(e.currentTarget);const expected:TransferDraft={instaPayAddress:String(f.get('instapay')??'').trim(),walletNumber:String(f.get('wallet')??'').trim(),recipientName:String(f.get('recipient')??'').trim(),isActive:f.get('active')==='on'};
  if(expected.isActive&&!expected.instaPayAddress&&!expected.walletNumber){setError('أدخل حساب إنستا باي أو رقم المحفظة قبل تفعيل التحويل.');return;}
  locked.current=true;intent.current=expected;setPending(expected);setBusy(true);setError('');setMessage('');
  try{await csrfRequest('/api/v1/payment-settings','PUT',expected,{}, {announce:false});await verify(expected);}
  catch(e){if(mounted.current)setError(e instanceof Error?e.message:'تعذر تأكيد حفظ إعدادات التحويل.');}
  finally{if(mounted.current){locked.current=intent.current!==null;setBusy(false);}}
 }
 async function retryRead(){if(busy||!intent.current)return;setBusy(true);setError('');try{await verify(intent.current);}catch(e){if(mounted.current)setError(e instanceof Error?e.message:'تعذر تأكيد إعدادات التحويل.');}finally{if(mounted.current){locked.current=intent.current!==null;setBusy(false);}}}
 if(allowed===false)return null;
 return <details className="detail-card" open={expanded||undefined}><summary>إعدادات الدفع — إنستا باي والمحفظة</summary>{error&&<ErrorState message={error}/>} {!settings?!error?<LoadingState/>:<button onClick={()=>void load()}>إعادة تحميل إعدادات التحويل</button>:<form key={revision} className="form-shell" onSubmit={e=>void save(e)}><fieldset disabled={busy||pending!==null}><legend>وسائل التحويل المتاحة للأسرة</legend><label className="check-inline"><input type="checkbox" name="active" defaultChecked={transferDraft(settings).isActive}/>تفعيل استقبال إثباتات التحويل</label><label>حساب إنستا باي<input name="instapay" dir="ltr" maxLength={120} defaultValue={settings.instaPayAddress??''}/></label><label>رقم المحفظة<input name="wallet" dir="ltr" maxLength={30} defaultValue={settings.walletNumber??''}/></label><label>اسم المستلم<input name="recipient" maxLength={120} defaultValue={settings.recipientName??''}/></label><button className="primary-button">{busy?'جارٍ الحفظ…':'حفظ'}</button></fieldset>{pending&&!busy&&<div className="form-actions"><button type="button" onClick={()=>void retryRead()}>إعادة التحقق من الحفظ</button><button type="button" onClick={()=>void load()}>إعادة قراءة الإعدادات الحالية</button></div>}{message&&<p role="status">{message}</p>}</form>}</details>;
}
type RenewalProof = { id: string; assetId: string; url: string; status: string; reviewReason?: string | null };
type RenewalProofGroup = { id: string; arabicName: string; branchId: string; sportId: string };
type RenewalProofDecision = { accept: boolean; groupId: string | null; reason: string | null; expectedProofAssetId: string };

type ManualPaymentReviewProps = {
  id: string;
  branchId?: string;
  sportId?: string;
  groupId?: string;
  allowReview: boolean;
  amount: number;
  currency: string;
  onReviewStarted: () => void;
  onReviewed: (error?: string) => Promise<unknown>;
};

export function ManualPaymentReview({ id, branchId, sportId, groupId, allowReview, amount, currency, onReviewStarted, onReviewed }: ManualPaymentReviewProps) {
  const [proof, setProof] = useState<RenewalProof | null>();
  const [readyProof, setReadyProof] = useState<string>();
  const [groups, setGroups] = useState<RenewalProofGroup[]>();
  const [selected, setSelected] = useState(groupId ?? "");
  const [reason, setReason] = useState("");
  const [readError, setReadError] = useState("");
  const [reviewError, setReviewError] = useState("");
  const [busy, setBusy] = useState(false);
  const [decision, setDecision] = useState<RenewalProofDecision>();
  const [needsReload, setNeedsReload] = useState(false);
  const writing = useRef(false);
  const mounted = useRef(false);
  const path = `/api/v1/manage/payments/${encodeURIComponent(id)}`;
  const eligibleGroups = groups?.filter(group => group.branchId === branchId && group.sportId === sportId && (!groupId || group.id === groupId));
  const actionable = allowReview && !needsReload && proof?.status === "Pending";
  const validAmount = typeof amount === "number" && Number.isFinite(amount) && amount >= 0 && /^[A-Z]{3}$/.test(currency);
  const canAccept = !!proof && readyProof === proof.url && validAmount && eligibleGroups?.some(group => group.id === selected) === true;

  useEffect(() => {
    mounted.current = true;
    const controller = new AbortController();
    const options = { signal: controller.signal, cache: "no-store" as const };
    async function load() {
      const response = await fetch(`${path}/proof`, options);
      if (controller.signal.aborted) return;
      if (response.status === 404) { setProof(null); return; }
      if (!response.ok) throw new Error();
      const proof = await response.json() as RenewalProof;
      if (!proof.id || !/^[a-f0-9-]{36}$/i.test(proof.assetId) || proof.url !== `/api/v1/assets/${proof.assetId}`) throw new Error();
      if (controller.signal.aborted) return;
      setProof(proof);
      if (!allowReview || proof.status !== "Pending") return;
      const structure = await fetch("/api/v1/manage/structure/options", options);
      if (!structure.ok) throw new Error();
      const { groups } = await structure.json() as { groups: RenewalProofGroup[] };
      if (!Array.isArray(groups)) throw new Error();
      if (!controller.signal.aborted) setGroups(groups);
    }
    void load().catch(() => {
      if (!controller.signal.aborted) setReadError("تعذر تحميل بيانات إثبات التحويل.");
    });
    return () => { mounted.current = false; controller.abort(); };
  }, [path, allowReview]);

  function prepare(accept: boolean) {
    if (!proof || !actionable || (accept && !canAccept)) return;
    setDecision({
      accept,
      groupId: accept ? selected : null,
      reason: reason.trim() || null,
      expectedProofAssetId: proof.assetId,
    });
  }

  async function savedDecision(attempt: RenewalProofDecision, source: RenewalProof) {
    const options = { cache: "no-store" as const };
    const [payment, evidence] = await Promise.all([
      fetch(`/api/v1/subscriptions/payments/${encodeURIComponent(id)}`, options),
      fetch(`${path}/proof`, options),
    ]);
    if (!payment.ok || !evidence.ok) return false;
    const [saved, reviewed] = await Promise.all([payment.json(), evidence.json()]);
    return saved.id === id && saved.isCurrent === true && saved.amount === amount && saved.currency === currency &&
      saved.branchId === branchId && saved.sportId === sportId &&
      reviewed.id === source.id && reviewed.assetId === attempt.expectedProofAssetId && reviewed.url === source.url &&
      (reviewed.reviewReason ?? null) === attempt.reason &&
      (attempt.accept
        ? saved.status === "Confirmed" && reviewed.status === "Accepted" && saved.groupId === attempt.groupId && !!saved.receiptId && !!saved.periodId
        : saved.status === "Cancelled" && reviewed.status === "Rejected" && !saved.receiptId && !saved.periodId);
  }

  async function review() {
    if (writing.current || !decision || !proof || !actionable || (decision.accept && !canAccept)) return;
    writing.current = true;
    onReviewStarted();
    setBusy(true);
    setReviewError("");
    try {
      await csrfRequest(`${path}/review`, "POST", decision, {}, { announce: false });
      if (mounted.current) await onReviewed();
    } catch (caught) {
      if (!mounted.current) return;
      if (caught instanceof DashboardRequestError && caught.status === 422) {
        setReviewError(caught.message);
      } else if (caught instanceof DashboardRequestError && caught.status >= 400 && caught.status < 500 && caught.status !== 408) {
        await onReviewed(caught.message);
      } else {
        const saved = await savedDecision(decision, proof).catch(() => false);
        if (!mounted.current) return;
        if (saved) await onReviewed();
        else {
          setNeedsReload(true);
          setReviewError("تعذر تأكيد المراجعة. أعد تحميل الطلب وراجع حالته قبل اتخاذ قرار.");
        }
      }
    } finally {
      writing.current = false;
      if (mounted.current) { setBusy(false); setDecision(undefined); }
    }
  }

  if (proof === null) return null;
  return <section className="detail-card" aria-labelledby="renewal-proof-heading">
    <h2 id="renewal-proof-heading">إثبات التحويل</h2>
    {readError || needsReload ? <>
      <ErrorState message={readError || reviewError} />
      <button className="button button-secondary" type="button" onClick={() => void onReviewed()}>إعادة تحميل الطلب</button>
    </> : !proof && <LoadingState message="جارٍ التحقق من إثبات التحويل…" />}
    {proof && !needsReload && <>
      <PaymentProofPreview key={proof.url} url={proof.url} description="صورة إثبات تحويل التجديد"
        linkLabel="فتح إثبات التحويل" onReady={ready => setReadyProof(ready ? proof.url : undefined)} />
      {actionable && !readError ? <div className="form-shell payment-proof-form">
        {!eligibleGroups ? <LoadingState message="جارٍ تحميل المجموعة…" /> : groupId ? <p>المجموعة الحالية: <strong>{eligibleGroups[0]?.arabicName ?? "غير متاحة"}</strong></p> : <label>
          المجموعة<select value={selected} disabled={busy || !eligibleGroups.length} onChange={event => setSelected(event.target.value)}>
            <option value="">اختر المجموعة</option>
            {eligibleGroups.map(group => <option key={group.id} value={group.id}>{group.arabicName}</option>)}
          </select>
        </label>}
        {eligibleGroups?.length === 0 && <p>لا توجد مجموعة متاحة لهذا التسجيل. راجع التسكين قبل قبول الإثبات.</p>}
        {!validAmount && <p className="form-hint">تعذر التحقق من مبلغ الطلب وعملته.</p>}
        <label>ملاحظة للأسرة (اختياري)<textarea maxLength={500} disabled={busy} value={reason} onChange={event => setReason(event.target.value)} /></label>
        {reviewError && <ErrorState message={reviewError} />}
        <div className="form-actions">
          <button className="button button-primary" type="button" disabled={busy || !canAccept} onClick={() => prepare(true)}>قبول الإثبات</button>
          <button className="button button-danger" type="button" disabled={busy} onClick={() => prepare(false)}>رفض الإثبات</button>
        </div>
      </div> : <p>{proof.status === "Accepted" ? "تم قبول الإثبات" : proof.status === "Rejected" ? "تم رفض الإثبات" : "الإثبات قيد المراجعة"}{proof.reviewReason && ` · ${proof.reviewReason}`}</p>}
    </>}
    <ConfirmationDialog open={decision !== undefined} title={decision?.accept ? "قبول إثبات التحويل" : "رفض إثبات التحويل"}
      message={decision?.accept ? `أكد استلام ${formatMoneyAr(amount, currency)}. سيُسجّل التحصيل ويبدأ التجديد حسب الفترة المحددة.` : "سيُغلق طلب الدفع وتظهر ملاحظتك للأسرة. رد التحويل له إجراء منفصل."}
      confirmLabel={decision?.accept ? "تسجيل التحصيل" : "تأكيد الرفض"} tone={decision?.accept ? "primary" : "danger"} busy={busy}
      onConfirm={() => void review()} onCancel={() => setDecision(undefined)} />
  </section>;
}
type ProductPayment = {
  id: string; productId?: string; productName: string; branchId?: string; branchName?: string; player?: string; reference?: string; createdOn?: string;
  playerId?: string; sportEnrollmentId?: string; variantId?: string;
  sizeSnapshot?: string; colorSnapshot?: string; shirtNumberSnapshot?: number | null;
  quantity: number; unitPrice?: number; amount: number; currency: string; status: string;
  paymentMethod?: string; confirmedAtUtc?: string; timeZone?: string; reviewReason?: string | null;
  cashSessionId?: string; proofUrl?: string; proofAssetId?: string; canReview?: boolean; canAccept?: boolean;
};
type ProductDecision = { accept: boolean; reason: string | null; expectedProofAssetId: string };
type ProductAttempt = { source: ProductPayment; decision: ProductDecision; acknowledged?: boolean; retryAllowed?: boolean };

const productProofFields = [
  "id", "productId", "productName", "branchId", "playerId", "sportEnrollmentId", "variantId", "sizeSnapshot",
  "colorSnapshot", "shirtNumberSnapshot", "quantity", "unitPrice", "amount", "currency", "proofAssetId", "proofUrl",
] as const;

function productDecisionSnapshot(source: ProductPayment, status: string, reason: string | null) {
  return { order: productProofFields.map(field => source[field]), status, reason };
}

export function ProductPaymentReview({ id }: { id: string }) {
  return <ProductPaymentDetail key={id} id={id} />;
}

function ProductPaymentDetail({ id }: { id: string }) {
  const queueBack = paymentQueueBack(useSearchParams());
  const path = `/api/v1/manage/product-sales/${id}`;
  const intentScope = `product-review:${id}`;
  const [data, setData] = useState<ProductPayment | null>(null);
  const [reason, setReason] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  const [unavailable, setUnavailable] = useState(false);
  const [review, setReview] = useState<ProductAttempt | null>(null);
  const [attempt, setAttempt] = useState<ProductAttempt | null>(null);
  const [recovered, setRecovered] = useState<{ key: string; pending?: boolean } | null>(null);
  const [preview, setPreview] = useState<{ url: string | null; ready: boolean }>({ url: null, ready: false });
  const running = useRef(false);

  const read = useCallback(async (signal?: AbortSignal): Promise<ProductPayment> => {
    const response = await fetch(path, { cache: "no-store", signal });
    if (!response.ok) throw new DashboardRequestError("تعذر تحميل تفاصيل الطلب.", response.status);
    const value = await response.json();
    if (value?.id !== id) throw new Error();
    return value;
  }, [id, path]);

  function denied(cause: unknown) {
    return cause instanceof DashboardRequestError && [401, 403, 404].includes(cause.status);
  }

  useEffect(() => {
    const controller = new AbortController();
    void read(controller.signal).then(value => {
      if (controller.signal.aborted) return;
      const key = readFinancialIntentKey(intentScope);
      setRecovered(key ? { key } : null);
      setData(value);
    }).catch(cause => {
      if (!controller.signal.aborted) {
        setData(null);
        setUnavailable(denied(cause));
        setError(denied(cause) ? "هذا الطلب غير متاح لهذا الحساب." : "تعذر تحميل تفاصيل الطلب.");
      }
    });
    return () => controller.abort();
  }, [intentScope, read, revision]);

  function readingFailed(cause: unknown, fallback: string) {
    if (denied(cause)) {
      setData(null);
      setAttempt(null);
      setUnavailable(true);
      setError("هذا الطلب غير متاح لهذا الحساب.");
    } else {
      setError(fallback);
    }
  }

  async function run(task: () => Promise<void>) {
    if (running.current) return;
    running.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    try { await task(); }
    finally { running.current = false; setBusy(false); }
  }

  async function check(command: ProductAttempt) {
    try {
      const current = await read();
      const sameSource = productProofFields.every(field => command.source[field] === current[field]);
      const status = command.decision.accept ? "Confirmed" : "Rejected";
      if (sameSource && current.status === status && (current.reviewReason?.trim() || null) === command.decision.reason) {
        setData(current);
        clearFinancialIntent(intentScope);
        setAttempt(null);
        setReason("");
        setNotice("ظهر القرار المحفوظ في الطلب.");
      } else if (!sameSource || current.status !== "Pending") {
        if (command.acknowledged) throw new Error();
        setData(current);
        clearFinancialIntent(intentScope);
        setAttempt(null);
        setError(current.status === "Pending" ? "تغيّر إثبات الدفع. افتح الإثبات الحالي قبل اتخاذ القرار." : "ظهر قرار مختلف على الطلب. راجع الحالة المحفوظة.");
      } else if (!command.acknowledged) {
        setAttempt({ ...command, retryAllowed: true });
        setError("الطلب ما زال بانتظار القرار. يمكنك إعادة إرسال نفس المراجعة.");
      } else {
        throw new Error();
      }
    } catch (cause) {
      readingFailed(cause, "تعذر مطابقة القرار المحفوظ. أعد التحقق من الطلب.");
    }
  }

  async function recover(key: string) {
    await run(async () => {
      try {
        const current = await read();
        if (current.status === "Pending") {
          setData(current);
          setRecovered({ key, pending: true });
          return;
        }
        const saved = ["Confirmed", "Rejected"].includes(current.status) &&
          await matchesFinancialIntent(intentScope, key, productDecisionSnapshot(current, current.status, current.reviewReason?.trim() || null));
        clearFinancialIntent(intentScope);
        setRecovered(null);
        setData(current);
        if (saved) setNotice("ظهر القرار المحفوظ في الطلب.");
        else setError("ظهر قرار مختلف على الطلب. راجع الحالة المحفوظة.");
      } catch (cause) {
        readingFailed(cause, "تعذر مطابقة القرار المحفوظ. أعد التحقق من الطلب.");
      }
    });
  }

  async function send(command: ProductAttempt) {
    await run(async () => {
      setReview(null);
      setAttempt({ ...command, retryAllowed: false });
      try {
        await pendingFinancialIntent(intentScope, productDecisionSnapshot(command.source, command.decision.accept ? "Confirmed" : "Rejected", command.decision.reason));
      } catch {
        setAttempt(null);
        setError("تعذر بدء المراجعة. أعد فتح التأكيد.");
        return;
      }
      try {
        await csrfRequest(`${path}/review`, "POST", command.decision, {}, { announce: false, signal: AbortSignal.timeout(20000) });
        const acknowledged = { ...command, acknowledged: true, retryAllowed: false };
        setAttempt(acknowledged);
        await check(acknowledged);
      } catch (cause) {
        if (denied(cause)) {
          readingFailed(cause, "");
        } else if (cause instanceof DashboardRequestError && [400, 409, 422].includes(cause.status)) {
          try {
            setData(await read());
            clearFinancialIntent(intentScope);
            setAttempt(null);
            setError(cause.message);
          } catch (error) {
            readingFailed(error, "تعذر تحديث الطلب. أعد التحقق قبل مراجعة الإثبات.");
          }
        } else {
          setError("تعذر تأكيد حفظ المراجعة. تحقّق من الطلب قبل اتخاذ قرار آخر.");
        }
      }
    });
  }

  const proof = data?.proofAssetId && /^[a-f0-9-]{36}$/i.test(data.proofAssetId) &&
    data.proofUrl === `/api/v1/assets/${data.proofAssetId}` ? data.proofUrl : null;
  if (preview.url !== proof) setPreview({ url: proof, ready: false });
  const counter = data?.status === "Confirmed" && !proof;
  const pending = data?.status === "Pending";
  const validCurrency = typeof data?.currency === "string" && /^[A-Z]{3}$/.test(data.currency);
  const validTotal = data != null && Number.isFinite(data.amount) && data.amount >= 0 && validCurrency;
  const unitPrice = data?.unitPrice;
  const validUnitPrice = typeof unitPrice === "number" && Number.isFinite(unitPrice) && unitPrice >= 0;
  const validQuantity = data != null && Number.isSafeInteger(data.quantity) && data.quantity > 0;
  const validPrice = validTotal && validQuantity && validUnitPrice &&
    Math.round(data.amount * 100) === Math.round(unitPrice * data.quantity * 100);
  const canAccept = !!data?.canAccept && !!proof && preview.url === proof && preview.ready && validPrice;
  const amountLabel = validTotal ? formatMoneyAr(data.amount, data.currency) : "المبلغ غير متاح";
  const method = data?.paymentMethod === "Cash" ? "نقدي" : data?.paymentMethod === "Online" ? "تحويل إلكتروني" : null;
  const confirmed = data?.confirmedAtUtc ? new Date(data.confirmedAtUtc) : null;
  const when = confirmed && Number.isFinite(confirmed.valueOf()) ? new Intl.DateTimeFormat("ar-EG-u-nu-latn", {
    dateStyle: "medium", timeStyle: "short", timeZone: data?.timeZone ?? "Africa/Cairo",
  }).format(confirmed) : null;
  const variant = [data?.colorSnapshot, data?.sizeSnapshot, data?.shirtNumberSnapshot == null ? null : `رقم ${data.shirtNumberSnapshot}`].filter(Boolean).join(" · ");
  const status = pending ? proof ? "إثبات قيد المراجعة" : "بانتظار إثبات الدفع"
    : data?.status === "Confirmed" ? "تم التحصيل" : names[data?.status ?? ""] ?? "حالة غير معروفة";

  function prepare(accept: boolean) {
    if (!data?.proofAssetId || !proof || !pending || !data.canReview || accept && !canAccept || attempt || recovered || busy) return;
    setReview({ source: data, decision: { accept, reason: reason.trim() || null, expectedProofAssetId: data.proofAssetId } });
  }

  return <>
    <PageHeader title={counter ? "تفاصيل بيع منتج" : "طلب منتج رياضي"} context={counter ? "المنتجات الرياضية" : "طلبات الدفع"}
      back={queueBack ?? (counter ? { label: "العودة إلى المنتجات", href: "/dashboard/content/catalog" }
        : { label: "العودة إلى طلبات الدفع", href: paymentQueuePath })} />
    {error && <ErrorState message={error} />}
    {notice && <p role="status" className="success-note">{notice}</p>}
    {!data && (error ? !unavailable && <button type="button" className="secondary-button" onClick={() => { setError(""); setRevision(value => value + 1); }}>إعادة تحميل التفاصيل</button> : <LoadingState />)}
    {data && <section className="detail-card product-sale-detail">
      <h2>{data.productName}</h2>
      <div className="product-sale-total"><strong>{amountLabel}</strong><span>{status}</span></div>
      <dl className="detail-grid">
        {data.reference && <div><dt>رقم الطلب</dt><dd><bdi dir="ltr">{data.reference}</bdi></dd></div>}
        {data.createdOn && <div><dt>تاريخ الطلب</dt><dd><time dateTime={data.createdOn}>{formatDateAr(data.createdOn)}</time></dd></div>}
        <div><dt>الكمية</dt><dd>{validQuantity ? data.quantity : "غير متاحة"}</dd></div>
        <div><dt>سعر القطعة</dt><dd>{validUnitPrice && validCurrency ? formatMoneyAr(unitPrice, data.currency) : "غير متاح"}</dd></div>
        {data.branchName && <div><dt>الفرع</dt><dd>{data.branchName}</dd></div>}
        {method && <div><dt>طريقة الدفع</dt><dd>{method}</dd></div>}
        {when && <div><dt>وقت التحصيل</dt><dd>{when}</dd></div>}
        {variant && <div><dt>الصنف</dt><dd>{variant}</dd></div>}
        {data.player && <div><dt>اللاعب</dt><dd>{data.player}</dd></div>}
        {data.reviewReason && <div><dt>رد الإدارة</dt><dd>{data.reviewReason}</dd></div>}
      </dl>
      {counter && <div className="form-actions">
        {data.productId && <Link className="secondary-button" href={`/dashboard/content/catalog/${data.productId}`}>تفاصيل المنتج</Link>}
        {data.cashSessionId && <Link className="secondary-button" href={`/dashboard/finance/cash-registers/sessions/${data.cashSessionId}`}>عرض وردية التحصيل</Link>}
      </div>}
      {proof && <PaymentProofPreview key={proof} url={proof} description="صورة إثبات دفع المنتج"
        linkLabel="فتح إثبات الدفع" onReady={ready => setPreview(current => current.url === proof ? { url: proof, ready } : current)} />}
      {recovered ? <section aria-labelledby="product-review-pending" aria-busy={busy}>
        <h3 id="product-review-pending">قرار المراجعة بانتظار التحقق</h3>
        {recovered.pending && <p role="status">الطلب ما زال معلقًا. راجع الإثبات الحالي قبل اتخاذ قرار.</p>}
        <div className="form-actions">
          <button type="button" className="primary-button" disabled={busy} onClick={() => void recover(recovered.key)}>التحقق من القرار</button>
          {recovered.pending && data.canReview && <button type="button" className="secondary-button" disabled={busy} onClick={() => {
            clearFinancialIntent(intentScope);
            setRecovered(null);
            setError("");
          }}>فتح مراجعة جديدة</button>}
        </div>
      </section> : attempt ? <section aria-labelledby="product-review-pending" aria-busy={busy}>
        <h3 id="product-review-pending">قرار المراجعة بانتظار التحقق</h3>
        <div className="form-actions">
          <button type="button" className="primary-button" disabled={busy} onClick={() => void run(() => check(attempt))}>التحقق من القرار</button>
          {attempt.retryAllowed && <button type="button" className="secondary-button" disabled={busy} onClick={() => void send(attempt)}>إعادة إرسال نفس القرار</button>}
        </div>
      </section> : pending && proof && data.canReview && <div className="form-shell">
        {data.canAccept && !validPrice && <ErrorState message="تعذر التحقق من الكمية وسعر الطلب. راجع التفاصيل قبل قبول الدفع." />}
        <label>ملاحظة لولي الأمر (اختياري)<textarea value={reason} maxLength={500} onChange={event => setReason(event.target.value)} /></label>
        <div className="form-actions">
          {data.canAccept && <button type="button" className="primary-button" disabled={busy || !canAccept} onClick={() => prepare(true)}>قبول وخصم المخزون</button>}
          <button type="button" className="secondary-button" disabled={busy} onClick={() => prepare(false)}>رفض الطلب</button>
        </div>
      </div>}
    </section>}
    <ConfirmationDialog open={review !== null} title={review?.decision.accept ? "قبول طلب المنتج" : "رفض طلب المنتج"}
      message={review ? `${review.source.productName} · ${amountLabel}. ${review.decision.accept ? "أكد وصول التحويل ومراجعة الإثبات. سيُخصم الصنف المسجل من المخزون." : "سيظهر الرفض وملاحظتك لولي الأمر."}` : ""}
      confirmLabel="تأكيد المراجعة" busy={busy} tone={review?.decision.accept ? "primary" : "danger"}
      onConfirm={() => {
        if (!review) return;
        if (review.decision.accept && (!canAccept || proof !== review.source.proofUrl)) setReview(null);
        else void send(review);
      }} onCancel={() => setReview(null)} />
  </>;
}
