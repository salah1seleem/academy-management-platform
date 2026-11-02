import { GuardianEdit } from "../../../../../features/guardian-record";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <GuardianEdit id={id} />; }
