"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { ErrorState } from "../../../../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "../../../../features/dashboard-api";
import {
  ExternalRenewalNotice,
  GuardianPlanChoices,
  guardianRenewalRoot,
  readExternalRenewalOpen,
  useGuardianRenewalCommand,
  type ExternalRenewalOpen,
  type GuardianRenewalPlan,
} from "../../../../features/guardian-renewal";

type Beneficiary = { playerDisplayName: string; sport: string; academyName: string; plans: GuardianRenewalPlan[] };

export default function Page() {
  const lookupVersion = useRef(0);
  const [reference, setReference] = useState("");
  const [beneficiary, setBeneficiary] = useState<Beneficiary | null>(null);
  const [selected, setSelected] = useState("");
  const [lookingUp, setLookingUp] = useState(false);
  const [lookupError, setLookupError] = useState("");
  const [openRequest, setOpenRequest] = useState<ExternalRenewalOpen | null>(null);
  const command = useGuardianRenewalCommand(() => {
    setOpenRequest({ kind: "Unavailable" });
    void resolve(true);
  });
  const locked = command.busy || command.uncertain;

  useEffect(() => () => { lookupVersion.current++; }, []);

  function changeReference(value: string) {
    lookupVersion.current++;
    setReference(value);
    setBeneficiary(null);
    setSelected("");
    setLookingUp(false);
    setLookupError("");
    setOpenRequest(null);
    command.clearError();
  }

  async function resolve(recheck = false) {
    const code = reference.trim();
    if (!code || lookingUp || (!recheck && (locked || openRequest))) return;
    const version = ++lookupVersion.current;
    setLookingUp(true);
    if (!recheck) setBeneficiary(null);
    setSelected("");
    setLookupError("");
    command.clearError();
    try {
      const response = await csrfRequest(`${guardianRenewalRoot}/external/resolve`, "POST", { reference: code });
      const data = await response.json() as (Beneficiary & { openRenewal?: unknown }) | null;
      if (!data?.playerDisplayName || !Array.isArray(data.plans)) throw new Error();
      const open = readExternalRenewalOpen(data.openRenewal);
      if (version === lookupVersion.current) {
        setBeneficiary(data);
        setOpenRequest(open);
        command.clearError();
      }
    } catch (caught) {
      if (version === lookupVersion.current) {
        if (caught instanceof DashboardRequestError && caught.status === 404) {
          setBeneficiary(null);
          setOpenRequest(null);
        }
        setLookupError(caught instanceof DashboardRequestError ? caught.message : "تعذر التحقق من الكود. أعد المحاولة.");
      }
    } finally {
      if (version === lookupVersion.current) setLookingUp(false);
    }
  }

  function submit() {
    if (openRequest || !beneficiary?.plans.some(plan => plan.id === selected)) return;
    const body = { reference: reference.trim(), subscriptionPlanId: selected };
    void command.submit(`${guardianRenewalRoot}/external/renewals`, `guardian-external-renewal:${selected}`, body);
  }

  return <main className="center-shell guardian-renewal-shell">
    <section className="authenticated-card guardian-renewal-card" aria-labelledby="external-renewal-title">
      <Link className="back-link" href="/guardian/renew-for-another">العودة إلى البحث بالاسم</Link>
      <h1 id="external-renewal-title">التجديد بكود الأكاديمية</h1>
      <form onSubmit={event => { event.preventDefault(); void resolve(); }}>
        <label className="field">كود التجديد
          <input dir="ltr" value={reference} onChange={event => changeReference(event.target.value)} required autoComplete="off" spellCheck={false} disabled={locked} />
        </label>
        {!openRequest && <button type="submit" className="secondary-button" disabled={locked || lookingUp || !reference.trim()}>
          {lookingUp ? "جارٍ التحقق من الكود…" : "تحقّق من الكود"}
        </button>}
      </form>
      {lookupError && !openRequest && <ErrorState message={lookupError} />}
      {beneficiary && <section className="beneficiary-confirmation" aria-label="المستفيد">
        <div className="guardian-renewal-context">
          <strong>{beneficiary.playerDisplayName}</strong>
          <span>{[beneficiary.sport, beneficiary.academyName].filter(Boolean).join(" · ")}</span>
        </div>
        {openRequest ? <ExternalRenewalNotice open={openRequest} checking={lookingUp} error={lookupError} onRetry={() => void resolve(true)} />
          : <form onSubmit={event => { event.preventDefault(); submit(); }}>
          <GuardianPlanChoices plans={beneficiary.plans} selected={selected} locked={locked} onSelect={id => { setSelected(id); command.clearError(); }} />
          {command.error && <ErrorState message={command.error} />}
          {beneficiary.plans.length > 0 && <button type="submit" className="primary-button" disabled={!selected || command.busy}>
            {command.busy ? "جارٍ فتح طلب الدفع…" : command.uncertain ? "إعادة إرسال نفس الطلب" : "متابعة إلى طلب الدفع"}
          </button>}
        </form>}
      </section>}
    </section>
  </main>;
}
