import { PlayerGoals } from "../../../../features/player-goals";
export default async function Page({ searchParams }: { searchParams: Promise<{ enrollmentId?: string }> }) {
  const { enrollmentId } = await searchParams;
  return enrollmentId && /^[a-f0-9-]{36}$/i.test(enrollmentId) ? <PlayerGoals enrollmentId={enrollmentId} /> : <p>افتح أهداف التطور من ملف اللاعب لتحديد رياضته.</p>;
}
