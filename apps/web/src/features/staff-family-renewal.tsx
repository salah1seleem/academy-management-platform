"use client";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { useEffect, useRef, useState } from "react";
import type { Enrollment, Quote } from "./family-renewal-quote";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { formatMoneyAr } from "../lib/formatters";
import { canonicalDigits } from "../lib/number-input";
import styles from "./family-renewal-quote.module.css";
import { CashFamilyConfirmation } from "./cash-family-confirmation";
import { PendingCashFamily, recallCashFamily } from "../lib/pending-cash-family";

const root = "/api/v1/manage/family-renewals/quotes";
type Family = { id: string; name: string; phone?: string; phoneHint?: string; reference?: string };
type ShiftAccess = { state: "ready"; canCross: boolean } | { state: "closed" | "denied" | "retry" };
export function StaffFamilyRenewal({ cashSessionId }: { cashSessionId?: string }) {
  const [cashHash, setCashHash] = useState(""), [locked, setLocked] = useState(false);
  const [cross, setCross] = useState(false), [pending, setPending] = useState<PendingCashFamily | null>(null);
  const [shiftAccess, setShiftAccess] = useState<ShiftAccess | null>(null), [shiftRevision, setShiftRevision] = useState(0);
  const [search, setSearch] = useState(""), [families, setFamilies] = useState<Family[] | null>(null), [family, setFamily] = useState<Family | null>(null);
  const [options, setOptions] = useState<Enrollment[] | null>(null), [selection, setSelection] = useState<Record<string, string>>({}), [quote, setQuote] = useState<Quote | null>(null);
  const [error, setError] = useState(""), [searching, setSearching] = useState(false), [busy, setBusy] = useState(false), [reload, setReload] = useState(0);
  const searchAbort = useRef<AbortController | null>(null);
  const shiftBack = cashSessionId ? { href: `/dashboard/finance/cash-registers/sessions/${encodeURIComponent(cashSessionId)}`, label: "العودة إلى الوردية" } : undefined;
  const phoneSearch = /^[+٠-٩۰-۹0-9\s()-]+$/.test(search.trim());
  const searchTerm = phoneSearch ? canonicalDigits(search).replace(/[\s()-]/g, "") : search.trim();
  useEffect(() => () => searchAbort.current?.abort(), []);
  useEffect(() => {
    if (!cashSessionId) return;
    const previous = recallCashFamily(cashSessionId);
    if (previous) queueMicrotask(() => { setPending(previous); setLocked(true); });
  }, [cashSessionId]);
  useEffect(() => {
    if (!cashSessionId) return;
    const abort = new AbortController();
    void fetch(`/api/v1/finance/cash-registers/sessions/${encodeURIComponent(cashSessionId)}`, { cache: "no-store", signal: abort.signal })
      .then(async response => {
        if ([401, 403, 404].includes(response.status)) return { state: "denied" } as const;
        if (!response.ok) throw new Error();
        const data = await response.json();
        if (data?.session?.id !== cashSessionId || typeof data.canCollect !== "boolean") throw new Error();
        if (data.session.closedAtUtc) return { state: "closed" } as const;
        return data.canCollect ? { state: "ready", canCross: data.canCrossBranchPayment === true } as const : { state: "denied" } as const;
      })
      .then(access => { if (!abort.signal.aborted) setShiftAccess(access); })
      .catch(() => { if (!abort.signal.aborted) setShiftAccess({ state: "retry" }); });
    return () => abort.abort();
  }, [cashSessionId, shiftRevision]);
  useEffect(() => {
    if (!family) return;
    const abort = new AbortController();
    const promise = cross && cashSessionId && family.reference
      ? csrfRequest(`/api/v1/finance/cash-registers/sessions/${cashSessionId}/cross-branch/families/options`, "POST", { reference: family.reference }, {}, { announce: false })
      : fetch(`${root}/${family.id}/options`, { cache: "no-store", signal: abort.signal });
    void promise.then(async r => {
      if (!r.ok) throw new Error();
      const { items } = await r.json() as { items: Enrollment[] };
      if (abort.signal.aborted) return;
      setOptions(items);
      setSelection(current => Object.fromEntries(Object.entries(current).filter(([id, plan]) => items.some(e =>
        e.id === id && (!cashSessionId || !e.hasOpenRequest) && (plan ? e.plans.some(p => p.id === plan) : e.plans.length > 0)))));
    }).catch(() => { if (!abort.signal.aborted) setError("تعذر تحميل تسجيلات الأسرة. راجع صلاحيتك أو حاول مرة أخرى."); });
    return () => abort.abort();
  }, [family, reload, cross, cashSessionId]);
  async function find() {
    searchAbort.current?.abort(); const abort = new AbortController(); searchAbort.current = abort;
    setSearching(true); setFamilies(null); setFamily(null); setOptions(null); setSelection({}); setQuote(null); setError("");
    try { const r = cross && cashSessionId
      ? await csrfRequest(`/api/v1/finance/cash-registers/sessions/${cashSessionId}/cross-branch/families/search`, "POST", { search: searchTerm }, {}, { announce: false })
      : await fetch(`${root}/families?search=${encodeURIComponent(searchTerm)}`, { cache: "no-store", signal: abort.signal }); if (!r.ok) throw new Error(); const data = await r.json(); if (!abort.signal.aborted) setFamilies(cross ? data.items.map((x: Family) => ({ ...x, id: x.reference })) : data.items); }
    catch (e) { if (!abort.signal.aborted) setError(e instanceof DashboardRequestError ? e.message : "تعذر البحث عن الأسرة. راجع صلاحيتك وحاول مجددًا."); }
    finally { if (!abort.signal.aborted) setSearching(false); }
  }
  function selectFamily(value: Family) { setOptions(null); setSelection({}); setQuote(null); setError(""); setFamily(value); }
  function choose(id: string, plan: string | null) {
    setQuote(null); setError(""); setSelection(old => { const next = { ...old }; if (plan === null) delete next[id]; else next[id] = plan; return next; });
  }
  async function preview() {
    if (!family || busy) return; setBusy(true); setError("");
    try { const lines = Object.entries(selection).map(([sportEnrollmentId, subscriptionPlanId]) => ({ sportEnrollmentId, subscriptionPlanId })); const path = cashSessionId ? cross ? `/api/v1/finance/cash-registers/sessions/${cashSessionId}/cross-branch/families/quote` : `/api/v1/finance/cash-registers/sessions/${cashSessionId}/family-quote` : `${root}/${family.id}/quote`; const data = await (await csrfRequest(path, "POST", cashSessionId ? cross ? { reference: family.reference, lines } : { guardianId: family.id, lines } : { lines }, {}, { announce: false })).json(); if (cashSessionId) { setQuote(data.family); setCashHash(data.quoteHash); } else setQuote(data); }
    catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر مراجعة السعر. لم يُنشأ دفع أو تجديد؛ حاول مجددًا."); }
    finally { setBusy(false); }
  }
  const count = Object.keys(selection).length;
  function reopenReview(message?: string) {
    setPending(null);
    setLocked(false);
    setQuote(null);
    setError(message ?? "");
    if (message) setReload(value => value + 1);
    if (cashSessionId) {
      setShiftAccess(null);
      setShiftRevision(value => value + 1);
    }
  }
  if (cashSessionId && pending) return <section className={styles.page}><PageHeader title="تحصيل اشتراكات الأسرة" context="تجديد الأسرة" back={shiftBack}/><CashFamilyConfirmation sessionId={cashSessionId} body={pending.body} amount="" currency="EGP" onLocked={() => setLocked(true)} onUnlocked={reopenReview} /></section>;
  if (cashSessionId && shiftAccess?.state !== "ready") return <section className={styles.page}>
    <PageHeader title="تحصيل اشتراكات الأسرة" context="تجديد الأسرة" back={shiftBack}/>
    {!shiftAccess ? <LoadingState message="جارٍ التحقق من الوردية…" />
      : shiftAccess.state === "closed" ? <ErrorState message="هذه الوردية مقفلة. افتح وردية للتحصيل." />
      : shiftAccess.state === "denied" ? <ErrorState message="هذه الوردية غير متاحة للتحصيل بحسابك." />
      : <><ErrorState message="تعذر التحقق من الوردية." /><button className="secondary-button" type="button" onClick={() => { setShiftAccess(null); setShiftRevision(value => value + 1); }}>إعادة المحاولة</button></>}
  </section>;
  return <section className={styles.page}>
    <PageHeader title={cashSessionId ? "تحصيل اشتراكات الأسرة" : "تجديد عدة أبناء معًا"} context="تجديد الأسرة" back={shiftBack} secondaryActions={cashSessionId ? [] : [{label:"تجديد لاعب منفرد",href:"/dashboard/subscriptions/renew/new"}]}/>
    {shiftAccess?.state === "ready" && shiftAccess.canCross && !locked && <label className={styles.choice}><input type="checkbox" checked={cross} disabled={busy} onChange={e => { searchAbort.current?.abort(); setSearching(false); setCross(e.target.checked); setFamilies(null); setFamily(null); setOptions(null); setSelection({}); setQuote(null); setError(""); }} />تحصيل أسرة لديها أبناء في فروع أخرى</label>}
    <form className="form-shell" onSubmit={e => { e.preventDefault(); if (!locked && !busy && !searching && searchTerm.length >= 3) void find(); }}><label>اسم ولي الأمر أو رقم هاتفه<input value={search} dir={phoneSearch ? "ltr" : "auto"} maxLength={100} disabled={busy || locked} onChange={e => setSearch(e.target.value)} placeholder="اسم من 3 أحرف أو رقم هاتف كامل" /></label><button className="primary-button" disabled={locked || busy || searching || searchTerm.length < 3}>البحث عن الأسرة</button></form>
    {error && <p role="alert" className="error-message">{error}</p>}{searching && <p role="status">جارٍ البحث…</p>}
    {families?.length === 0 && <p>لا توجد أسرة مطابقة في نطاقك.</p>}
    {families && !family && <section className={styles.options} aria-label="نتائج الأسر">{families.map(f => <article className={styles.card} key={f.id}><strong>{f.name}</strong><small dir="ltr">{f.phoneHint ?? f.phone}</small><button className="secondary-button" onClick={() => selectFamily(f)}>اختيار {f.name}</button></article>)}</section>}
    {family && <><h2>{family.name}</h2>{!options && !error && <p role="status">جارٍ تحميل التسجيلات…</p>}{!options && error && <button className="secondary-button" onClick={() => { setError(""); setReload(x => x + 1); }}>إعادة تحميل تسجيلات الأسرة</button>}{options?.length === 0 && <p>لا توجد تسجيلات متاحة لهذه الأسرة.</p>}
      {options && !quote && <form onSubmit={e => { e.preventDefault(); void preview(); }}><fieldset className={styles.options} disabled={busy}><legend>اختر التسجيلات والباقات</legend>{options.map(e => {
        const unavailable = cashSessionId && e.hasOpenRequest ? "لهذا التسجيل طلب دفع مفتوح." : !e.plans.length ? "لا توجد باقة منشورة مناسبة." : "";
        return <article className={`${styles.card} ${e.id in selection ? styles.selected : ""}`} key={e.id}>
          <label className={styles.choice}><input type="checkbox" checked={e.id in selection} disabled={Boolean(unavailable)} onChange={event => choose(e.id, event.target.checked ? "" : null)} aria-label={`تجديد ${e.player} · ${e.sport}`} /><span><strong>{e.player}</strong><small>{e.sport} · {e.branch}</small></span></label>
          {unavailable ? <small>{unavailable}</small> : e.id in selection && <label className={styles.plan}>الباقة لـ{e.player} · {e.sport}<select required value={selection[e.id]} onChange={event => choose(e.id, event.target.value)}><option value="">اختر الباقة</option>{e.plans.map(p => <option value={p.id} key={p.id}>{p.name} · {formatMoneyAr(p.amount, p.currency)}</option>)}</select></label>}
        </article>;
      })}</fieldset><button className="primary-button" disabled={busy || count < 1 || count > 20 || Object.values(selection).some(x => !x)}>مراجعة إجمالي {count} تسجيل</button></form>}
      {quote && <><section className={styles.summary} aria-label="مراجعة سعر الأسرة"><h2>إجمالي الأسرة</h2><div className={cashSessionId ? `${styles.totals} ${styles.cashTotals}` : styles.totals}>{[["قبل الخصم", quote.originalAmount], ["الخصم", quote.discountAmount], ["الإجمالي", quote.totalAmount]].map(([name, value]) => <div key={name}><small>{name}</small><strong>{formatMoneyAr(value, quote.currency)}</strong></div>)}</div>{quote.items.map(l => <article className={styles.line} key={l.sportEnrollmentId}><div><strong>{l.player}</strong><small>{l.sport} · {l.branch} · {l.plan}</small>{l.discountReason && <small>{l.discountReason}</small>}</div><strong>{formatMoneyAr(l.finalAmount, l.currency)}</strong></article>)}</section><button className="secondary-button" disabled={busy || locked} onClick={() => setQuote(null)}>تعديل الأبناء والباقات</button></>}
      {cashSessionId && quote && <CashFamilyConfirmation key={cashHash} sessionId={cashSessionId} body={cross && family.reference ? { reference: family.reference, quoteHash: cashHash, lines: Object.entries(selection).map(([sportEnrollmentId, subscriptionPlanId]) => ({ sportEnrollmentId, subscriptionPlanId })) } : { guardianId: family.id, quoteHash: cashHash, lines: Object.entries(selection).map(([sportEnrollmentId, subscriptionPlanId]) => ({ sportEnrollmentId, subscriptionPlanId })) }} amount={quote.totalAmount} currency={quote.currency} onLocked={() => setLocked(true)} onUnlocked={reopenReview} />}
      {!cashSessionId && <aside className={styles.notice}>هذه مراجعة سعر فقط؛ لم يُسجل تحصيل ولم تُمدد الاشتراكات. تحتفظ كل رياضة بباقة وتسجيل مستقلين.</aside>}
    </>}
  </section>;
}
