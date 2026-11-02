import { ProviderQuarantineDetail } from "../../../../../features/provider-quarantine";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { return <ProviderQuarantineDetail id={(await params).id} />; }
