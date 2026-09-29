"use client";

import { Suspense } from "react";
import { AdminRenewalFlow } from "../../../../../features/subscriptions";
import { LoadingState } from "../../../../../components/dashboard-shell";

export default function Page() {
  return <Suspense fallback={<LoadingState />}><AdminRenewalFlow /></Suspense>;
}
