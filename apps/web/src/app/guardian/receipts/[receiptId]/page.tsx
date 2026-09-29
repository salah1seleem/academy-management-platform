"use client";
import { useParams } from "next/navigation";
import { GuardianShell } from "../../../../components/guardian-shell";
import { ReceiptPrint } from "../../../../features/receipt-print";
export default function Page() { const id = String(useParams().receiptId); return <GuardianShell><header className="guardian-page-header"><span>إيصالات الدفع</span><h1>إيصال دفع</h1></header><ReceiptPrint endpoint={`/api/v1/guardian/subscriptions/receipts/${id}`} /></GuardianShell>; }
