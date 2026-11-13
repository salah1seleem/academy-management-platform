import { NutritionLibrary } from "../../../../../features/guardian-content";

export default async function Page({ params, searchParams }: {
  params: Promise<{ playerId: string }>;
  searchParams: Promise<{ day?: string; category?: string }>;
}) {
  const { playerId } = await params;
  const { day, category } = await searchParams;
  return <NutritionLibrary key={`${playerId}:${category ?? ""}`} playerId={playerId} initialCategory={category} day={day && /^\d{4}-\d{2}-\d{2}$/.test(day) ? day : undefined} />;
}
