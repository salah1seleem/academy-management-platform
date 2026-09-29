"use client";

import { useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../components/dashboard-shell";

type Receipt = { academyName: string; receiptNumber: string; playerNameSnapshot: string; sportNameSnapshot: string; planNameSnapshot: string; originalAmount: number; discountType?: "Percentage" | "FixedAmount"; discountValue?: number; discountAmount: number; finalAmount: number; amount: number; currency: string; paidAtUtc: string; paymentMethod: string; providerReference: string };

export function ReceiptPrint({ endpoint }: { endpoint: string }) {
  const [receipt, setReceipt] = useState<Receipt | null>(null); const [error, setError] = useState("");
  useEffect(() => { fetch(endpoint).then(r => { if (!r.ok) throw new Error(); return r.json(); }).then(setReceipt).catch(() => setError("تعذر تحميل الإيصال أو لا تملك صلاحية عرضه.")); }, [endpoint]);
  if (error) return <ErrorState message={error} />; if (!receipt) return <LoadingState />;
  const currency = receipt.currency === "EGP" ? "جنيه" : receipt.currency;
  return <><article className="printable-receipt"><header><span>{receipt.academyName}</span><h1>إيصال دفع</h1><strong>{receipt.receiptNumber}</strong></header><dl><div><dt>اللاعب</dt><dd>{receipt.playerNameSnapshot}</dd></div><div><dt>الرياضة</dt><dd>{receipt.sportNameSnapshot}</dd></div><div><dt>الباقة</dt><dd>{receipt.planNameSnapshot}</dd></div>{receipt.discountAmount > 0 && <><div><dt>السعر الأصلي</dt><dd>{receipt.originalAmount} {currency}</dd></div><div><dt>الخصم</dt><dd>{receipt.discountType === "Percentage" ? `${receipt.discountValue}%` : `${receipt.discountValue} ${currency}`} (-{receipt.discountAmount} {currency})</dd></div></>}<div><dt>المبلغ المدفوع</dt><dd>{receipt.finalAmount ?? receipt.amount} {currency}</dd></div><div><dt>تاريخ ووقت الدفع</dt><dd>{new Date(receipt.paidAtUtc).toLocaleString("ar-EG")}</dd></div><div><dt>طريقة الدفع</dt><dd>{receipt.paymentMethod}</dd></div><div><dt>مرجع مزود الدفع</dt><dd dir="ltr">{receipt.providerReference}</dd></div></dl><footer>إيصال دفع صادر من نظام الأكاديمية</footer></article><button className="primary-button print-button" type="button" onClick={() => window.print()}>طباعة الإيصال</button></>;
}
