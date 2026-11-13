import { EvaluationsList } from "../../../features/evaluations";
import { ContextHelp } from "../../../components/context-help";
import Link from "next/link";
import { Suspense } from "react";
import { LoadingState } from "../../../components/dashboard-shell";
import {TechnicalInboxLink} from "../../../features/player-technical-reviews";
import {BadgeRulesLink} from "../../../features/player-badges-link";
export default function Page() { return <><ContextHelp lesson="evaluations" /><nav className="workspace-section-links" aria-label="أقسام التقييم والتطور"><Link href="/dashboard/evaluations" aria-current="page">تقييمات اللاعبين</Link><Link className="button button-secondary" href="/dashboard/evaluations/skills">مناهج المهارات ومتطلبات المستويات</Link><Link className="button button-secondary" href="/dashboard/evaluations/lessons">خطط الحصص والتمارين</Link><BadgeRulesLink/><TechnicalInboxLink/></nav><Suspense fallback={<LoadingState />}><EvaluationsList /></Suspense></>; }
