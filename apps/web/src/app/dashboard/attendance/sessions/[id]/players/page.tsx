import { PlayerAttendance } from "../../../../../../features/attendance";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <PlayerAttendance sessionId={id} />; }
