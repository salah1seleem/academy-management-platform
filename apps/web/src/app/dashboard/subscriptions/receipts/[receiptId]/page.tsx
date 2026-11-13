"use client";
import { useParams, useSearchParams } from "next/navigation";
import { PageHeader } from "../../../../../components/dashboard-shell";
import { ReceiptPrint } from "../../../../../features/receipt-print";
import { financialLedgerScope } from "../../../../../lib/financial-ledger-query";
import { enrollmentReceiptBack } from "../../../../../lib/payment-queue-navigation";
import { renewalReceiptBack } from "../../../../../lib/renewal-request-navigation";
import { receiptCollectionScope } from "../../../../../lib/subscription-collection-navigation";
import { receiptPeriodScope, periodScopeQuery } from "../../../../../lib/subscription-period-navigation";
export default function Page() {
  const id = encodeURIComponent(String(useParams().receiptId));
  const search = useSearchParams();
  const query = new URLSearchParams();
  for (const key of ["from", "to", "page", "pageSize"]) {
    const value = search.get(key);
    if (value) query.set(key, value);
  }
  const periodScope = receiptPeriodScope(search);
  const collections = receiptCollectionScope(search);
  const back = search.get("report") === "financial" ? { label: "العودة إلى سجل التحصيلات", href: `/dashboard/reports/financial?${financialLedgerScope(search)}` }
    : search.get("report") === "1" ? { label: "العودة إلى الإيصالات", href: `/dashboard/reports/receipts?${query}` }
    : collections ? { label: "العودة إلى تحصيلات الاشتراكات", href: collections.href }
    : periodScope ? { label: "العودة إلى الاشتراك", href: `/dashboard/subscriptions/periods/${periodScope.id}?${periodScopeQuery(periodScope.kind, periodScope.term)}` } : enrollmentReceiptBack(search) ?? renewalReceiptBack(search);
  return <><PageHeader title="إيصال دفع" context="التقارير / الإيصالات" back={back} />
    <ReceiptPrint endpoint={`/api/v1/subscriptions/receipts/${id}`} returnQuery={collections?.query ?? periodScope?.query.toString()} /></>;
}
