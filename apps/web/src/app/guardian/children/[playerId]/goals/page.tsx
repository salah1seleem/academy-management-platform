import { GuardianPlayerGoals } from "../../../../../features/player-goals";
export default async function Page({ params }: { params: Promise<{ playerId: string }> }) { const { playerId } = await params; return <GuardianPlayerGoals playerId={playerId} />; }
