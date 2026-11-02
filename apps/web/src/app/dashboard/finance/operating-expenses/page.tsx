import { OperatingExpenses } from "../../../../features/operating-expenses";
export default async function Page({ searchParams }: { searchParams: Promise<{ from?: string; to?: string; branchId?: string; status?: string; unpaid?: string; expenseId?: string }> }) {
  const filters = await searchParams;
  return <OperatingExpenses initialFrom={filters.from} initialTo={filters.to} initialBranchId={filters.branchId} initialStatus={filters.status} initialUnpaid={filters.unpaid === "true"} initialExpenseId={filters.expenseId} />;
}
