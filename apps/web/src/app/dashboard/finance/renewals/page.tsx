import { RenewalOpportunities } from "../../../../features/renewal-opportunities";
export default async function Page({ searchParams }: { searchParams: Promise<{ branchId?: string | string[] }> }) {
  const { branchId } = await searchParams;
  const branch = typeof branchId === "string" ? branchId : "";
  return <RenewalOpportunities key={branch} initialBranchId={branch} />;
}
