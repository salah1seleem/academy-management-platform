import { PlayerAttendance } from "../../../../../../features/attendance";
export default async function Page({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ playerId?: string }> }) { const [{ id }, query] = await Promise.all([params, searchParams]); return <PlayerAttendance sessionId={id} playerId={query.playerId} />; }
