import { PlayerGoalEditor } from "../../../../../features/player-goals";
import { DevelopmentEntry } from "../../../../../features/development-entry";
export default async function Page({ searchParams }: { searchParams: Promise<{ enrollmentId?: string }> }) {
  const { enrollmentId } = await searchParams;
  return enrollmentId && /^[a-f0-9-]{36}$/i.test(enrollmentId) ? <PlayerGoalEditor enrollmentId={enrollmentId} /> : <DevelopmentEntry title="هدف تطور جديد" destination="/dashboard/evaluations/goals/new"/>;
}
