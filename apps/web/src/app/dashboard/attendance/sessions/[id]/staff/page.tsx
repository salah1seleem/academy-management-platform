import { StaffAttendance } from "../../../../../../features/attendance";

export default async function Page({ params, searchParams }: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ returnTo?: string }>;
}) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  return <StaffAttendance sessionId={id} returnTo={query.returnTo} />;
}
