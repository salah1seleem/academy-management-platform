import { AttendanceWorkspace } from "../../../../features/attendance-workspace";
export default async function Page({ searchParams }: { searchParams: Promise<{ playerId?: string;tab?:string }> }) { const query = await searchParams; return <AttendanceWorkspace playerId={query.playerId} initialTab={query.tab==="corrections"?"corrections":query.tab==="reports"?"reports":"history"} />; }
