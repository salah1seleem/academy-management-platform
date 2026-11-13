"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { ErrorState, LoadingState } from "../../../../components/dashboard-shell";
import { FilePicker } from "../../../../components/file-picker";
import { csrfRequest, DashboardRequestError, uploadAsset } from "../../../../features/dashboard-api";
import { guardianCheckoutPath, guardianRenewalRoot, saveGuardianPaymentRequest } from "../../../../features/guardian-renewal";
import { formatMoneyAr, statusLabel } from "../../../../lib/formatters";
import { clearFinancialIntent } from "../../../../lib/pending-financial-intent";

type Payment = {
  id: string;
  provider: string;
  simulationAvailable?: boolean;
  providerReference: string;
  amount: number;
  currency: string;
  status: string;
  player: string;
  sport: string;
  plan: string;
  originalAmount: number;
  discountType?: "Percentage" | "FixedAmount";
  discountValue?: number;
  discountAmount: number;
  currentPaymentId?: string;
  isCurrent: boolean;
  proofStatus?: string | null;
  receiptId?: string | null;
};
type TransferSettings = { configured: boolean; recipientName?: string; instaPayAddress?: string; walletNumber?: string };
type PaymentRead = { payment: Payment } | { error: string; retryable: boolean };

export default function Page() {
  const id = String(useParams().paymentId);
  return <Checkout key={id} id={id} />;
}

function Checkout({ id }: { id: string }) {
  const router = useRouter();
  const path = `${guardianRenewalRoot}/payments/${encodeURIComponent(id)}`;
  const retryScope = `guardian-payment-retry:${id}`;
  const mounted = useRef(false);
  const locked = useRef(false);
  const [result, setResult] = useState<PaymentRead | null>(null);
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [needsCheck, setNeedsCheck] = useState(false);
  const [retryUncertain, setRetryUncertain] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void fetch(path, { cache: "no-store", signal: controller.signal }).then(async response => {
      if (controller.signal.aborted) return;
      if (!response.ok) {
        const denied = [401, 403, 404].includes(response.status);
        setResult({
          error: response.status === 401 ? "انتهت الجلسة. سجّل الدخول مجددًا."
            : denied ? "طلب الدفع غير متاح لهذا الحساب." : "تعذر تحميل حالة الدفع.",
          retryable: !denied,
        });
        return;
      }
      const data = await response.json() as Payment | null;
      if (!data || data.id !== id || !Number.isFinite(data.amount) || !data.currency || typeof data.status !== "string") throw new Error();
      if (!controller.signal.aborted) {
        setResult({ payment: data });
        setNeedsCheck(false);
      }
    }).catch(() => {
      if (!controller.signal.aborted) setResult({ error: "تعذر تحميل حالة الدفع.", retryable: true });
    });
    return () => controller.abort();
  }, [id, path, revision]);

  function refresh() {
    setError("");
    setResult(null);
    setRevision(value => value + 1);
  }

  async function perform(operation: "simulate" | "retry" | "proof", send: () => Promise<string | void>) {
    if (locked.current || needsCheck) return;
    locked.current = true;
    setBusy(true);
    setError("");
    let navigating = false;
    try {
      const replacementId = await send();
      if (!mounted.current) return;
      if (replacementId) {
        router.push(guardianCheckoutPath(replacementId));
        clearFinancialIntent(retryScope);
        navigating = true;
      } else {
        refresh();
      }
    } catch (caught) {
      if (!mounted.current) return;
      const knownRejection = caught instanceof DashboardRequestError &&
        caught.status >= 400 && caught.status < 500 && caught.status !== 408;
      const ambiguousRetry = operation === "retry" && !knownRejection;
      const correctableProof = operation === "proof" && knownRejection && [400, 413, 415, 422].includes(caught.status);
      setRetryUncertain(ambiguousRetry);
      setNeedsCheck(!ambiguousRetry && !correctableProof);
      setError(knownRejection ? caught.message : ambiguousRetry
        ? "لم يصل تأكيد إنشاء محاولة الدفع. أعد إرسال نفس الطلب."
        : "لم تتأكد نتيجة الإجراء. تحقّق من حالة الدفع قبل المحاولة مجددًا.");
    } finally {
      if (!navigating) {
        locked.current = false;
        if (mounted.current) setBusy(false);
      }
    }
  }

  const payment = result && "payment" in result ? result.payment : null;
  const pending = payment?.isCurrent === true && payment.status === "Pending";
  const awaitingReview = payment?.status === "Pending" && payment.proofStatus === "Pending";
  const simulation = payment?.provider === "InternalTest" && payment.simulationAvailable === true;
  const manualTransfer = pending && !awaitingReview && payment?.provider === "Manual";
  const rejectedProof = payment?.proofStatus === "Rejected";
  const canRetry = payment?.isCurrent === true && !rejectedProof && ["Failed", "Cancelled", "Expired"].includes(payment.status);
  const status = awaitingReview ? "الإثبات قيد المراجعة"
    : payment?.status === "Confirmed" ? "تم الدفع"
    : payment?.isCurrent === false ? "استُبدل طلب الدفع"
    : payment?.status === "Pending" ? "بانتظار الدفع"
    : statusLabel(payment?.status);

  function simulate(outcome: "Success" | "Failed" | "Cancelled") {
    if (!pending || awaitingReview || !simulation) return;
    void perform("simulate", async () => {
      await csrfRequest(`${path}/simulate`, "POST", { outcome }, {}, { announce: false });
    });
  }

  function retry() {
    if (!canRetry) return;
    void perform("retry", () => saveGuardianPaymentRequest(`${path}/retry`, retryScope, {}));
  }

  function sendProof(file: File) {
    if (!manualTransfer) return;
    void perform("proof", async () => {
      const { id: assetId } = await uploadAsset(file, "Proof");
      if (!mounted.current) return;
      if (!assetId) throw new Error();
      await csrfRequest(`/api/v1/guardian/payments/${encodeURIComponent(id)}/proof`, "POST", { assetId }, {}, { announce: false });
    });
  }

  return <main className="center-shell guardian-renewal-shell">
    <section className="authenticated-card guardian-renewal-card" aria-labelledby="checkout-title">
      <Link className="back-link" href="/guardian">العودة إلى الرئيسية</Link>
      <h1 id="checkout-title">طلب دفع التجديد</h1>
      {!result ? <LoadingState message="جارٍ تحميل حالة الدفع…" />
        : "error" in result ? <>
          <ErrorState message={result.error} />
          {result.retryable && <button className="secondary-button" onClick={refresh}>إعادة تحميل حالة الدفع</button>}
        </> : payment && <>
          <div className="guardian-checkout-total">
            <div><span>{payment.discountAmount > 0 ? "الإجمالي بعد الخصم" : "إجمالي الطلب"}</span><strong>{formatMoneyAr(payment.amount, payment.currency)}</strong></div>
            <span className={`status-pill ${payment.status.toLowerCase()}`}>{status}</span>
          </div>
          <div className="guardian-renewal-context">
            <strong>{payment.player}</strong>
            <span>{payment.sport} · {payment.plan}</span>
          </div>
          {payment.discountAmount > 0 && <dl className="guardian-checkout-details">
              <div><dt>السعر الأصلي</dt><dd>{formatMoneyAr(payment.originalAmount, payment.currency)}</dd></div>
              <div><dt>الخصم</dt><dd>{payment.discountType === "Percentage" && payment.discountValue != null
                ? `${payment.discountValue}% (-${formatMoneyAr(payment.discountAmount, payment.currency)})`
                : formatMoneyAr(payment.discountAmount, payment.currency)}</dd></div>
          </dl>}
          {payment.providerReference && <details className="guardian-checkout-reference">
            <summary>مرجع طلب الدفع</summary>
            <bdi dir="ltr">{payment.providerReference}</bdi>
          </details>}
          {error && <ErrorState message={error} />}
          {busy && <p role="status">جارٍ تنفيذ الإجراء…</p>}
          {needsCheck ? <button className="secondary-button" onClick={refresh}>تحقّق من حالة الدفع</button> : <>
            {payment.isCurrent === false && payment.currentPaymentId && payment.currentPaymentId !== payment.id &&
              <Link className="primary-action" href={guardianCheckoutPath(payment.currentPaymentId)}>فتح طلب الدفع الحالي</Link>}
            {awaitingReview && <p className="notice">وصل إثبات الدفع إلى الأكاديمية. انتظر المراجعة قبل إرسال مبلغ آخر.</p>}
            {rejectedProof && <p className="notice">لم تعتمد الأكاديمية إثبات الدفع. راجعها بشأن التحويل السابق قبل إرسال مبلغ جديد.</p>}
            {pending && !awaitingReview && simulation && <section className="guardian-checkout-actions" aria-labelledby="checkout-demo-title">
              <h2 id="checkout-demo-title">محاكاة الدفع</h2>
              <p className="demo-note">بيئة دفع تجريبية — لا توجد أموال حقيقية</p>
              <button className="primary-button" disabled={busy} onClick={() => simulate("Success")}>محاكاة نجاح الدفع</button>
              <button className="secondary-button" disabled={busy} onClick={() => simulate("Failed")}>محاكاة فشل الدفع</button>
              <button className="secondary-button" disabled={busy} onClick={() => simulate("Cancelled")}>محاكاة إلغاء الدفع</button>
            </section>}
            {manualTransfer && <TransferProof busy={busy} onSubmit={sendProof} />}
            {pending && !awaitingReview && !simulation && !manualTransfer && <p>طريقة الدفع غير متاحة لهذا الطلب. تواصل مع الأكاديمية.</p>}
            {payment.status === "Confirmed" && <p className="success-message">تم الدفع وتجديد الاشتراك بنجاح</p>}
            {payment.status === "Failed" && <p>فشلت عملية الدفع ولم يتم تجديد الاشتراك.</p>}
            {payment.status === "Cancelled" && !rejectedProof && <p>أُلغي طلب الدفع. لم يتجدد الاشتراك.</p>}
            {canRetry && <button className="primary-button" disabled={busy} onClick={retry}>
              {retryUncertain ? "إعادة إرسال نفس الطلب" : "إعادة محاولة الدفع"}
            </button>}
            {payment.receiptId && <Link className="primary-action" href={`/guardian/receipts/${encodeURIComponent(payment.receiptId)}`}>عرض الإيصال</Link>}
            {(awaitingReview || (payment.status === "Confirmed" && !payment.receiptId)) &&
              <button className="secondary-button" disabled={busy} onClick={refresh}>تحديث حالة الدفع</button>}
          </>}
        </>}
    </section>
  </main>;
}

function TransferProof({ busy, onSubmit }: { busy: boolean; onSubmit: (file: File) => void }) {
  const [settings, setSettings] = useState<TransferSettings | null>(null);
  const [failed, setFailed] = useState(false);
  const [revision, setRevision] = useState(0);
  const [file, setFile] = useState<File | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    void fetch("/api/v1/payment-settings", { cache: "no-store", signal: controller.signal }).then(async response => {
      if (!response.ok) throw new Error();
      const data = await response.json() as TransferSettings | null;
      if (!data) throw new Error();
      if (!controller.signal.aborted) setSettings(data);
    }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [revision]);
  const accounts = [["إنستا باي", settings?.instaPayAddress], ["المحفظة", settings?.walletNumber]].filter(([, value]) => Boolean(value));
  return <section className="guardian-checkout-actions" aria-labelledby="checkout-transfer-title">
    <h2 id="checkout-transfer-title">التحويل إلى الأكاديمية</h2>
    {failed ? <>
      <ErrorState message="تعذر تحميل بيانات التحويل. تحقّق من الأكاديمية قبل الدفع." />
      <button className="secondary-button" onClick={() => { setFailed(false); setRevision(value => value + 1); }}>إعادة تحميل بيانات التحويل</button>
    </> : !settings ? <LoadingState message="جارٍ تحميل بيانات التحويل…" />
      : !settings.configured || accounts.length === 0 ? <p>لم تضف الأكاديمية حسابًا للتحويل. تواصل معها قبل الدفع.</p>
      : <>
        <p>حوّل الإجمالي إلى أحد الحسابات أدناه ثم أرفق الإيصال. إذا كنت حوّلت بالفعل، أرفق الإيصال فقط. تراجع الأكاديمية الإثبات قبل تأكيد الدفع.</p>
        <dl className="guardian-checkout-details">
          {settings.recipientName && <div><dt>المستفيد</dt><dd>{settings.recipientName}</dd></div>}
          {accounts.map(([name, value]) => <div key={name}><dt>{name}</dt><dd><bdi dir="ltr">{value}</bdi></dd></div>)}
        </dl>
        <form onSubmit={event => { event.preventDefault(); if (file && !busy) onSubmit(file); }}>
          <label className="field"><span id="transfer-proof-label">صورة إيصال التحويل</span>
            <FilePicker aria-labelledby="transfer-proof-label" accept="image/png,image/jpeg,image/webp" onChange={event => setFile(event.currentTarget.files?.[0] ?? null)} disabled={busy} />
          </label>
          <button type="submit" className="primary-button" disabled={!file || busy}>إرسال إثبات التحويل</button>
        </form>
      </>}
  </section>;
}
