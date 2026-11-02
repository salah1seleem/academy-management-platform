import { GroupSessions } from "../../../../../features/group-sessions";

export default async function Page({ searchParams }: { searchParams: Promise<{ groupId?: string }> }) {
  const query = await searchParams;
  const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
  return <GroupSessions key={query.groupId ?? "all"} initialGroupId={query.groupId} initialDate={today} />;
}
