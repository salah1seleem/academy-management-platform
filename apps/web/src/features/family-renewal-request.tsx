"use client";
import { ConfirmationDialog, PageHeader } from "../components/dashboard-shell";
import {FilePicker} from "../components/file-picker";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { GuardianShell } from "../components/guardian-shell";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { paymentQueueBack } from "../lib/payment-queue-navigation";
import { csrfRequest, DashboardRequestError, uploadAsset } from "./dashboard-api";
import styles from "./family-renewal-quote.module.css";

type Line = { discountReason?:string; id: string; player: string; sport: string; branch: string; plan: string; originalAmount: string; discountAmount: string; finalAmount: string; currency: string; groupId?: string; receiptId?: string; groups: { id: string; name: string }[] };
export type FamilyRequest = { id: string; revision: number; status: string; canReview?: boolean; lineCount: number; originalAmount: string; discountAmount: string; totalAmount: string; currency: string; proofUrl?: string; decisionReason?: string; items: Line[] };
const labels: Record<string, string> = { Pending: "بانتظار المراجعة", Approved: "تم تجديد الأسرة", Rejected: "لم يُقبل الطلب", Cancelled: "ملغي" };
const guardianRoot = "/api/v1/guardian/subscriptions/family/requests";
function failure(e: unknown) { return e instanceof DashboardRequestError ? e.message : "تعذر تأكيد العملية. حدّث الطلب أو أعد المحاولة؛ لا تكرر التحويل."; }

export function FamilyRenewalRequests() {
  const [data, setData] = useState<{ id: string; status: string; lineCount: number; amount: string; currency: string; createdAtUtc: string }[] | null>(null), [error, setError] = useState("");
  useEffect(() => { const controller = new AbortController(); void fetch(guardianRoot, { cache: "no-store", signal: controller.signal }).then(async r => { if (!r.ok) throw new Error(); setData((await r.json()).items); }).catch(e => { if (e.name !== "AbortError") setError("تعذر تحميل طلبات الأسرة."); }); return () => controller.abort(); }, []);
  return <GuardianShell><section className={styles.page}><Link href="/guardian/renew-family">تجديد الأسرة</Link><header><h1>طلبات تجديد الأسرة</h1><p>طلب واحد وإثبات واحد؛ ويمكنك متابعة كل إيصال بعد الاعتماد.</p></header>{error && <p role="alert">{error}</p>}{!data && !error && <p role="status">جارٍ التحميل…</p>}{data?.length === 0 && <p>لا توجد طلبات أسرة بعد.</p>}{data?.map(r => <Link className={styles.card} href={`/guardian/family-renewals/${r.id}`} key={r.id}><strong>{labels[r.status]} · {r.lineCount} تسجيل</strong><span>{formatMoneyAr(r.amount, r.currency)}</span><small>{formatDateAr(r.createdAtUtc)}</small></Link>)}</section></GuardianShell>;
}

const paymentQueue = "/dashboard/subscriptions/payments?type=Renewal&allDates=true";
const requestUnavailable = "طلب الأسرة غير متاح.";
const requestLoadFailed = "تعذر تحميل طلب الأسرة. أعد المحاولة.";

type Decision = "approve" | "reject" | "cancel";
const decisionStatus: Record<Decision, string> = { approve: "Approved", reject: "Rejected", cancel: "Cancelled" };

function registrationCount(count: number) {
  if (count === 1) return "تسجيل واحد";
  if (count === 2) return "تسجيلين";
  return `${count} ${count <= 10 ? "تسجيلات" : "تسجيلًا"}`;
}

async function requestView(response: Response, id: string) {
  const value = await response.json().catch(() => null) as FamilyRequest | null;
  if (value?.id !== id || !Array.isArray(value.items) || !value.items.length || value.items.length !== value.lineCount ||
      !Number.isInteger(value.revision) || !Object.hasOwn(labels, value.status)) throw new Error();
  return value;
}

export function FamilyRenewalRequest({ id, staff = false }: { id: string; staff?: boolean }) {
  return <FamilyRenewalDetails key={`${staff}:${id}`} id={id} staff={staff} />;
}

function FamilyRenewalDetails({ id, staff }: { id: string; staff: boolean }) {
  const queueBack = paymentQueueBack(useSearchParams());
  const root = staff ? "/api/v1/manage/family-renewals" : guardianRoot;
  const endpoint = `${root}/${encodeURIComponent(id)}`;
  const [data, setData] = useState<FamilyRequest | null>(null);
  const [groups, setGroups] = useState<Record<string, string>>({});
  const [reason, setReason] = useState("");
  const [error, setError] = useState("");
  const [loadError, setLoadError] = useState<{ message: string; retry: boolean }>();
  const [loading, setLoading] = useState(true);
  const [readRevision, setReadRevision] = useState(0);
  const [needsCheck, setNeedsCheck] = useState(false);
  const [busy, setBusy] = useState(false);
  const [decision, setDecision] = useState<Decision>();
  const writing = useRef(false);
  const canApprove = !!data?.proofUrl && data.items.every(line =>
    line.groups.some(group => group.id === (line.groupId || groups[line.id])));
  const canDecide = data?.status === "Pending" && (!staff || data.canReview) && !needsCheck && !loading;

  useEffect(() => {
    const controller = new AbortController();
    void fetch(endpoint, { cache: "no-store", signal: controller.signal }).then(async response => {
      if (!response.ok) {
        if (!controller.signal.aborted) {
          const retry = response.status >= 500 || response.status === 429;
          setData(null);
          setLoadError({ message: retry ? requestLoadFailed : requestUnavailable, retry });
          setLoading(false);
        }
        return;
      }
      const next = await requestView(response, id);
      if (controller.signal.aborted) return;
      setData(next);
      setGroups({});
      setNeedsCheck(false);
      setLoading(false);
    }).catch(() => {
      if (controller.signal.aborted) return;
      setData(null);
      setLoadError({ message: requestLoadFailed, retry: true });
      setLoading(false);
    });
    return () => controller.abort();
  }, [endpoint, id, readRevision]);

  function readLatest() {
    setError("");
    setLoadError(undefined);
    setLoading(true);
    setReadRevision(value => value + 1);
  }

  async function decide() {
    if (!data || !decision || !canDecide || writing.current || (decision === "approve" && !canApprove)) return;
    writing.current = true;
    setBusy(true);
    setError("");
    const accept = decision === "approve";
    const body = staff ? {
      accept, revision: data.revision, reason,
      placements: accept ? data.items.map(line => ({ lineId: line.id, groupId: line.groupId || groups[line.id] })) : null,
    } : { revision: data.revision };
    try {
      const response = await csrfRequest(`${endpoint}/${staff ? "review" : "cancel"}`, "POST", body, {}, { announce: false });
      const next = await requestView(response, id);
      if (next.status !== decisionStatus[decision]) throw new Error();
      setData(next);
    } catch (caught) {
      if (caught instanceof DashboardRequestError && [401, 403, 404].includes(caught.status)) {
        setData(null);
        setLoadError({ message: requestUnavailable, retry: false });
      } else {
        const uncertain = !(caught instanceof DashboardRequestError) || caught.status >= 500;
        const check = uncertain || caught.status === 409;
        setNeedsCheck(check);
        setError(uncertain ? "تعذر تأكيد نتيجة القرار. تحقّق من حالة الطلب." : caught.message);
      }
    } finally {
      writing.current = false;
      setBusy(false);
      setDecision(undefined);
    }
  }

  const content = <section className={styles.page}>
    {staff
      ? <PageHeader title="مراجعة تجديد الأسرة" context="طلبات الدفع" back={queueBack ?? { label: "العودة لطلبات الدفع", href: paymentQueue }} />
      : <><Link href="/guardian/family-renewals">العودة للطلبات</Link><header><h1>طلب تجديد الأسرة</h1>{data && <p role="status">{labels[data.status]}</p>}</header></>}
    {staff && data && <p className="status-pill" role="status">{labels[data.status]}</p>}
    {loading && <p role="status">{data ? "جارٍ التحقق من حالة الطلب…" : "جارٍ التحميل…"}</p>}
    {loadError && <section className={styles.card}>
      <p className="error-message" role="alert">{loadError.message}</p>
      {loadError.retry && <button className="secondary-button" type="button" onClick={readLatest}>إعادة تحميل الطلب</button>}
    </section>}
    {data && <>
      <section className={styles.summary} aria-label="تفاصيل تجديد الأسرة">
        <div className={styles.totals}>{[["قبل الخصم", data.originalAmount], ["الخصم", data.discountAmount], ["الإجمالي", data.totalAmount]].map(([name, value]) =>
          <div key={name}><small>{name}</small><strong>{formatMoneyAr(value, data.currency)}</strong></div>)}</div>
        {data.proofUrl && <a className={styles.receiptLink} href={data.proofUrl} target="_blank" rel="noreferrer">فتح إثبات التحويل الواحد</a>}
        {data.items.map(line => {
          const assigned = line.groups.find(group => group.id === line.groupId);
          return <article className={styles.card} key={line.id}>
            <strong>{line.player}</strong>
            <small>{line.sport} · {line.branch} · {line.plan}</small>
            <strong>{formatMoneyAr(line.finalAmount, line.currency)}</strong>
            {line.discountReason && <small>{line.discountReason}</small>}
            {staff && data.canReview && data.status === "Pending" && <>
              {line.groupId
                ? <small>{assigned ? <>المجموعة الحالية · <strong>{assigned.name}</strong></> : "راجع أهلية المجموعة الحالية في ملف اللاعب قبل الاعتماد."}</small>
                : line.groups.length > 0
                  ? <label className={styles.plan}>مجموعة {line.player}<select disabled={busy || !canDecide} value={groups[line.id] ?? ""} onChange={event => {
                    setGroups(old => ({ ...old, [line.id]: event.target.value }));
                    setError("");
                  }}><option value="">اختر المجموعة</option>{line.groups.map(group => <option value={group.id} key={group.id}>{group.name}</option>)}</select></label>
                  : <small>لا توجد مجموعة مناسبة لهذا التسجيل.</small>}
            </>}
            {line.receiptId && <Link className={styles.receiptLink} href={`${staff ? "/dashboard/subscriptions" : "/guardian"}/receipts/${line.receiptId}`}>إيصال {line.player}</Link>}
          </article>;
        })}
      </section>
      {data.decisionReason && <p className={styles.notice}>{data.decisionReason}</p>}
      {error && <p className="error-message" role="alert">{error}</p>}
      {needsCheck && !loading && <button type="button" className="secondary-button" onClick={readLatest}>التحقق من حالة الطلب</button>}
      {data.status === "Pending" && (staff
        ? !data.canReview ? <p>هذا الطلب للقراءة فقط. المراجعة تتطلب صلاحية تعديل طلبات الدفع.</p>
          : canDecide && <section className={styles.card}>
            <h2>قرار المراجعة</h2>
            <p>{data.proofUrl ? "طابق مبلغ التحويل مع إجمالي الطلب قبل الاعتماد." : "بانتظار إثبات التحويل قبل الاعتماد."}</p>
            <label>ملاحظة أو سبب الرفض (اختياري)<textarea maxLength={500} rows={2} disabled={busy} value={reason} onChange={event => setReason(event.target.value)} />
              <small>تظهر الملاحظة لولي الأمر.</small></label>
            <div className={styles.reviewActions}>
              <button type="button" className="primary-button" disabled={busy || !canApprove} onClick={() => setDecision("approve")}>قبول وتجديد كل التسجيلات</button>
              <button type="button" className="danger-button" disabled={busy} onClick={() => setDecision("reject")}>رفض الطلب كاملًا</button>
            </div>
          </section>
        : canDecide && <>
          {!data.proofUrl ? <FamilyTransferProof id={id} revision={data.revision} onSaved={next => {
            setData(current => current?.status === "Pending" && next.revision >= current.revision ? next : current);
          }} /> : <aside className={styles.notice}>وصل إثبات الدفع. الطلب بانتظار اعتماد الإدارة.</aside>}
          <button className="secondary-button" type="button" disabled={busy} onClick={() => setDecision("cancel")}>إلغاء طلب التجديد</button>
        </>)}
      {decision && <ConfirmationDialog open busy={busy} tone={decision === "approve" ? "primary" : "danger"}
        title={decision === "approve" ? "تأكيد اعتماد تجديد الأسرة" : decision === "reject" ? "تأكيد رفض طلب الأسرة" : "إلغاء طلب تجديد الأسرة؟"}
        message={decision === "approve"
          ? `سيُعتمد تجديد ${registrationCount(data.lineCount)} بإجمالي ${formatMoneyAr(data.totalAmount, data.currency)}.`
          : decision === "reject"
            ? reason.trim() ? `سيُرفض الطلب كاملًا. سيظهر لولي الأمر سبب الرفض «${reason.trim()}».` : "سيُرفض الطلب كاملًا، وسيظهر القرار لولي الأمر."
            : "إلغاء الطلب لا يسترد أي تحويل أرسلته. تواصل مع الإدارة إن كنت دفعت بالفعل."}
        confirmLabel={decision === "approve" ? "تأكيد الاعتماد" : decision === "reject" ? "تأكيد رفض الطلب" : "تأكيد إلغاء الطلب"}
        cancelLabel={decision === "cancel" ? "الاحتفاظ بالطلب" : undefined}
        onConfirm={() => void decide()} onCancel={() => setDecision(undefined)} />}
    </>}
  </section>;
  return staff ? content : <GuardianShell>{content}</GuardianShell>;
}

export function FamilyTransferProof({ id, revision, onSaved }: { id: string; revision: number; onSaved: (data: FamilyRequest) => void }) {
  const [settings, setSettings] = useState<{ configured: boolean; recipientName?: string; instaPayAddress?: string; walletNumber?: string } | null>(null), [file, setFile] = useState<File | null>(null), [busy, setBusy] = useState(false), [message, setMessage] = useState("");
  const [settingsError, setSettingsError] = useState(false), [settingsRevision, setSettingsRevision] = useState(0);
  const [asset, setAsset] = useState<string | null>(null); const input = useRef<HTMLInputElement>(null);
  useEffect(() => {
    const controller = new AbortController();
    void fetch("/api/v1/payment-settings", { cache: "no-store", signal: controller.signal }).then(async response => {
      if (!response.ok) throw new Error();
      const value = await response.json();
      if (typeof value?.configured !== "boolean") throw new Error();
      if (!controller.signal.aborted) setSettings(value);
    }).catch(() => {
      if (!controller.signal.aborted) setSettingsError(true);
    });
    return () => controller.abort();
  }, [settingsRevision]);
  async function copyAccount(value: string) {
    try {
      await navigator.clipboard.writeText(value);
      setMessage("تم نسخ حساب التحويل.");
    } catch {
      setMessage("تعذر النسخ؛ يمكنك تحديد الحساب ونسخه يدويًا.");
    }
  }
  async function submit() {
    if (busy || !file) return; setBusy(true); setMessage("");
    try {
      const assetId = asset ?? (await uploadAsset(file, "Proof")).id;
      setAsset(assetId);
      const response = await csrfRequest(`${guardianRoot}/${id}/proof`, "POST", { assetId, revision }, {}, { announce: false });
      const saved = await requestView(response, id);
      if (!saved.proofUrl) throw new Error();
      onSaved(saved);
    }
    catch (e) { setMessage(failure(e)); } finally { setBusy(false); }
  }
  return <section className={styles.card}>
    <h2>تحويل واحد للأسرة</h2>
    {settingsError ? <>
      <p role="alert">تعذر تحميل حساب التحويل. تأكد من الحساب قبل الدفع.</p>
      <button className="secondary-button" type="button" onClick={() => {
        setSettingsError(false);
        setSettingsRevision(value => value + 1);
      }}>إعادة تحميل حساب التحويل</button>
    </> : !settings && <p role="status">جارٍ تحميل حساب التحويل…</p>}
    {settings && !settings.configured && <p>لم تضبط الأكاديمية حساب التحويل. تواصل مع الإدارة قبل الدفع.</p>}
    {settings?.configured && <>
      <p>حوّل الإجمالي الموضح وارفع صورة الإيصال للمراجعة.</p>
      {settings.recipientName && <strong>{settings.recipientName}</strong>}
      {[["إنستا باي", settings.instaPayAddress], ["المحفظة", settings.walletNumber]].map(([name, value]) => value && <div key={name}>
        <small>{name}</small><p dir="ltr">{value}</p>
        <button className="secondary-button" type="button" onClick={() => void copyAccount(value)}>نسخ {name}</button>
      </div>)}
      <FilePicker ref={input} aria-label="صورة إثبات الأسرة" accept="image/png,image/jpeg,image/webp" hidden onChange={event => {
        setFile(event.target.files?.[0] ?? null);
        setMessage("");
      }} />
      <button className="secondary-button" type="button" disabled={busy || !!asset} onClick={() => input.current?.click()}>اختيار صورة إيصال التحويل</button>
      {file && <small>{file.name}</small>}
      <button className="primary-button" type="button" disabled={busy || !file} onClick={() => void submit()}>
        {busy ? "جارٍ إرسال الإثبات…" : asset ? "إعادة تأكيد نفس الإثبات" : "إرسال إثبات الأسرة"}
      </button>
    </>}
    {message && <p role="status">{message}</p>}
  </section>;
}
