import { GuardianShell } from "../../../../../components/guardian-shell";
import { PlayerCardPanel } from "../../../../../features/player-card";
import Link from "next/link";
export default async function Page({ params }: { params: Promise<{ playerId: string }> }) {
  const { playerId } = await params;
  return <GuardianShell><div className="player-card-page"><Link className="back-link" href={`/guardian/children/${playerId}`}>العودة لملف الطفل</Link><PlayerCardPanel playerId={playerId} guardian /></div></GuardianShell>;
}
