import { ReceivableCredit } from "../../../../../../features/receivable-credit";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { return <ReceivableCredit sourceId={(await params).id} />; }
