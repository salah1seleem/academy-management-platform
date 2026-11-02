import { FacilityOperations } from "../../../features/facilities";
export default async function Page({ searchParams }: { searchParams: Promise<{ branchId?: string }> }) { const { branchId } = await searchParams; return <FacilityOperations initialBranchId={branchId && /^[0-9a-fA-F-]{36}$/.test(branchId) ? branchId : ""} />; }
