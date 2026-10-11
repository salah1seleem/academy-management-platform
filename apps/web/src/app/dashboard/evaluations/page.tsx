import { EvaluationsList } from "../../../features/evaluations";
export default async function Page({ searchParams }: { searchParams: Promise<{ playerId?: string; groupId?: string }> }) { const query = await searchParams; return <EvaluationsList playerId={query.playerId} groupId={query.groupId} />; }
