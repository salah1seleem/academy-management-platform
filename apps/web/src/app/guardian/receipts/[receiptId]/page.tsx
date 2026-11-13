import { ArrowRight } from "lucide-react";
import Link from "next/link";
import { GuardianShell } from "../../../../components/guardian-shell";
import { ReceiptPrint } from "../../../../features/receipt-print";
import { TournamentReceipt } from "../../../../features/tournament-receipt";
import { receiptListHref, receiptScope } from "../../../../lib/guardian-receipt-links";

export default async function Page({ params, searchParams }: {
  params: Promise<{ receiptId: string }>;
  searchParams: Promise<{ source?: string; playerId?: string; page?: string }>;
}) {
  const { receiptId } = await params;
  const query = await searchParams;
  const { playerId, page } = receiptScope(query);
  const id = encodeURIComponent(receiptId);
  const tournament = query.source === "Tournament";
  return <GuardianShell>
    <header className="guardian-page-header guardian-receipt-header">
      <Link href={`${receiptListHref(playerId, page)}#receipt-${id}`}><ArrowRight size={18} aria-hidden />العودة إلى الإيصالات</Link>
      <h1>{tournament ? "إيصال رسم البطولة" : "إيصال دفع"}</h1>
    </header>
    {tournament ? <TournamentReceipt id={id} /> : <ReceiptPrint endpoint={`/api/v1/guardian/subscriptions/receipts/${id}`} />}
  </GuardianShell>;
}
