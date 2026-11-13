import { NewEvaluation } from "../../../../features/evaluations";
export default async function Page({ searchParams }: { searchParams: Promise<{ enrollmentId?: string }> }) { const query = await searchParams; return <NewEvaluation key={query.enrollmentId ?? ""} initialEnrollment={query.enrollmentId} />; }
