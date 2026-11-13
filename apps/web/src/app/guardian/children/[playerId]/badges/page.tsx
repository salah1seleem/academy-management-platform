import { PlayerBadges } from "../../../../../features/player-badges";

export default async function Page({ params, searchParams }: {
  params: Promise<{ playerId: string }>;
  searchParams: Promise<{ enrollmentId?: string }>;
}) {
  const [{ playerId }, { enrollmentId }] = await Promise.all([params, searchParams]);
  return <PlayerBadges playerId={playerId} enrollmentId={typeof enrollmentId === "string" ? enrollmentId : undefined} />;
}
