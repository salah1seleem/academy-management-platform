"use client";
import { useParams, useSearchParams } from "next/navigation";
import { CashReceiptVoid } from "../../../../../../features/cash-receipt-void";
import { receiptCollectionScope } from "../../../../../../lib/subscription-collection-navigation";
import { receiptPeriodScope } from "../../../../../../lib/subscription-period-navigation";
export default function Page() {
  const receiptId = String(useParams().receiptId);
  const search = useSearchParams();
  const returnQuery = receiptCollectionScope(search)?.query ?? receiptPeriodScope(search)?.query.toString();
  return <CashReceiptVoid receiptId={receiptId} returnQuery={returnQuery} />;
}
