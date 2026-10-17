import { OwnerFollowUpDetail } from "../../../../../features/owner-followups";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <OwnerFollowUpDetail id={id} />; }
