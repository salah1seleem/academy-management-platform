import { AssignedFollowUps } from "../../../features/assigned-followups";
export default async function Page({ searchParams }: { searchParams: Promise<{ allDates?: string }> }) {
  return <AssignedFollowUps initialAllDates={(await searchParams).allDates === "true"} />;
}
