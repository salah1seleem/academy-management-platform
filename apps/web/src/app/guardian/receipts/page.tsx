import { GuardianShell } from "../../../components/guardian-shell";
import { GuardianReceipts } from "../../../features/guardian-receipts";
import { receiptScope } from "../../../lib/guardian-receipt-links";

export default async function Page({ searchParams }: { searchParams: Promise<{ playerId?: string; page?: string }> }) {
  return <GuardianShell><GuardianReceipts {...receiptScope(await searchParams)} /></GuardianShell>;
}
