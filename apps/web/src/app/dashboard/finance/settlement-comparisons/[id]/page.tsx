import { SettlementComparisonDetail } from "../../../../../features/settlement-comparisons";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { return <SettlementComparisonDetail id={(await params).id} />; }
