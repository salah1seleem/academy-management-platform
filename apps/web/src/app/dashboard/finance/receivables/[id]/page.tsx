import { ReceivableDetail } from "../../../../../features/receivables";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { return <ReceivableDetail id={(await params).id} />; }
