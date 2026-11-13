import { GuardianMatches } from "../../../features/guardian-matches";

export default async function Page({ searchParams }: { searchParams: Promise<{ playerId?: string; matchId?: string }> }) {
  const { playerId, matchId } = await searchParams;
  return <GuardianMatches key={`${playerId}:${matchId}`} playerId={playerId} matchId={matchId} />;
}
