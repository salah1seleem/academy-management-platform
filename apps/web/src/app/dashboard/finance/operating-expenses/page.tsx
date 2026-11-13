import { OperatingExpenses } from "../../../../features/operating-expenses";
import { AcademyDateDefaults } from "../../../../components/academy-date-defaults";
export default async function Page({ searchParams }: { searchParams: Promise<{
  from?: string; to?: string; branchId?: string; sportId?: string; status?: string; category?: string;
  paid?: string; unpaid?: string; dateBasis?: string; allDates?: string; expenseId?: string;
}> }) {
  const filters = await searchParams;
  return <AcademyDateDefaults title="المصروفات التشغيلية" context="المالية" skip={filters.unpaid === "true" || filters.dateBasis === "payment" || !!filters.expenseId}><OperatingExpenses key={JSON.stringify(filters)}
    initialFrom={filters.from} initialTo={filters.to} initialBranchId={filters.branchId} initialSportId={filters.sportId}
    initialStatus={filters.status} initialCategory={filters.category} initialPaid={filters.paid === "true"}
    initialUnpaid={filters.unpaid === "true"} initialPaymentDates={filters.dateBasis === "payment"}
    initialAllDates={filters.allDates === "true"} initialExpenseId={filters.expenseId}
  /></AcademyDateDefaults>;
}
