import { TrainingSessionsList } from "../../../../features/attendance";
export default async function Page({ searchParams }: { searchParams: Promise<{ groupId?: string; playerId?: string }> }) { const query = await searchParams; return <TrainingSessionsList key={`${query.groupId ?? "all"}-${query.playerId ?? "all"}`} initialGroupId={query.groupId} playerId={query.playerId} />; }
