import { TrainingSessionsList } from "../../../../features/attendance";

type Query = { from?: string; to?: string; groupId?: string; playerId?: string; search?: string; status?: string };
export default async function Page({ searchParams }: { searchParams: Promise<Query> }) {
  const query = await searchParams;
  return <TrainingSessionsList key={JSON.stringify(query)} initial={query} playerId={typeof query.playerId === "string" ? query.playerId : undefined} />;
}
