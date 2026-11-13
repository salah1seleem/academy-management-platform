"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { ErrorState } from "../components/dashboard-shell";
import { formatMoneyAr, planTypeLabel } from "../lib/formatters";
import { clearFinancialIntent, pendingFinancialIntent } from "../lib/pending-financial-intent";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

export const guardianRenewalRoot = "/api/v1/guardian/subscriptions";
const durationDays = new Intl.NumberFormat("ar-EG-u-nu-latn", { style: "unit", unit: "day", unitDisplay: "long" });
const durationMonths = new Intl.NumberFormat("ar-EG-u-nu-latn", { style: "unit", unit: "month", unitDisplay: "long" });

export type GuardianRenewalPlan = {
  id: string;
  arabicName: string;
  planType: string;
  price: number;
  currency: string;
  durationDays?: number | null;
  durationMonths?: number | null;
  sessionCount?: number | null;
};

export function guardianCheckoutPath(id: string) {
  return `/guardian/checkout/${encodeURIComponent(id)}`;
}

export type ExternalRenewalOpen = { kind: "Unavailable" } |
  { kind: "Payment" | "Family"; id: string };

export function readExternalRenewalOpen(value: unknown): ExternalRenewalOpen | null {
  if (value == null) return null;
  if (typeof value !== "object" || !("kind" in value)) throw new Error();
  const open = value as { kind: unknown; paymentId?: unknown; familyRequestId?: unknown };
  if (open.kind === "Unavailable") return { kind: "Unavailable" };
  if (open.kind !== "Payment" && open.kind !== "Family") throw new Error();
  const id = open.kind === "Payment" ? open.paymentId : open.familyRequestId;
  if (typeof id !== "string" || !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id)) throw new Error();
  return { kind: open.kind, id };
}

export function ExternalRenewalNotice({ open, checking, error, onRetry }: {
  open: ExternalRenewalOpen;
  checking: boolean;
  error: string;
  onRetry: () => void;
}) {
  if (checking) return <div className="guardian-renewal-notice" role="status">جارٍ التحقق من حالة الطلب…</div>;
  if (error) return <div className="guardian-renewal-notice">
    <ErrorState message={error} />
    <button type="button" className="secondary-button" onClick={onRetry}>إعادة التحقق</button>
  </div>;
  const family = open.kind === "Family";
  return <div className="guardian-renewal-notice" role="status">
    <h2>{family ? "طلب تجديد أسرة مفتوح" : open.kind === "Payment" ? "طلب دفع مفتوح" : "طلب تجديد مفتوح"}</h2>
    {open.kind === "Unavailable"
      ? <p className="muted">تواصل مع الأكاديمية لمتابعة الطلب.</p>
      : <Link className="primary-action" href={family ? `/guardian/family-renewals/${encodeURIComponent(open.id)}` : guardianCheckoutPath(open.id)}>
        {family ? "متابعة طلب الأسرة" : "متابعة طلب الدفع"}
      </Link>}
  </div>;
}

export async function saveGuardianPaymentRequest(path: string, scope: string, body: object) {
  const key = await pendingFinancialIntent(scope, body);
  const response = await csrfRequest(path, "POST", body, { "Idempotency-Key": key }, { announce: false });
  const result = await response.json() as { paymentId?: unknown } | null;
  if (typeof result?.paymentId !== "string" || !/^[a-z0-9-]+$/i.test(result.paymentId)) {
    throw new Error("Missing payment request ID");
  }
  return result.paymentId;
}

export function useGuardianRenewalCommand(onOpenRenewal?: () => void, onUnavailable?: () => void) {
  const router = useRouter();
  const mounted = useRef(false);
  const locked = useRef(false);
  const [busy, setBusy] = useState(false);
  const [uncertain, setUncertain] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

  async function submit(path: string, scope: string, body: object) {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      const id = await saveGuardianPaymentRequest(path, scope, body);
      if (mounted.current) {
        router.push(guardianCheckoutPath(id));
        clearFinancialIntent(scope);
      }
    } catch (caught) {
      locked.current = false;
      if (!mounted.current) return;
      const knownRejection = caught instanceof DashboardRequestError &&
        caught.status >= 400 && caught.status < 500 && caught.status !== 408;
      if (caught instanceof DashboardRequestError && caught.status === 404 && onUnavailable) {
        clearFinancialIntent(scope);
        onUnavailable();
        setBusy(false);
        setUncertain(false);
        return;
      }
      if (caught instanceof DashboardRequestError && caught.code === "renewal_already_open" && onOpenRenewal) {
        clearFinancialIntent(scope);
        onOpenRenewal();
      }
      setUncertain(!knownRejection);
      setError(knownRejection ? caught.message : "لم يصل تأكيد إنشاء طلب الدفع. أعد إرسال الطلب نفسه.");
      setBusy(false);
    }
  }

  return { busy, uncertain, error, submit, clearError: () => setError("") };
}

export function GuardianPlanChoices({ plans, selected, locked, onSelect }: {
  plans: GuardianRenewalPlan[];
  selected: string;
  locked: boolean;
  onSelect: (id: string) => void;
}) {
  if (!plans.length) return <p className="muted">لا توجد باقات متاحة لهذا التسجيل. تواصل مع الأكاديمية.</p>;
  return <fieldset className="guardian-plan-choices" disabled={locked}>
    <legend>الباقات المتاحة</legend>
    <div className="choice-list">
      {plans.map(plan => {
        const terms = [
          plan.durationMonths ? durationMonths.format(plan.durationMonths) : plan.durationDays ? durationDays.format(plan.durationDays) : null,
          plan.sessionCount ? `عدد الحصص ${plan.sessionCount}` : null,
        ].filter(Boolean).join(" · ");
        return <label key={plan.id}>
          <input type="radio" name="plan" value={plan.id} checked={selected === plan.id} onChange={() => onSelect(plan.id)} />
          <strong>{plan.arabicName}</strong>
          <span>{formatMoneyAr(plan.price, plan.currency)} · {terms || planTypeLabel(plan.planType)}</span>
        </label>;
      })}
    </div>
  </fieldset>;
}
