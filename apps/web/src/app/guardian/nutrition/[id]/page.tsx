import { NutritionDetails } from "../../../../features/guardian-content";

export default async function Page({ params, searchParams }: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ player?: string; category?: string; day?: string }>;
}) {
  const [{ id }, { player, category, day }] = await Promise.all([params, searchParams]);
  const playerId = player && /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(player) ? player : undefined;
  return <NutritionDetails id={id} playerId={playerId} category={category} day={day} />;
}
