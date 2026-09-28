import { AdminEnrollmentRequestDetails } from "../../../../features/guardian-core";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <AdminEnrollmentRequestDetails id={id} />; }
