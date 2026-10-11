import { AttendanceHistory } from "../../../../features/attendance";
export default async function Page({ searchParams }: { searchParams: Promise<{ playerId?: string }> }) { const query = await searchParams; return <AttendanceHistory playerId={query.playerId} />; }
