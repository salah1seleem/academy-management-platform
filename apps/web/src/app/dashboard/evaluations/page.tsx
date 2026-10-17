import { EvaluationsList } from "../../../features/evaluations";
import { ContextHelp } from "../../../components/context-help";
export default async function Page({ searchParams }: { searchParams: Promise<{ playerId?: string; groupId?: string }> }) { const query = await searchParams; return <><ContextHelp lesson="evaluations" /><EvaluationsList playerId={query.playerId} groupId={query.groupId} /></>; }
