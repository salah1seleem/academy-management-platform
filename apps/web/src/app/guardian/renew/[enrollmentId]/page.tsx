"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../../../../components/dashboard-shell";
import {
  GuardianPlanChoices,
  guardianCheckoutPath,
  guardianRenewalRoot,
  useGuardianRenewalCommand,
  type GuardianRenewalPlan,
} from "../../../../features/guardian-renewal";

type Enrollment = { id: string; player: string; sport: string; branch: string };
type OpenRenewal = { kind: "Payment" | "Family"; paymentId?: string | null; familyRequestId?: string | null };
type Review = { plans: GuardianRenewalPlan[]; enrollment: Enrollment; openRenewal: OpenRenewal | null };
type ReadResult = Review | { error: string; retryable: boolean };

export default function Page() {
  const id = String(useParams().enrollmentId);
  return <RenewalPlan key={id} enrollmentId={id} />;
}

function RenewalPlan({ enrollmentId }: { enrollmentId: string }) {
  const [result, setResult] = useState<ReadResult | null>(null);
  const [revision, setRevision] = useState(0);
  const [selected, setSelected] = useState("");
  function reload() {
    setSelected("");
    setResult(null);
    setRevision(value => value + 1);
  }
  const command = useGuardianRenewalCommand(reload);

  useEffect(() => {
    const controller = new AbortController();
    void fetch(`${guardianRenewalRoot}/enrollments/${encodeURIComponent(enrollmentId)}/renewal-review`,
      { signal: controller.signal, cache: "no-store" }).then(async response => {
      if (response.status === 403 || response.status === 404) {
        if (!controller.signal.aborted) setResult({ error: "هذا التسجيل غير متاح.", retryable: false });
        return;
      }
      if (!response.ok) throw new Error();
      const review = await response.json() as Review | null;
      if (review?.enrollment?.id !== enrollmentId || !Array.isArray(review.plans) ||
        (review.openRenewal !== null && review.openRenewal?.kind !== "Payment" && review.openRenewal?.kind !== "Family")) throw new Error();
      if (!controller.signal.aborted) setResult(review);
    }).catch(() => {
      if (!controller.signal.aborted) setResult({ error: "تعذر تحميل بيانات التسجيل والباقات.", retryable: true });
    });
    return () => controller.abort();
  }, [enrollmentId, revision]);

  function submit() {
    if (!result || !("plans" in result) || result.openRenewal || !result.plans.some(plan => plan.id === selected)) return;
    const body = { sportEnrollmentId: enrollmentId, subscriptionPlanId: selected };
    void command.submit(`${guardianRenewalRoot}/renewals`, `guardian-renewal:${enrollmentId}:${selected}`, body);
  }

  return <main className="center-shell guardian-renewal-shell">
    <section className="authenticated-card guardian-renewal-card" aria-labelledby="renewal-title">
      <Link className="back-link" href="/guardian#children">العودة إلى الأبناء</Link>
      <h1 id="renewal-title">{result && "plans" in result && result.openRenewal ? "تجديد الاشتراك" : "اختيار باقة الاشتراك"}</h1>
      {!result ? <LoadingState message="جارٍ تحميل حالة التجديد…" />
        : "error" in result ? <>
          <ErrorState message={result.error} />
          {result.retryable && <button type="button" className="secondary-button" onClick={reload}>إعادة تحميل الباقات</button>}
        </> : <>
          <div className="guardian-renewal-context">
            <strong>{result.enrollment.player}</strong>
            <span>{[result.enrollment.sport, result.enrollment.branch].filter(Boolean).join(" · ")}</span>
          </div>
          {result.openRenewal ? <OpenRenewalNotice open={result.openRenewal} /> : <form onSubmit={event => { event.preventDefault(); submit(); }}>
            <GuardianPlanChoices plans={result.plans} selected={selected} locked={command.busy || command.uncertain} onSelect={id => { setSelected(id); command.clearError(); }} />
            {command.error && <ErrorState message={command.error} />}
            {result.plans.length > 0 && <button type="submit" className="primary-button" disabled={!selected || command.busy}>
              {command.busy ? "جارٍ فتح طلب الدفع…" : command.uncertain ? "إعادة إرسال نفس الطلب" : "متابعة إلى طلب الدفع"}
            </button>}
          </form>}
        </>}
    </section>
  </main>;
}

function OpenRenewalNotice({ open }: { open: OpenRenewal }) {
  const family = open.kind === "Family";
  const href = family
    ? open.familyRequestId && `/guardian/family-renewals/${encodeURIComponent(open.familyRequestId)}`
    : open.paymentId && guardianCheckoutPath(open.paymentId);
  return <section className="guardian-renewal-notice" aria-label="الطلب المفتوح">
    <h2>{family ? "طلب تجديد أسرة مفتوح" : "طلب دفع مفتوح"}</h2>
    {href
      ? <Link className="primary-action" href={href}>{family ? "متابعة طلب الأسرة" : "متابعة طلب الدفع"}</Link>
      : <p>تواصل مع الأكاديمية لمتابعة الطلب.</p>}
  </section>;
}
