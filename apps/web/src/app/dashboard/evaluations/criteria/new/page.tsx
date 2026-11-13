import { Suspense } from "react";
import { LoadingState } from "../../../../../components/dashboard-shell";
import { CriterionForm } from "../../../../../features/evaluation-criteria";

export default function Page() {
  return <Suspense fallback={<LoadingState />}><CriterionForm /></Suspense>;
}
