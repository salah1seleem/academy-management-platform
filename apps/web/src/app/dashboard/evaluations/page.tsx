import { EvaluationsList } from "../../../features/evaluations";
import { ContextHelp } from "../../../components/context-help";
import Link from "next/link";
import {TechnicalInboxLink} from "../../../features/player-technical-reviews";
import {BadgeRulesLink} from "../../../features/player-badges-link";
export default async function Page({ searchParams }: { searchParams: Promise<{ playerId?: string; groupId?: string }> }) { const query = await searchParams; return <><ContextHelp lesson="evaluations" /><nav className="workspace-section-links" aria-label="أقسام التقييم والتطور"><Link href="/dashboard/evaluations" aria-current="page">تقييمات اللاعبين</Link><Link className="button button-secondary" href="/dashboard/evaluations/skills">مناهج المهارات ومتطلبات المستويات</Link><Link className="button button-secondary" href="/dashboard/evaluations/lessons">خطط الحصص والتمارين</Link><BadgeRulesLink/><TechnicalInboxLink/></nav><EvaluationsList playerId={query.playerId} groupId={query.groupId} /></>; }
