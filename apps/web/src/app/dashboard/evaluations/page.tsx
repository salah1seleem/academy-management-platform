import { EvaluationsList } from "../../../features/evaluations";
import { ContextHelp } from "../../../components/context-help";
import Link from "next/link";
export default async function Page({ searchParams }: { searchParams: Promise<{ playerId?: string; groupId?: string }> }) { const query = await searchParams; return <><ContextHelp lesson="evaluations" /><Link className="button button-secondary" href="/dashboard/evaluations/skills">مناهج المهارات ومتطلبات المستويات</Link><Link className="button button-secondary" href="/dashboard/evaluations/lessons">خطط الحصص والتمارين</Link><EvaluationsList playerId={query.playerId} groupId={query.groupId} /></>; }
