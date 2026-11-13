import { Suspense } from "react";
import { LoadingState } from "../../../../components/dashboard-shell";
import { CriteriaList } from "../../../../features/evaluation-criteria";

export default function Page() {
  return <Suspense fallback={<LoadingState />}><CriteriaList /></Suspense>;
}
