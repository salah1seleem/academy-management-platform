import { PlayerGoals } from "../../../../features/player-goals";
import { DevelopmentEntry } from "../../../../features/development-entry";
export default async function Page({ searchParams }: { searchParams: Promise<{ enrollmentId?: string }> }) {
  const { enrollmentId } = await searchParams;
  return enrollmentId && /^[a-f0-9-]{36}$/i.test(enrollmentId) ? <PlayerGoals enrollmentId={enrollmentId} /> : <DevelopmentEntry title="أهداف تطور اللاعبين" destination="/dashboard/evaluations/goals"/>;
}
