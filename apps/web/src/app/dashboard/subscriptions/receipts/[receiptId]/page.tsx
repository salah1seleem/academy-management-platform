"use client";
import { useParams } from "next/navigation";
import { PageHeader } from "../../../../../components/dashboard-shell";
import { ReceiptPrint } from "../../../../../features/receipt-print";
export default function Page() { const id = String(useParams().receiptId); return <><PageHeader title="إيصال دفع" context="التقارير / الإيصالات" /><ReceiptPrint endpoint={`/api/v1/subscriptions/receipts/${id}`} /></>; }
