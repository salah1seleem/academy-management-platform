"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { type FormEvent, useEffect, useRef, useState } from "react";
import { ErrorState, LoadingState, PageHeader, StatusBadge } from "../components/dashboard-shell";
import { formatDateTimeAr, formatMoneyAr, statusLabel } from "../lib/formatters";
import { canonicalDigits } from "../lib/number-input";
import { clearFinancialIntent, pendingFinancialIntent, readFinancialIntentKey } from "../lib/pending-financial-intent";
import { renewalDetailBack, renewalPaymentHref } from "../lib/renewal-request-navigation";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { useReceiptRead } from "./receipt-read";
import styles from "./renewal-discount.module.css";

type DiscountType = "Percentage" | "FixedAmount";
type DiscountAudit = { id: string; newDiscountType: DiscountType | null; previousFinalAmount: number; newFinalAmount: number; reason: string; performedBy: string; createdAtUtc: string };
type RenewalDiscount = { id: string; player: string; sport: string; plan: string; originalAmount: number; discountType: DiscountType | null; discountValue: number | null; discountAmount: number; finalAmount: number; currency: string; status: string; paymentStatus: string | null; visiblePaymentId?: string | null; version: number; canModify: boolean; adjustments: DiscountAudit[] };
type Attempt = { key: string; missing?: boolean };

async function savedAdjustment(response: Response, id: string) {
  const result = await response.json();
  if (result?.renewalRequestId?.toLowerCase() !== id.toLowerCase() || typeof result.adjustmentId !== "string" || !result.adjustmentId) throw Error();
}

export function RenewalDiscountDetail({ id }: { id: string }) {
  return <RenewalDiscountEditor key={id} id={id} />;
}

function RenewalDiscountEditor({ id }: { id: string }) {
  const query = useSearchParams();
  const endpoint = `/api/v1/subscriptions/renewals/${encodeURIComponent(id)}`;
  const intentScope = `renewal-discount:${id}`;
  const { current, retry } = useReceiptRead<RenewalDiscount>(endpoint);
  const data = current?.data;
  const validRecord = data && typeof data.id === "string" && data.id.toLowerCase() === id.toLowerCase() &&
    Array.isArray(data.adjustments) && typeof data.currency === "string" &&
    [data.originalAmount, data.discountAmount, data.finalAmount].every(Number.isFinite);
  const currentPaymentId = validRecord && !current?.error && typeof data.visiblePaymentId === "string" && /^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(data.visiblePaymentId)
    ? data.visiblePaymentId : undefined;
  const [attempt, setAttempt] = useState<Attempt | null>();
  const [mode, setMode] = useState<"edit" | "remove" | null>(null);
  const [type, setType] = useState<DiscountType>("Percentage");
  const [value, setValue] = useState("");
  const [reason, setReason] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const sending = useRef(false);
  const form = useRef<HTMLFormElement>(null);

  useEffect(() => {
    let active = true;
    queueMicrotask(() => {
      if (!active) return;
      const key = readFinancialIntentKey(intentScope);
      setAttempt(key ? { key } : null);
    });
    return () => { active = false; };
  }, [intentScope]);

  useEffect(() => {
    if (mode) form.current?.querySelector<HTMLElement>("select, textarea")?.focus();
  }, [mode]);

  const amount = (amount: number) => formatMoneyAr(amount, data?.currency, true);
  const original = data?.originalAmount ?? 0;
  const cleaned = canonicalDigits(value).trim().replace("٫", ".");
  const numeric = /^\d+(?:\.\d{1,2})?$/.test(cleaned) ? Number(cleaned) : NaN;
  const max = type === "Percentage" ? 100 : original;
  const discount = type === "Percentage" ? Math.round(Math.round(original * 100) * Math.round(numeric * 100) / 10000) / 100 : numeric;
  const valueError = !Number.isFinite(numeric) || numeric <= 0 ? "أدخل قيمة موجبة بمنزلتين عشريتين على الأكثر." :
    numeric > max ? (type === "Percentage" ? "النسبة لا تتجاوز 100%." : "المبلغ لا يتجاوز السعر الأصلي.") :
    discount <= 0 ? "قيمة الخصم بعد التقريب أقل من قرش." : "";
  const reasonError = !reason.trim() ? "اكتب سبب التعديل." : "";

  function open(next: "edit" | "remove") {
    setMode(next); setType(data?.discountType ?? "Percentage"); setValue(String(data?.discountValue ?? ""));
    setReason(""); setSubmitted(false); setError(""); setNotice("");
  }

  function finish(message: string) {
    clearFinancialIntent(intentScope);
    setAttempt(null); setMode(null); setReason(""); setNotice(message); retry();
  }

  async function verify() {
    if (!attempt || sending.current) return;
    sending.current = true; setBusy(true); setError("");
    try {
      const response = await fetch(`${endpoint}/discount/attempt`, { cache: "no-store", headers: { "Idempotency-Key": attempt.key } });
      if (response.status === 404) { setAttempt({ key: attempt.key, missing: true }); return; }
      if (!response.ok) throw Error();
      await savedAdjustment(response, id);
      finish("تم العثور على تعديل الخصم المحفوظ.");
    } catch {
      setError("تعذر التحقق من نتيجة الحفظ. أعد التحقق.");
    } finally { sending.current = false; setBusy(false); }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!data?.canModify || !mode || attempt !== null || sending.current) return;
    setSubmitted(true);
    if (reasonError || mode === "edit" && valueError) return;
    sending.current = true; setBusy(true); setError("");
    const path = mode === "remove" ? "discount/remove" : "discount";
    const base = { reason: reason.trim(), expectedVersion: data.version };
    const body = mode === "remove" ? base : { ...base, type, value: numeric };
    let key: string | undefined;
    try {
      key = await pendingFinancialIntent(intentScope, { path, body });
      const response = await csrfRequest(`${endpoint}/${path}`, "POST", body, { "Idempotency-Key": key }, { announce: false });
      await savedAdjustment(response, id);
      finish(mode === "remove" ? "تمت إزالة الخصم." : "تم حفظ الخصم.");
    } catch (caught) {
      if (key && (!(caught instanceof DashboardRequestError) || [0, 408, 429].includes(caught.status) || caught.status >= 500)) {
        setAttempt({ key }); setMode(null);
      } else {
        clearFinancialIntent(intentScope);
        setError(caught instanceof DashboardRequestError ? caught.message : "تعذر بدء حفظ الخصم.");
        if (caught instanceof DashboardRequestError && [401, 403, 404, 409].includes(caught.status)) { setMode(null); retry(); }
      }
    } finally { sending.current = false; setBusy(false); }
  }

  return <>
    <PageHeader title="تفاصيل طلب التجديد" context="الأعضاء والاشتراكات" back={renewalDetailBack(query, attempt === null && !busy ? currentPaymentId : undefined)} />
    {error && <ErrorState message={error} />}
    {notice && <p role="status" className={styles.notice}>{notice}</p>}
    {!current || attempt === undefined ? <LoadingState message="جارٍ تحميل طلب التجديد…" /> : current.error === "unavailable" ?
      <ErrorState message="طلب التجديد غير متاح لحسابك." /> : !validRecord ? <>
        <ErrorState message="تعذر تحميل طلب التجديد." />
        <button type="button" className="secondary-button" onClick={retry}>إعادة المحاولة</button>
      </> : <section className={`detail-card ${styles.detail}`}>
        <header className={styles.identity}>
          <div><h2>{data.player}</h2><p>{data.sport} · {data.plan}</p></div>
          <div className={styles.statuses}>
            <span>التجديد <StatusBadge status={data.status} label={statusLabel(data.status)} /></span>
            {data.paymentStatus && <span>الدفع <StatusBadge status={data.paymentStatus} label={statusLabel(data.paymentStatus)} /></span>}
          </div>
        </header>
        <dl className={styles.amounts}>
          <div><dt>السعر الأصلي</dt><dd>{amount(data.originalAmount)}</dd></div>
          <div><dt>الخصم</dt><dd>{data.discountAmount ? <>{amount(data.discountAmount)}{data.discountType === "Percentage" && <small> · <bdi>{data.discountValue}%</bdi></small>}</> : "لا يوجد"}</dd></div>
          <div className={styles.total}><dt>{data.paymentStatus === "Confirmed" ? "المبلغ المدفوع" : "المبلغ المطلوب"}</dt><dd>{amount(data.finalAmount)}</dd></div>
        </dl>
        {currentPaymentId && attempt === null && !busy && query.get("fromPayment") !== "true" && <div className="form-actions">
          <Link className="button button-secondary" href={renewalPaymentHref(currentPaymentId, query)}>فتح طلب الدفع الحالي</Link>
        </div>}
        {attempt ? <div className={styles.editor}>
          <p role="status">{attempt.missing ? "لا يوجد تعديل محفوظ لهذه المحاولة. راجع المبلغ الحالي قبل الحفظ." : "نتيجة الحفظ غير مؤكدة. تحقّق منها قبل إجراء تعديل آخر."}</p>
          {attempt.missing ? <button type="button" className="secondary-button" onClick={() => finish("")}>فتح مراجعة جديدة</button> :
            <button type="button" className="secondary-button" disabled={busy} onClick={() => void verify()}>{busy ? "جارٍ التحقق…" : "التحقق من نتيجة الحفظ"}</button>}
        </div> : data.canModify && (mode ? <form ref={form} className={`adjustment-form ${styles.editor}`} noValidate onSubmit={event => void submit(event)}>
          <h2>{mode === "remove" ? "إزالة الخصم" : data.discountType ? "تعديل الخصم" : "تطبيق خصم"}</h2>
          {mode === "edit" && <div className={styles.fields}>
            <label>نوع الخصم<select disabled={busy} value={type} onChange={event => setType(event.target.value as DiscountType)}>
              <option value="Percentage">نسبة مئوية</option><option value="FixedAmount">مبلغ ثابت</option>
            </select></label>
            <label>{type === "Percentage" ? "النسبة المئوية" : "المبلغ"}<input aria-label="قيمة الخصم" inputMode="decimal" dir="ltr" required disabled={busy}
              aria-invalid={submitted && !!valueError} aria-describedby={submitted && valueError ? "renewal-discount-value-error" : undefined}
              value={value} onChange={event => setValue(event.target.value)} />
              {submitted && valueError && <small id="renewal-discount-value-error" className={styles.fieldError}>{valueError}</small>}</label>
          </div>}
          <label>سبب الخصم<textarea required maxLength={500} disabled={busy} aria-invalid={submitted && !!reasonError}
            aria-describedby={submitted && reasonError ? "renewal-discount-reason-error" : undefined}
            value={reason} onChange={event => setReason(event.target.value)} />
            {submitted && reasonError && <small id="renewal-discount-reason-error" className={styles.fieldError}>{reasonError}</small>}</label>
          {(mode === "remove" || !valueError) && <dl className={styles.preview}>
            <div><dt>قيمة الخصم بعد الحفظ</dt><dd>{amount(mode === "remove" ? 0 : discount)}</dd></div>
            <div><dt>المبلغ المتوقع</dt><dd>{amount(mode === "remove" ? original : Math.round((original - discount) * 100) / 100)}</dd></div>
          </dl>}
          <p className={styles.notice}>عند الحفظ سيُلغى طلب الدفع الحالي ويصدر طلب بالمبلغ الجديد.</p>
          <div className="form-actions">
            <button type="submit" className={mode === "remove" ? "danger-button" : "primary-button"} disabled={busy}>
              {busy ? "جارٍ الحفظ…" : mode === "remove" ? "تأكيد إزالة الخصم" : "حفظ الخصم"}</button>
            <button type="button" className="secondary-button" disabled={busy} onClick={() => { setMode(null); setError(""); }}>إلغاء</button>
          </div>
        </form> : <div className="form-actions">
          <button type="button" className="primary-button" onClick={() => open("edit")}>{data.discountType ? "تعديل الخصم" : "تطبيق خصم"}</button>
          {data.discountType && <button type="button" className="secondary-button" onClick={() => open("remove")}>إزالة الخصم</button>}
        </div>)}
        <section className={`adjustment-history ${styles.history}`} aria-label="سجل الخصم">
          <h2>سجل الخصم</h2>
          {data.adjustments.length === 0 ? <p>لا توجد تعديلات خصم.</p> : data.adjustments.map(item => <article key={item.id}>
            <div><b>{item.newDiscountType ? "حفظ الخصم" : "إزالة الخصم"}</b><small>{item.performedBy} · {formatDateTimeAr(item.createdAtUtc)}</small></div>
            <p>{item.reason}</p><small>من {amount(item.previousFinalAmount)} إلى {amount(item.newFinalAmount)}</small>
          </article>)}
        </section>
      </section>}
  </>;
}
