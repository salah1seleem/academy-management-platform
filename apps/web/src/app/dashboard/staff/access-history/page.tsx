import { StaffAccessAudit } from "../../../../features/staff-access-audit";

export default async function Page({ searchParams }: { searchParams: Promise<{ from?: string; to?: string; allDates?: string }> }) {
  const filters = await searchParams;
  return <StaffAccessAudit initialFrom={filters.from} initialTo={filters.to} initialAllDates={filters.allDates === "true"} />;
}
