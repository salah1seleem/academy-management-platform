"use client";

import Link from "next/link";
import { type FormEvent, useEffect, useRef, useState } from "react";
import { AdminDataTable, ConfirmationDialog, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { formatDateAr, formatDateTimeAr, formatMoneyAr } from "../lib/formatters";
import { clearFinancialIntent, matchesFinancialIntent, pendingFinancialIntent, readFinancialIntentCashSession, readFinancialIntentKey } from "../lib/pending-financial-intent";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./external-agreement-detail.module.css";

const pagePath = "/dashboard/finance/external-agreements";
const apiPath = "/api/v1/finance/external-agreements";
const receiptTitle = "إيصال تحصيل اتفاق خارجي";
const readError = "تعذر تحميل الاتفاق. أعد المحاولة.";

export const externalAgreementKind = (kind: string) => kind === "PoolRental" ? "تأجير مسبح / حارة" : "برنامج خارجي";

type Agreement = {
  id: string; version: number; title: string; counterpartyName: string; kind: string;
  fromDate: string; toDate: string; dueOn: string; amount: number; currency: string;
  cancelledAtUtc?: string | null; note?: string | null;
};
type Receipt = {
  id: string; receiptNumber: string; amount: number; currency: string; paymentMethod: string;
  paymentReference?: string | null; receivedAtUtc: string; matchesAttempt?: boolean;
};
type Booking = { id: string; date: string; startTime: string; endTime: string; resource: string; reservedSeats: number; state: string };
type Detail = {
  agreement: Agreement; membershipId: string; academyName?: string; paid: number; remaining: number;
  canManage: boolean; receipts: Receipt[]; bookings: Booking[];
  cash: { cashEnabled: boolean; sessions: { id: string; name: string }[] };
};
type Payment = { amount: number; paymentMethod: string; paymentReference: string | null; cashSessionId: string | null; confirmedReceived: boolean };
type Attempt = { key: string; scope: string; cashSessionId?: string; body?: Payment; receiptId?: string };

const methodLabel = (method: string) => method === "Cash" ? "نقدي" : "تحويل مؤكّد يدويًا";
const money = (amount: number, currency: string) => formatMoneyAr(amount, currency, true);
const receiptHref = (id: string, receiptId: string) => `${pagePath}/${id}/receipts/${receiptId}`;

async function readDetail(path: string, signal: AbortSignal, key?: string): Promise<Detail> {
  const response = await fetch(path, { cache: "no-store", signal, ...(key && { headers: { "Idempotency-Key": key } }) });
  if ([401, 403, 404].includes(response.status)) throw new DashboardRequestError("الاتفاق غير متاح لهذا الحساب.", response.status);
  if (!response.ok) throw Error(readError);
  const data: Detail = await response.json();
  if (!data.agreement || !data.membershipId || !Array.isArray(data.receipts) || !Array.isArray(data.bookings) || !Array.isArray(data.cash?.sessions)) throw Error(readError);
  return data;
}

function attemptScope(data: Detail) {
  return `external-agreement-receipt:${data.membershipId}:${data.agreement.id}`;
}

function matchingReceipt(data: Detail, attempt: Attempt) {
  if (attemptScope(data) !== attempt.scope) throw new DashboardRequestError("تغيّر الحساب. افتح الاتفاق مرة أخرى.", 403);
  const receipt = data.receipts.find(row => row.matchesAttempt);
  if (!receipt) {
    if (attempt.receiptId) throw Error("لم يظهر الإيصال في السجل بعد.");
    return undefined;
  }
  if (!receipt.id || !receipt.receiptNumber || receipt.currency !== data.agreement.currency || !Number.isFinite(receipt.amount) || receipt.amount <= 0 ||
      (attempt.receiptId && receipt.id !== attempt.receiptId) || (attempt.body && (
        receipt.amount !== attempt.body.amount || receipt.paymentMethod !== attempt.body.paymentMethod ||
        (receipt.paymentReference || null) !== attempt.body.paymentReference))) throw Error("بيانات الإيصال قيد التحقق.");
  return receipt;
}

export function ExternalAgreementDetail({ id, receiptId }: { id: string; receiptId?: string }) {
  return <AgreementDetail key={`${id}:${receiptId ?? "detail"}`} id={id} receiptId={receiptId} />;
}

function AgreementDetail({ id, receiptId }: { id: string; receiptId?: string }) {
  const path = `${apiPath}/${id}`;
  const [data, setData] = useState<Detail>();
  const [error, setError] = useState("");
  const [canReload, setCanReload] = useState(false);
  const [revision, setRevision] = useState(0);
  const [message, setMessage] = useState("");
  const [formVersion, setFormVersion] = useState(0);
  const [method, setMethod] = useState("Online");
  const [busy, setBusy] = useState(false);
  const [cancel, setCancel] = useState(false);
  const [attempt, setAttempt] = useState<Attempt>();
  const [missing, setMissing] = useState(false);
  const [saved, setSaved] = useState<Receipt>();
  const writing = useRef(false);
  const outcome = useRef<HTMLElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    const signal = AbortSignal.any([controller.signal, AbortSignal.timeout(12000)]);
    async function load() {
      const details = await readDetail(path, signal);
      if (controller.signal.aborted) return;
      setData(details);
      if (receiptId) return;
      const scope = attemptScope(details);
      const key = readFinancialIntentKey(scope);
      if (!key) return;
      const cashSessionId = readFinancialIntentCashSession(scope, key);
      const pending = { key, scope, cashSessionId };
      if (cashSessionId) setMethod("Cash");
      setAttempt(pending);
      const current = await readDetail(path, signal, key);
      if (controller.signal.aborted) return;
      const found = matchingReceipt(current, pending);
      setData(current);
      if (found) {
        clearFinancialIntent(scope);
        setAttempt(undefined);
        setSaved(found);
      } else {
        setMissing(true);
      }
    }
    void load().catch(failure => {
      if (controller.signal.aborted) return;
      const unavailable = failure instanceof DashboardRequestError;
      setError(unavailable ? failure.message : readError);
      setCanReload(!unavailable);
      if (unavailable) setData(undefined);
    });
    return () => controller.abort();
  }, [path, receiptId, revision]);

  useEffect(() => { if (attempt || saved) outcome.current?.focus(); }, [attempt, saved]);

  function reload() {
    setError("");
    setCanReload(false);
    setRevision(value => value + 1);
  }

  async function verify(pending: Attempt) {
    const details = await readDetail(path, AbortSignal.timeout(12000), pending.key);
    const found = matchingReceipt(details, pending);
    setData(details);
    setCanReload(false);
    if (!found) {
      setMissing(true);
      return;
    }
    clearFinancialIntent(pending.scope);
    setAttempt(undefined);
    setMissing(false);
    setSaved(found);
    setFormVersion(value => value + 1);
  }

  function reportReadFailure(failure: unknown) {
    if (failure instanceof DashboardRequestError) {
      setData(undefined);
      setError(failure.message);
    } else {
      setError("تعذر تأكيد التحصيل من سجل الإيصالات. أعد التحقق.");
    }
  }

  async function check() {
    if (!attempt || writing.current) return;
    writing.current = true;
    setBusy(true);
    setMissing(false);
    setError("");
    try { await verify(attempt); }
    catch (failure) { reportReadFailure(failure); }
    finally { writing.current = false; setBusy(false); }
  }

  async function collect(body: Payment, retry?: Attempt) {
    if (!data?.canManage || writing.current || (attempt && !retry)) return;
    writing.current = true;
    setBusy(true);
    setError("");
    setSaved(undefined);
    let pending = retry;
    try {
      if (retry && !retry.body && !await matchesFinancialIntent(retry.scope, retry.key, body)) {
        setError("البيانات مختلفة عن التحصيل المعلّق. أدخل نفس المبلغ وطريقة الدفع والمرجع.");
        return;
      }
      const scope = attemptScope(data);
      const cashSessionId = body.cashSessionId ?? undefined;
      pending = { scope, key: retry?.key ?? await pendingFinancialIntent(scope, body, cashSessionId), body, cashSessionId };
      setAttempt(pending);
      setMissing(false);
      const response = await csrfRequest(`${path}/receipts`, "POST", { ...body, requestId: pending.key }, {}, {
        announce: false, signal: AbortSignal.timeout(12000),
      });
      const result: { id?: string } = await response.json();
      if (!result.id) throw Error();
      pending = { ...pending, receiptId: result.id };
      setAttempt(pending);
      await verify(pending);
    } catch (failure) {
      const rejected = !pending?.receiptId && failure instanceof DashboardRequestError &&
        failure.status >= 400 && failure.status < 500 && failure.status !== 408;
      if (rejected && (!retry || failure.status === 409 || failure.status === 422)) {
        if (pending) clearFinancialIntent(pending.scope);
        setAttempt(undefined);
        setError(failure.message);
        if ([401, 403, 404].includes(failure.status)) setData(undefined);
        if (failure.status === 409) {
          try { setData(await readDetail(path, AbortSignal.timeout(12000))); }
          catch (error) { reportReadFailure(error); setCanReload(true); }
        }
      } else if (pending) {
        setAttempt(pending);
        if (pending.receiptId || failure instanceof DashboardRequestError) reportReadFailure(failure);
      } else {
        setError("تعذر بدء تسجيل التحصيل. أعد المحاولة.");
      }
    } finally {
      writing.current = false;
      setBusy(false);
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const body = {
      amount: Number(form.get("amount")), paymentMethod: method,
      paymentReference: String(form.get("reference") ?? "").trim() || null,
      cashSessionId: method === "Cash" ? attempt?.cashSessionId ?? (String(form.get("session") ?? "") || null) : null,
      confirmedReceived: form.get("received") === "on",
    };
    if (!body.confirmedReceived || !Number.isFinite(body.amount) || body.amount <= 0) return;
    void collect(body, missing ? attempt : undefined);
  }

  async function cancelAgreement() {
    if (!data?.canManage || attempt || writing.current) return;
    writing.current = true;
    setBusy(true);
    setError("");
    try {
      await csrfRequest(`${path}/cancel`, "POST", { version: data.agreement.version }, {}, { announce: false, signal: AbortSignal.timeout(12000) });
      setMessage("تم إلغاء الاتفاق.");
      setData(await readDetail(path, AbortSignal.timeout(12000)));
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "تعذر تأكيد الإلغاء. أعد قراءة الاتفاق.");
      setCanReload(true);
    } finally {
      writing.current = false;
      setBusy(false);
      setCancel(false);
    }
  }

  const agreement = data?.agreement;
  const receipt = receiptId ? data?.receipts.find(row => row.id === receiptId) : undefined;
  const allowCollect = data?.canManage && data.remaining > 0 && !agreement?.cancelledAtUtc;
  const formVisible = !canReload && allowCollect && (!attempt || (missing && !attempt.body));
  const needsShift = method === "Cash" && data?.cash.cashEnabled && !attempt;
  const currency = agreement?.currency === "EGP" ? "جنيه" : agreement?.currency;

  return <>
    <PageHeader title={receiptId ? receiptTitle : agreement?.title ?? "تفاصيل الاتفاق"} context="الاتفاقيات والتأجير"
      back={receiptId ? { label: "العودة إلى الاتفاق", href: `${pagePath}/${id}` } : { label: "العودة إلى الاتفاقيات", href: pagePath }} />
    {error && <ErrorState message={error} />}
    {canReload && !attempt && <button type="button" className="secondary-button" onClick={reload}>إعادة تحميل الاتفاق</button>}
    {!data && !error && <LoadingState message="جارٍ تحميل الاتفاق…" />}
    {data && agreement && receiptId && (receipt ? <ExternalReceipt data={data} receipt={receipt} /> : <ErrorState message="الإيصال غير متاح لهذا الاتفاق." />)}
    {data && agreement && !receiptId && <>
      <section className="detail-card">
        <h2>{agreement.counterpartyName}</h2>
        <p>{externalAgreementKind(agreement.kind)} · {formatDateAr(agreement.fromDate)} — {formatDateAr(agreement.toDate)}</p>
        <dl className="detail-grid">
          <div><dt>قيمة الاتفاق</dt><dd>{money(agreement.amount, agreement.currency)}</dd></div>
          <div><dt>المحصّل</dt><dd>{money(data.paid, agreement.currency)}</dd></div>
          <div><dt>المتبقي</dt><dd>{money(data.remaining, agreement.currency)}</dd></div>
          <div><dt>الاستحقاق</dt><dd>{formatDateAr(agreement.dueOn)}</dd></div>
        </dl>
        {agreement.note && <p>{agreement.note}</p>}
        {agreement.cancelledAtUtc && <p>ملغى دون تحصيل · {formatDateTimeAr(agreement.cancelledAtUtc)}</p>}
        {data.canManage && !agreement.cancelledAtUtc && data.paid === 0 && !attempt && !canReload &&
          <button type="button" className="secondary-button" onClick={() => setCancel(true)} disabled={busy}>إلغاء الاتفاق</button>}
      </section>
      {message && <p role="status">{message}</p>}
      {attempt && <section className="detail-card" aria-label="التحقق من التحصيل" ref={outcome} tabIndex={-1}>
        <h2>التحقق من التحصيل</h2>
        <p>{missing ? "لم يظهر إيصال لهذه المحاولة حتى الآن." : "لم يتأكد ظهور الإيصال في السجل."}</p>
        <div className="form-actions">
          <button type="button" className="secondary-button" disabled={busy} onClick={() => void check()}>تحقّق من التحصيل</button>
          {missing && attempt.body && data.canManage && <button type="button" className="secondary-button" disabled={busy}
            onClick={() => void collect(attempt.body!, attempt)}>إعادة إرسال نفس التحصيل</button>}
        </div>
      </section>}
      {saved && <section className="detail-card" aria-label="التحصيل المسجّل" ref={outcome} tabIndex={-1}>
        <p role="status">تم تسجيل {money(saved.amount, saved.currency)} وإصدار الإيصال.</p>
        <Link className="secondary-button" href={receiptHref(id, saved.id)}>فتح الإيصال المسجّل</Link>
      </section>}
      {formVisible && <section className="detail-card">
        <h2>{attempt ? "أعد إدخال بيانات التحصيل نفسه" : "تسجيل التحصيل"}</h2>
        <form className="form-shell" key={formVersion} onSubmit={submit}>
          <label>المبلغ ({currency})<input type="number" name="amount" min="0.01" max={attempt ? agreement.amount : data.remaining} step="0.01" required disabled={busy} /></label>
          <label>طريقة الدفع<select value={method} disabled={busy || Boolean(attempt?.cashSessionId)} onChange={event => setMethod(event.target.value)}>
            <option value="Online">تحويل مؤكّد يدويًا</option><option value="Cash">نقدي</option>
          </select></label>
          <label>{method === "Online" ? "مرجع التحويل — اختياري" : "مرجع الدفع — اختياري"}<input name="reference" maxLength={100} disabled={busy} /></label>
          {attempt?.cashSessionId && <p>تُعاد المحاولة على الوردية الأصلية.</p>}
          {needsShift && <label>ورديتك المفتوحة<select name="session" required defaultValue="" disabled={busy}>
            <option value="">اختر الوردية</option>{data.cash.sessions.map(session => <option key={session.id} value={session.id}>{session.name}</option>)}
          </select>{!data.cash.sessions.length && <small>افتح وردية مخوّلة في فرع الاتفاق أولًا.</small>}</label>}
          <label className={styles.received}><input type="checkbox" name="received" required disabled={busy} /><span>أؤكد استلام المبلغ</span></label>
          <button type="submit" className="primary-button" disabled={busy || (needsShift && !data.cash.sessions.length)}>
            {busy ? "جارٍ تسجيل التحصيل…" : attempt ? "إعادة إرسال نفس التحصيل" : "تسجيل التحصيل وإصدار الإيصال"}
          </button>
        </form>
      </section>}
      <section className="list-card"><h2>إيصالات التحصيل</h2>
        {data.receipts.length === 0 ? <EmptyState message="لا توجد إيصالات لهذا الاتفاق." /> :
          <AdminDataTable label="تحصيل الاتفاق الخارجي" rows={data.receipts} rowKey={row => row.id} columns={[
            { key: "number", header: "الإيصال", primary: true, render: row => <Link href={receiptHref(id, row.id)} aria-label={`عرض الإيصال ${row.receiptNumber}`}><bdi className={styles.receiptNumber} dir="ltr">{row.receiptNumber}</bdi></Link> },
            { key: "date", header: "التاريخ", render: row => formatDateTimeAr(row.receivedAtUtc) },
            { key: "amount", header: "المبلغ", render: row => money(row.amount, row.currency) },
            { key: "method", header: "طريقة الدفع", render: row => methodLabel(row.paymentMethod) },
          ]} />}
      </section>
      {data.bookings.length > 0 && <section className="list-card"><h2>الحجوزات المرتبطة</h2>
        <AdminDataTable label="مواعيد تأجير الموارد" rows={data.bookings} rowKey={row => row.id} columns={[
          { key: "resource", header: "المورد", render: row => row.resource },
          { key: "date", header: "اليوم", render: row => formatDateAr(row.date) },
          { key: "time", header: "التوقيت", render: row => <bdi>{row.startTime.slice(0, 5)} — {row.endTime.slice(0, 5)}</bdi> },
          { key: "seats", header: "الأماكن", render: row => row.reservedSeats },
          { key: "state", header: "الحالة", render: row => row.state === "Cancelled" ? "ملغى" : "محجوز" },
        ]} />
      </section>}
      <ConfirmationDialog open={cancel} title="إلغاء الاتفاق؟" message="يحرر الإلغاء الحجوزات المرتبطة ويحفظ سجل الاتفاق. يتاح الإلغاء قبل التحصيل."
        confirmLabel="تأكيد الإلغاء" busy={busy} onConfirm={() => void cancelAgreement()} onCancel={() => setCancel(false)} />
    </>}
  </>;
}

function ExternalReceipt({ data, receipt }: { data: Detail; receipt: Receipt }) {
  return <>
    <article className="printable-receipt" aria-label={receiptTitle}>
      <p className="receipt-print-title">{receiptTitle}</p>
      <header>
        <div>{data.academyName && <span>{data.academyName}</span>}<h2>{data.agreement.counterpartyName}</h2></div>
        <div className="receipt-paid"><span>المبلغ المحصّل</span><strong>{money(receipt.amount, receipt.currency)}</strong></div>
      </header>
      <dl>
        <div className="receipt-code"><dt>رقم الإيصال</dt><dd><bdi className={styles.receiptNumber} dir="ltr">{receipt.receiptNumber}</bdi></dd></div>
        <div><dt>الاتفاق</dt><dd>{data.agreement.title}</dd></div>
        <div><dt>نوع الاتفاق</dt><dd>{externalAgreementKind(data.agreement.kind)}</dd></div>
        <div><dt>تاريخ التحصيل</dt><dd><time dateTime={receipt.receivedAtUtc}>{formatDateTimeAr(receipt.receivedAtUtc)}</time></dd></div>
        <div><dt>طريقة الدفع</dt><dd>{methodLabel(receipt.paymentMethod)}</dd></div>
        {receipt.paymentReference && <div className="receipt-code"><dt>مرجع الدفع</dt><dd><bdi>{receipt.paymentReference}</bdi></dd></div>}
      </dl>
    </article>
    <div className="receipt-actions"><button type="button" className="primary-button" onClick={() => window.print()}>طباعة الإيصال</button></div>
  </>;
}
