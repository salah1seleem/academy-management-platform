import { ReceiptPrint } from "./receipt-print";

export function TournamentReceipt({ id }: { id: string }) {
  return <ReceiptPrint endpoint={`/api/v1/guardian/tournament-payments/${id}`} tournament />;
}
