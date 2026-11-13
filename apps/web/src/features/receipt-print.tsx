"use client";

import Link from "next/link";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { formatDateAr, formatDateTimeAr, formatMoneyAr, formatReceiptTimestampAr } from "../lib/formatters";
import { receiptAmount, useReceiptRead } from "./receipt-read";

type Receipt = {
  id?: string; enrollmentBranch?: string; collectionBranch?: string; isVoided?: boolean;
  voidedAtUtc?: string; voidedAtAcademy?: string | null; canReviewVoid?: boolean; academyName?: string; receiptNumber: string;
  playerNameSnapshot?: string; currentPlayer?: string | null; sportNameSnapshot?: string; planNameSnapshot?: string;
  originalAmount: number; discountType?: "Percentage" | "FixedAmount"; discountValue?: number;
  discountAmount: number; finalAmount?: number; amount: number; currency: string;
  paidAtUtc: string; paidAtAcademy?: string | null; paymentMethod?: string; providerReference?: string;
  historicalPaidOn?: string | null;
  historicalPeriod?: { startDate: string; endDate: string | null } | null;
};

type Tournament = {
  player: string; tournamentName: string; amount: number; currency: string;
  receiptNumber?: string; confirmedAtUtc?: string; status?: string;
};

const methodLabels: Record<string, string> = {
  Cash: "نقدًا", Online: "الدفع الإلكتروني", InternalTest: "دفع تجريبي",
  ManualTransfer: "تحويل بنكي", BankTransfer: "تحويل بنكي",
};

export function ReceiptPrint({ endpoint, tournament = false, returnQuery }: { endpoint: string; tournament?: boolean; returnQuery?: string }) {
  const { current, retry } = useReceiptRead<Receipt | Tournament>(endpoint);
  if (!current) return <LoadingState message="جارٍ تحميل الإيصال…" />;
  if (current.error === "unavailable") return <ErrorState message="الإيصال غير متاح أو لا تملك صلاحية عرضه." />;
  if (current.error || !current.data) return <div className="receipt-read-error"><ErrorState message="تعذر تحميل الإيصال." />
    <button type="button" className="secondary-button" onClick={retry}>إعادة المحاولة</button></div>;
  const data = current.data;
  if (typeof data !== "object") return <ErrorState message="الإيصال غير متاح." />;
  let receipt: Receipt | undefined;
  if (tournament && "tournamentName" in data) {
    if (data.status === "Confirmed" && data.receiptNumber && data.confirmedAtUtc) {
      receipt = {
        academyName: "", receiptNumber: data.receiptNumber, playerNameSnapshot: data.player, sportNameSnapshot: "",
        planNameSnapshot: data.tournamentName, originalAmount: data.amount, discountAmount: 0,
        amount: data.amount, currency: data.currency, paidAtUtc: data.confirmedAtUtc,
      };
    }
  } else if (!tournament && !("tournamentName" in data) && data.receiptNumber) {
    receipt = data;
  }
  const paid = receiptAmount(receipt?.finalAmount ?? receipt?.amount);
  const paidOn = receipt?.historicalPaidOn ?? receipt?.paidAtUtc;
  if (!receipt || typeof receipt.receiptNumber !== "string" || !receipt.receiptNumber.trim() || paid === null ||
      typeof receipt.currency !== "string" || !/^[A-Z]{3}$/.test(receipt.currency) || typeof paidOn !== "string" || !Number.isFinite(Date.parse(paidOn))) {
    return <ErrorState message="الإيصال غير متاح." />;
  }

  const method = typeof receipt.paymentMethod === "string" ? receipt.paymentMethod.trim() : "";
  const cash = method.toLowerCase() === "cash";
  const methodLabel = cash ? methodLabels.Cash : method && (methodLabels[method] ?? (/\p{Script=Arabic}/u.test(method) ? method : "غير محددة في الإيصال"));
  const sharedBranch = receipt.enrollmentBranch && receipt.enrollmentBranch === receipt.collectionBranch;
  const discount = receiptAmount(receipt.discountAmount);
  const original = receiptAmount(receipt.originalAmount);
  const amount = formatMoneyAr(paid, receipt.currency, true);
  const linkedName = receipt.currentPlayer && receipt.currentPlayer !== receipt.playerNameSnapshot ? receipt.currentPlayer : null;
  const historical = receipt.historicalPaidOn;
  const paymentTime = historical || tournament ? null : formatReceiptTimestampAr(receipt.paidAtUtc, receipt.paidAtAcademy);
  if (!historical && !tournament && !paymentTime) return <ErrorState message="الإيصال غير متاح." />;
  const reversalTime = formatReceiptTimestampAr(receipt.voidedAtUtc, receipt.voidedAtAcademy);
  const period = receipt.historicalPeriod;
  const candidate = typeof receipt.providerReference === "string" ? receipt.providerReference.trim() : "";
  const reference = historical || cash || /^manual-[a-f0-9]{32}$/i.test(candidate) ? "" : candidate;

  return <>
    <article className="printable-receipt" aria-label={tournament ? "إيصال رسم البطولة" : "إيصال دفع"}>
      <p className="receipt-print-title">{tournament ? "إيصال رسم البطولة" : "إيصال دفع"}</p>
      {receipt.isVoided && <aside className="receipt-void-notice" role="status">
        <strong>إيصال معكوس — الفترة المرتبطة ملغاة</strong>
        <p>رُدت القيمة كاملة. {reversalTime && <>تاريخ العكس <time dateTime={receipt.voidedAtUtc}>{reversalTime.text}</time> {reversalTime.zone}</>}</p>
      </aside>}
      <header>
        <div>
          {receipt.academyName && <span>{receipt.academyName}</span>}
          {receipt.playerNameSnapshot && <h2>{receipt.playerNameSnapshot}</h2>}
          {historical && <span>تحصيل سابق</span>}
          {linkedName && <span className="receipt-linked-name">اللاعب المرتبط الآن: {linkedName}</span>}
        </div>
        <div className="receipt-paid"><span>المبلغ المدفوع</span><strong>{amount}</strong></div>
      </header>
      <dl>
        <div className="receipt-code"><dt>رقم الإيصال</dt><dd><bdi>{receipt.receiptNumber}</bdi></dd></div>
        <div><dt>{historical ? "تاريخ القبض" : "تاريخ ووقت الدفع"}</dt><dd>
          <time dateTime={historical ?? receipt.paidAtUtc}>{historical ? formatDateAr(historical) : paymentTime?.text ?? formatDateTimeAr(receipt.paidAtUtc)}</time>
          {paymentTime && <small className="receipt-time-zone">{paymentTime.zone}</small>}
        </dd></div>
        {period && <div><dt>فترة الاشتراك</dt><dd>من <time dateTime={period.startDate}>{formatDateAr(period.startDate)}</time>{period.endDate && <> إلى <time dateTime={period.endDate}>{formatDateAr(period.endDate)}</time></>}</dd></div>}
        {receipt.planNameSnapshot && <div><dt>{tournament ? "البطولة" : "الباقة"}</dt><dd>{receipt.planNameSnapshot}</dd></div>}
        {receipt.sportNameSnapshot && <div><dt>الرياضة</dt><dd>{receipt.sportNameSnapshot}</dd></div>}
        {sharedBranch ? <div><dt>فرع التسجيل والتحصيل</dt><dd>{receipt.enrollmentBranch}</dd></div> : <>
          {receipt.enrollmentBranch && <div><dt>فرع التسجيل</dt><dd>{receipt.enrollmentBranch}</dd></div>}
          {receipt.collectionBranch && <div><dt>فرع التحصيل</dt><dd>{receipt.collectionBranch}</dd></div>}
        </>}
        {discount !== null && discount > 0 && <>
          {original !== null && <div><dt>السعر الأصلي</dt><dd>{formatMoneyAr(original, receipt.currency, true)}</dd></div>}
          <div><dt>الخصم</dt><dd>{receipt.discountType === "Percentage" && receipt.discountValue != null ? `${receipt.discountValue}% · ` : ""}{formatMoneyAr(discount, receipt.currency, true)}</dd></div>
        </>}
        {methodLabel && <div><dt>طريقة الدفع</dt><dd>{methodLabel}</dd></div>}
        {reference && <div className="receipt-code"><dt>مرجع الدفع</dt><dd><bdi>{reference}</bdi></dd></div>}
      </dl>
    </article>
    <div className="receipt-actions">
      <button className="primary-button" type="button" onClick={() => window.print()}>طباعة الإيصال</button>
      {!tournament && receipt.canReviewVoid && receipt.id && <Link className="secondary-button" href={`/dashboard/subscriptions/receipts/${receipt.id}/void${returnQuery ? `?${returnQuery}` : ""}`}>مراجعة عكس الإيصال</Link>}
    </div>
  </>;
}
