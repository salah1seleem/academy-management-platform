import { FinancialDecisions } from "../../../../features/financial-decisions";

export default async function Page({ searchParams }: { searchParams: Promise<{ from?: string; to?: string; branchId?: string; allDates?: string }> }) {
  const filters = await searchParams;
  return <FinancialDecisions initialFrom={filters.from} initialTo={filters.to} initialBranchId={filters.branchId} initialAllDates={filters.allDates === "true"} />;
}
