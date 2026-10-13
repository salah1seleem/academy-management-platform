"use client";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import type { Enrollment, Quote } from "./family-renewal-quote";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { formatMoneyAr } from "../lib/formatters";
import styles from "./family-renewal-quote.module.css";
import { CashFamilyConfirmation } from "./cash-family-confirmation";

const root = "/api/v1/manage/family-renewals/quotes";
type Family = { id: string; name: string; phone?: string };
export function StaffFamilyRenewal({ cashSessionId }: { cashSessionId?: string }) {
  const [cashHash, setCashHash] = useState(""), [locked, setLocked] = useState(false);
  const [search, setSearch] = useState(""), [families, setFamilies] = useState<Family[] | null>(null), [family, setFamily] = useState<Family | null>(null);
  const [options, setOptions] = useState<Enrollment[] | null>(null), [selection, setSelection] = useState<Record<string, string>>({}), [quote, setQuote] = useState<Quote | null>(null);
  const [error, setError] = useState(""), [searching, setSearching] = useState(false), [busy, setBusy] = useState(false), [reload, setReload] = useState(0);
  const searchAbort = useRef<AbortController | null>(null);
  useEffect(() => () => searchAbort.current?.abort(), []);
  useEffect(() => {
    if (!family) return;
    const abort = new AbortController();
    void fetch(`${root}/${family.id}/options`, { cache: "no-store", signal: abort.signal }).then(async r => { if (!r.ok) throw new Error(); const data = await r.json(); if (!abort.signal.aborted) setOptions(data.items); }).catch(() => { if (!abort.signal.aborted) setError("تعذر تحميل تسجيلات الأسرة. راجع صلاحيتك أو حاول مرة أخرى."); });
    return () => abort.abort();
  }, [family, reload]);
  async function find() {
    searchAbort.current?.abort(); const abort = new AbortController(); searchAbort.current = abort;
    setSearching(true); setFamilies(null); setFamily(null); setOptions(null); setSelection({}); setQuote(null); setError("");
    try { const r = await fetch(`${root}/families?search=${encodeURIComponent(search.trim())}`, { cache: "no-store", signal: abort.signal }); if (!r.ok) throw new Error(); const data = await r.json(); if (!abort.signal.aborted) setFamilies(data.items); }
    catch (e) { if (!abort.signal.aborted) setError(e instanceof DashboardRequestError ? e.message : "تعذر البحث عن الأسرة. راجع صلاحيتك وحاول مجددًا."); }
    finally { if (!abort.signal.aborted) setSearching(false); }
  }
  function selectFamily(value: Family) { setOptions(null); setSelection({}); setQuote(null); setError(""); setFamily(value); }
  function choose(id: string, plan: string | null) {
    setQuote(null); setError(""); setSelection(old => { const next = { ...old }; if (plan === null) delete next[id]; else next[id] = plan; return next; });
  }
  async function preview() {
    if (!family || busy) return; setBusy(true); setError("");
    try { const lines = Object.entries(selection).map(([sportEnrollmentId, subscriptionPlanId]) => ({ sportEnrollmentId, subscriptionPlanId })); const path = cashSessionId ? `/api/v1/finance/cash-registers/sessions/${cashSessionId}/family-quote` : `${root}/${family.id}/quote`; const data = await (await csrfRequest(path, "POST", cashSessionId ? { guardianId: family.id, lines } : { lines }, {}, { announce: false })).json(); if (cashSessionId) { setQuote(data.family); setCashHash(data.quoteHash); } else setQuote(data); }
    catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر مراجعة السعر. لم يُنشأ دفع أو تجديد؛ حاول مجددًا."); }
    finally { setBusy(false); }
  }
  const count = Object.keys(selection).length;
  return <section className={styles.page}>
    <Link href="/dashboard/subscriptions/renew/new">تجديد لاعب منفرد</Link>
    <header><span className="eyebrow">تجديد الأسرة</span><h1>راجع تجديد عدة أبناء معًا</h1><p>ابحث عن ولي الأمر، ثم اختر التسجيلات وباقة كل رياضة. تظهر بيانات الفروع المصرح بها فقط.</p></header>
    {cashSessionId && <Link href={`/dashboard/finance/cash-registers/sessions/${cashSessionId}`}>العودة للوردية النقدية</Link>}
    <form className="form-shell" onSubmit={e => { e.preventDefault(); if (!locked) void find(); }}><label>اسم ولي الأمر أو رقم هاتفه<input value={search} maxLength={100} disabled={busy || locked} onChange={e => setSearch(e.target.value)} placeholder="اسم من 3 أحرف أو رقم هاتف كامل" /></label><button className="primary-button" disabled={locked || busy || searching || search.trim().length < 3}>البحث عن الأسرة</button></form>
    {error && <p role="alert" className="error-message">{error}</p>}{searching && <p role="status">جارٍ البحث…</p>}
    {families?.length === 0 && <p>لا توجد أسرة مطابقة في نطاقك.</p>}
    {families && !family && <section className={styles.options} aria-label="نتائج الأسر">{families.map(f => <article className={styles.card} key={f.id}><strong>{f.name}</strong><small dir="ltr">{f.phone}</small><button className="secondary-button" onClick={() => selectFamily(f)}>اختيار {f.name}</button></article>)}</section>}
    {family && <><h2>{family.name}</h2>{!options && !error && <p role="status">جارٍ تحميل التسجيلات…</p>}{!options && error && <button className="secondary-button" onClick={() => { setError(""); setReload(x => x + 1); }}>إعادة تحميل تسجيلات الأسرة</button>}{options?.length === 0 && <p>لا توجد تسجيلات متاحة لهذه الأسرة.</p>}
      {options && !quote && <form onSubmit={e => { e.preventDefault(); void preview(); }}><fieldset className={styles.options} disabled={busy}><legend>اختر التسجيلات والباقات</legend>{options.map(e => <article className={`${styles.card} ${e.id in selection ? styles.selected : ""}`} key={e.id}><label className={styles.choice}><input type="checkbox" checked={e.id in selection} disabled={!e.plans.length} onChange={event => choose(e.id, event.target.checked ? "" : null)} aria-label={`تجديد ${e.player} · ${e.sport}`} /><span><strong>{e.player}</strong><small>{e.sport} · {e.branch}</small></span></label>{!e.plans.length ? <p>لا توجد باقة منشورة مناسبة.</p> : e.id in selection && <label className={styles.plan}>الباقة لـ{e.player} · {e.sport}<select required value={selection[e.id]} onChange={event => choose(e.id, event.target.value)}><option value="">اختر الباقة</option>{e.plans.map(p => <option value={p.id} key={p.id}>{p.name} · {formatMoneyAr(p.amount, p.currency)}</option>)}</select></label>}</article>)}</fieldset><button className="primary-button" disabled={busy || count < 1 || count > 20 || Object.values(selection).some(x => !x)}>مراجعة إجمالي {count} تسجيل</button></form>}
      {quote && <><section className={styles.summary} aria-label="مراجعة سعر الأسرة"><h2>إجمالي الأسرة</h2><div className={styles.totals}>{[["قبل الخصم", quote.originalAmount], ["الخصم المؤهل", quote.discountAmount], ["الإجمالي", quote.totalAmount]].map(([name, value]) => <div key={name}><small>{name}</small><strong>{formatMoneyAr(value, quote.currency)}</strong></div>)}</div>{quote.items.map(l => <article className={styles.line} key={l.sportEnrollmentId}><div><strong>{l.player}</strong><small>{l.sport} · {l.branch} · {l.plan}</small>{l.discountReason && <small>{l.discountReason}</small>}</div><strong>{formatMoneyAr(l.finalAmount, l.currency)}</strong></article>)}</section><button className="secondary-button" disabled={busy || locked} onClick={() => setQuote(null)}>تعديل الأبناء والباقات</button></>}
      {cashSessionId && quote && <CashFamilyConfirmation key={cashHash} sessionId={cashSessionId} body={{ guardianId: family.id, quoteHash: cashHash, lines: Object.entries(selection).map(([sportEnrollmentId, subscriptionPlanId]) => ({ sportEnrollmentId, subscriptionPlanId })) }} amount={quote.totalAmount} currency={quote.currency} onLocked={() => setLocked(true)} />}
      <aside className={styles.notice}>{cashSessionId ? "راجع الإجمالي ثم أكد استلام النقدية. لا تحصيل قبل التأكيد، ولكل تسجيل إيصال مستقل داخل الوردية نفسها." : "هذه مراجعة سعر فقط؛ لم يُسجل تحصيل ولم تُمدد الاشتراكات. تحتفظ كل رياضة بباقة وتسجيل مستقلين."}</aside>
    </>}
  </section>;
}
