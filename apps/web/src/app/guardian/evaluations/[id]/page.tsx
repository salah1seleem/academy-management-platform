import { GuardianShell } from "../../../../components/guardian-shell";
import { EvaluationReport } from "../../../../features/evaluations";

export default async function Page({ params, searchParams }: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ child?: string | string[] }>;
}) {
  const [{ id }, { child }] = await Promise.all([params, searchParams]);
  const childId = typeof child === "string" && /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(child) ? child : undefined;
  return <GuardianShell><EvaluationReport id={id} guardian childId={childId} /></GuardianShell>;
}
