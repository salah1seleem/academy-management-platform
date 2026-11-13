import { Suspense } from "react";
import { LoadingState } from "../../../../../../components/dashboard-shell";
import { CriterionForm } from "../../../../../../features/evaluation-criteria";

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <Suspense fallback={<LoadingState />}><CriterionForm id={id} key={id} /></Suspense>;
}
