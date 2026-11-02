import { AssignedFollowUps } from "../../../../features/assigned-followups";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { return <AssignedFollowUps id={(await params).id}/>; }
