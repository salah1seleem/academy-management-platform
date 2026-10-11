import { EvaluationsList } from "../../../features/evaluations";
export default async function Page({ searchParams }: { searchParams: Promise<{ playerId?: string }> }) { const query = await searchParams; return <EvaluationsList playerId={query.playerId} />; }
