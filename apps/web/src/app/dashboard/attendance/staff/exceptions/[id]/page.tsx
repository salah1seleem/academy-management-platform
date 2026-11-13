import { Suspense } from "react";
import { StaffAttendanceExceptionDetail } from "../../../../../../features/staff-attendance-exceptions";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <Suspense fallback={null}><StaffAttendanceExceptionDetail key={id} id={id} /></Suspense>; }
