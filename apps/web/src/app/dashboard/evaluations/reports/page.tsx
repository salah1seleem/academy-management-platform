import { Suspense } from "react";
import { LoadingState } from "../../../../components/dashboard-shell";
import { EvaluationsList } from "../../../../features/evaluations";
export default function Page() { return <Suspense fallback={<LoadingState />}><EvaluationsList publishedOnly /></Suspense>; }
