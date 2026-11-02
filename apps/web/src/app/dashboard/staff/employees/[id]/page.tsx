import { EmployeeProfile } from "../../../../../features/employee-profile";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <EmployeeProfile id={id} />; }
