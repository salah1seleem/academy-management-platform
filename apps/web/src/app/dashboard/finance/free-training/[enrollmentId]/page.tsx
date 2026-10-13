import { FreeTraining } from "../../../../../features/free-training";
export default async function Page({ params }: { params: Promise<{ enrollmentId: string }> }) { const { enrollmentId } = await params; return <FreeTraining key={enrollmentId} enrollmentId={enrollmentId} />; }
