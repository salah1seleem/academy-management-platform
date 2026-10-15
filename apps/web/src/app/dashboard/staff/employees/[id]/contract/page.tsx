import { ContractHistory } from "../../../../../../features/contract-history";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <ContractHistory id={id} />; }
