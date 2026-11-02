import { CashSessionDetail } from "../../../../../../features/cash-registers";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <CashSessionDetail id={id} />; }
