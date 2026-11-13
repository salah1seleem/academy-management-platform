"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { ErrorState } from "../../../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "../../../features/dashboard-api";
import {
  ExternalRenewalNotice,
  GuardianPlanChoices,
  guardianRenewalRoot,
  readExternalRenewalOpen,
  useGuardianRenewalCommand,
  type ExternalRenewalOpen,
  type GuardianRenewalPlan,
} from "../../../features/guardian-renewal";

type Match = { name: string; sport: string; branch: string; reference: string | null };
type Beneficiary = { playerDisplayName: string; sport: string; branch: string; plans: GuardianRenewalPlan[]; openRenewal?: unknown };
const external = `${guardianRenewalRoot}/external`;
const staleSelection = "انتهى الاختيار أو تغيّر التسجيل. أعد البحث.";

function readMatches(data: unknown): Match[] {
  if (!Array.isArray(data)) throw new Error();
  const matches = new Map<string, Match>();
  for (const row of data) {
    if (!row || typeof row.name !== "string" || typeof row.sport !== "string" || typeof row.branch !== "string" ||
      !(row.reference === null || (typeof row.reference === "string" && row.reference.trim()))) throw new Error();
    const match = { name: row.name.trim(), sport: row.sport.trim(), branch: row.branch.trim(), reference: row.reference };
    if (!match.name || !match.sport || !match.branch) throw new Error();
    const key = JSON.stringify([match.name, match.sport, match.branch]);
    matches.set(key, matches.has(key) ? { ...match, reference: null } : match);
  }
  return [...matches.values()];
}

export default function Page() {
  const [name, setName] = useState("");
  const [matches, setMatches] = useState<Match[] | null>(null);
  const [activity, setActivity] = useState<"search" | number | null>(null);
  const [lookupError, setLookupError] = useState("");
  const [selection, setSelection] = useState<{ match: Match; plans: GuardianRenewalPlan[]; index: number; open: ExternalRenewalOpen | null } | null>(null);
  const [selectedPlan, setSelectedPlan] = useState("");
  const pending = useRef<AbortController | null>(null);
  const input = useRef<HTMLInputElement>(null);
  const confirmation = useRef<HTMLHeadingElement>(null);
  const results = useRef<HTMLUListElement>(null);
  const returnFocus = useRef<number | "input" | null>(null);
  const command = useGuardianRenewalCommand(() => {
    if (!selection) return;
    setSelection({ ...selection, open: { kind: "Unavailable" } });
    setSelectedPlan("");
    choose(selection.match, selection.index);
  }, () => {
    setSelection(null);
    setSelectedPlan("");
    setMatches(null);
    setLookupError("انتهى الاختيار أو تغيّرت الباقة. أعد البحث.");
    returnFocus.current = "input";
  });
  const locked = command.busy || command.uncertain;

  useEffect(() => () => {
    pending.current?.abort();
    pending.current = null;
  }, []);
  useEffect(() => {
    if (selection) {
      confirmation.current?.focus();
    } else if (returnFocus.current !== null) {
      const target = returnFocus.current === "input" ? input.current :
        results.current?.querySelectorAll("li")[returnFocus.current]?.querySelector("button");
      target?.focus();
      returnFocus.current = null;
    }
  }, [selection]);

  function changeName(value: string) {
    pending.current?.abort();
    pending.current = null;
    setName(value);
    setActivity(null);
    setMatches(null);
    setLookupError("");
  }

  async function lookup(action: "search" | number, path: string, body: object, apply: (data: unknown) => void) {
    if (pending.current) return;
    const request = new AbortController();
    pending.current = request;
    setActivity(action);
    setLookupError("");
    if (action === "search") setMatches(null);
    try {
      const response = await csrfRequest(`${external}/${path}`, "POST", body, {}, { signal: request.signal });
      const data: unknown = await response.json();
      if (pending.current === request) apply(data);
    } catch (caught) {
      if (pending.current !== request) return;
      if (action !== "search" && caught instanceof DashboardRequestError && caught.status === 404) {
        if (selection) returnFocus.current = "input";
        setSelection(null);
        setMatches(null);
        setLookupError(staleSelection);
        input.current?.focus();
      } else {
        const fallback = action === "search" ? "تعذر البحث عن اللاعب. أعد المحاولة." : "تعذر تأكيد التسجيل. أعد المحاولة.";
        setLookupError(caught instanceof DashboardRequestError ? caught.message : fallback);
      }
    } finally {
      if (pending.current === request) {
        pending.current = null;
        setActivity(null);
      }
    }
  }

  function search(event: FormEvent) {
    event.preventDefault();
    const term = name.trim();
    if (term.length < 3 || term.length > 100) return;
    void lookup("search", "search", { name: term }, data => setMatches(readMatches(data)));
  }

  function choose(match: Match, index: number) {
    if (!match.reference) return;
    void lookup(index, "resolve", { reference: match.reference }, data => {
      const beneficiary = data as Beneficiary | null;
      if (!beneficiary || !Array.isArray(beneficiary.plans)) throw new Error();
      if (beneficiary.playerDisplayName?.trim() !== match.name || beneficiary.sport?.trim() !== match.sport ||
        beneficiary.branch?.trim() !== match.branch) throw new DashboardRequestError(staleSelection, 404);
      const open = readExternalRenewalOpen(beneficiary.openRenewal);
      setSelection({ match, index, plans: beneficiary.plans, open });
      command.clearError();
    });
  }

  function backToResults() {
    if (!selection || locked) return;
    pending.current?.abort();
    pending.current = null;
    setActivity(null);
    returnFocus.current = selection.index;
    setSelection(null);
    setSelectedPlan("");
    setLookupError("");
    command.clearError();
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!selection?.match.reference || selection.open || !selection.plans.some(plan => plan.id === selectedPlan)) return;
    const body = { reference: selection.match.reference, subscriptionPlanId: selectedPlan };
    void command.submit(`${external}/renewals`, `guardian-external-renewal:${selectedPlan}`, body);
  }

  return <main className="center-shell guardian-renewal-shell">
    <section className="authenticated-card guardian-renewal-card guardian-external-renewal" aria-labelledby="external-renewal-title">
      <Link className="back-link" href="/guardian">العودة إلى الرئيسية</Link>
      <h1 id="external-renewal-title">تجديد اشتراك لشخص آخر</h1>
      {selection ? <section className="beneficiary-confirmation" aria-labelledby="external-beneficiary">
        <div className="guardian-renewal-context">
          <h2 ref={confirmation} id="external-beneficiary" tabIndex={-1}>{selection.match.name}</h2>
          <span>{selection.match.sport} · {selection.match.branch}</span>
        </div>
        {selection.open ? <ExternalRenewalNotice open={selection.open} checking={activity !== null} error={lookupError}
          onRetry={() => choose(selection.match, selection.index)} /> : <form onSubmit={submit}>
          <GuardianPlanChoices plans={selection.plans} selected={selectedPlan} locked={locked} onSelect={id => { setSelectedPlan(id); command.clearError(); }} />
          {command.error && <ErrorState message={command.error} />}
          {selection.plans.length > 0 && <button type="submit" className="primary-button" disabled={!selectedPlan || command.busy}>
            {command.busy ? "جارٍ فتح طلب الدفع…" : command.uncertain ? "إعادة إرسال نفس الطلب" : "متابعة إلى طلب الدفع"}
          </button>}
        </form>}
        <button type="button" className="secondary-button" disabled={locked} onClick={backToResults}>تغيير التسجيل</button>
      </section> : <>
        <form onSubmit={search}>
          <label className="field">اسم اللاعب
            <input ref={input} dir="auto" value={name} onChange={event => changeName(event.target.value)} maxLength={100}
              autoComplete="off" enterKeyHint="search" aria-describedby={name.trim().length < 3 ? "external-search-hint" : undefined} />
          </label>
          {name.trim().length < 3 && <p id="external-search-hint" className="muted">اكتب ثلاثة أحرف على الأقل من اسم اللاعب.</p>}
          <button type="submit" className="primary-button" disabled={activity !== null || name.trim().length < 3}>
            {activity === "search" ? "جارٍ البحث…" : "بحث عن لاعب"}
          </button>
        </form>
        {lookupError && <ErrorState message={lookupError} />}
        <p className={matches?.length === 0 ? "muted" : "sr-only"} role="status">
          {matches && (matches.length ? "نتائج البحث جاهزة." : "لا توجد نتائج. راجع كتابة الاسم ثم أعد البحث.")}
        </p>
        {matches && matches.length > 0 && <section className="guardian-external-results" aria-labelledby="external-results">
          <h2 id="external-results">نتائج البحث</h2>
          <ul ref={results}>
            {matches.map((match, index) => <li key={JSON.stringify([match.name, match.sport, match.branch])}>
              <div className="guardian-renewal-context"><strong>{match.name}</strong><span>{match.sport} · {match.branch}</span></div>
              {match.reference ? <button type="button" className="secondary-button" disabled={activity !== null}
                aria-label={`اختيار ${match.name} · ${match.sport} · ${match.branch}`} onClick={() => choose(match, index)}>
                {activity === index ? "جارٍ فتح التسجيل…" : "اختيار التسجيل"}
              </button> : <p className="muted">أكثر من تسجيل بهذه البيانات. تواصل مع الأكاديمية لتحديد التسجيل.</p>}
            </li>)}
          </ul>
        </section>}
        <Link className="back-link" href="/guardian/renew-for-another/code">لديّ كود تجديد</Link>
      </>}
    </section>
  </main>;
}
