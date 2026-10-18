import { PlayerGoalEditor } from "../../../../../features/player-goals";
export default async function Page({ searchParams }: { searchParams: Promise<{ enrollmentId?: string }> }) {
  const { enrollmentId } = await searchParams;
  return enrollmentId && /^[a-f0-9-]{36}$/i.test(enrollmentId) ? <PlayerGoalEditor enrollmentId={enrollmentId} /> : <p>اختر تسجيل اللاعب الرياضي من ملفه أولًا.</p>;
}
