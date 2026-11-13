import { AssignedFollowUps } from "../../../../features/assigned-followups";
export default async function Page({ params, searchParams }: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ allDates?: string }>;
}) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  return <AssignedFollowUps id={id} initialAllDates={query.allDates === "true"} />;
}
