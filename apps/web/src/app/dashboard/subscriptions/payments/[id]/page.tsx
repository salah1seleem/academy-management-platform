"use client";

import { useParams } from "next/navigation";
import { AdminPaymentDetail } from "../../../../../features/subscriptions";

export default function Page() {
  return <AdminPaymentDetail id={String(useParams().id)} />;
}
