"use client";
import { useParams } from "next/navigation";
import { CashReceiptVoid } from "../../../../../../features/cash-receipt-void";
export default function Page() { return <CashReceiptVoid receiptId={String(useParams().receiptId)} />; }
