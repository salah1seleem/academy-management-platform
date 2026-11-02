import { StaffFamilyRenewal } from "../../../../../../../features/staff-family-renewal";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <StaffFamilyRenewal key={id} cashSessionId={id} />; }
