"use client";

import { useParams, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { csrfRequest } from "../../../../features/dashboard-api";
import { formatMoneyAr, planTypeLabel } from "../../../../lib/formatters";

type Plan = { id: string; arabicName: string; planType: string; price: number; currency: string; durationDays?: number; sessionCount?: number };
export default function Page() {
  const enrollmentId = String(useParams().enrollmentId); const router = useRouter(); const [plans, setPlans] = useState<Plan[]>([]); const [selected, setSelected] = useState(""); const [error, setError] = useState("");
  useEffect(() => { fetch(`/api/v1/guardian/subscriptions/enrollments/${enrollmentId}/plans`).then(async response => { if (!response.ok) throw new Error(); setPlans(await response.json() as Plan[]); }).catch(() => setError("تعذر تحميل الباقات المتاحة.")); }, [enrollmentId]);
  async function pay() { try { const response = await csrfRequest("/api/v1/guardian/subscriptions/renewals", "POST", { sportEnrollmentId: enrollmentId, subscriptionPlanId: selected }, { "Idempotency-Key": crypto.randomUUID() }); const result = await response.json() as { paymentId: string }; router.push(`/guardian/checkout/${result.paymentId}`); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر بدء الدفع"); } }
  return <main className="center-shell"><section className="authenticated-card"><span className="eyebrow">تجديد الاشتراك</span><h1>اختر الباقة</h1><div className="choice-list">{plans.map(plan => <label key={plan.id}><input type="radio" name="plan" value={plan.id} checked={selected === plan.id} onChange={() => setSelected(plan.id)} /><strong>{plan.arabicName}</strong><span>{formatMoneyAr(plan.price, plan.currency)} · {planTypeLabel(plan.planType)}</span></label>)}</div>{error && <p className="error-message" role="alert">{error}</p>}<button className="primary-button" disabled={!selected} onClick={() => void pay()}>الدفع الإلكتروني — ادفع الآن</button></section></main>;
}
