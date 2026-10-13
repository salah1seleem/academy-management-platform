"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { GuardianShell } from "../components/guardian-shell";
import { formatMoneyAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { clearFinancialIntent, pendingFinancialIntent } from "../lib/pending-financial-intent";
import styles from "./family-renewal-quote.module.css";

const root = "/api/v1/guardian/subscriptions/family";
type Plan = { id: string; name: string; amount: string; currency: string };
type Enrollment = { id: string; playerId: string; player: string; sport: string; branch: string; plans: Plan[] };
type Quote = { quoteHash: string; currency: string; pricingDate: string; originalAmount: string; discountAmount: string; totalAmount: string; items: { sportEnrollmentId: string; subscriptionPlanId: string; player: string; sport: string; branch: string; plan: string; originalAmount: string; discountAmount: string; finalAmount: string; discountReason?: string; currency: string }[] };

export function FamilyRenewalQuote() {
  const router = useRouter();
  const [uncertain, setUncertain] = useState(false);
  const [options, setOptions] = useState<Enrollment[] | null>(null), [selection, setSelection] = useState<Record<string, string>>({}), [quote, setQuote] = useState<Quote | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState("");
  useEffect(() => { let active = true; const abort = new AbortController(); void fetch(`${root}/options`, { cache: "no-store", signal: abort.signal }).then(async r => { if (!r.ok) throw new Error(); return r.json(); }).then(d => { if (active) setOptions(d.items); }).catch(e => { if (active && e.name !== "AbortError") setError("تعذر تحميل تسجيلات أبنائك. حدّث الصفحة أو سجل الدخول مجددًا."); }); return () => { active = false; abort.abort(); }; }, []);
  function choose(id: string, plan: string | null) { setQuote(null); setError(""); setSelection(old => { const next = { ...old }; if (plan === null) delete next[id]; else next[id] = plan; return next; }); }
  async function preview() {
    if (busy) return; setBusy(true); setError(""); setQuote(null);
    try { setQuote(await (await csrfRequest(`${root}/quote`, "POST", { lines: Object.entries(selection).map(([sportEnrollmentId, subscriptionPlanId]) => ({ sportEnrollmentId, subscriptionPlanId })) }, {}, { announce: false })).json()); }
    catch (e) { setError(e instanceof DashboardRequestError ? e.message : "تعذر الاتصال لمراجعة التجديد. لم يُنشأ طلب دفع؛ حاول مجددًا."); }
    finally { setBusy(false); }
  }
  const count = Object.keys(selection).length;
  async function create() {
    if (!quote || busy) return; setBusy(true); setError("");
    const payload = { lines: quote.items.map(({ sportEnrollmentId, subscriptionPlanId }) => ({ sportEnrollmentId, subscriptionPlanId })), quoteHash: quote.quoteHash };
    try {
      const idempotencyKey = await pendingFinancialIntent("family-renewal", payload);
      const saved = await (await csrfRequest(`${root}/requests`, "POST", { ...payload, idempotencyKey }, {}, { announce: false })).json() as { id: string };
      clearFinancialIntent("family-renewal"); router.push(`/guardian/family-renewals/${saved.id}`);
    } catch (e) {
      const known = e instanceof DashboardRequestError && e.status < 500;
      setUncertain(!known); setError(known ? e.message : "لم يصل تأكيد إنشاء الطلب. أعد المحاولة بنفس الاختيارات أو راجع طلبات الأسرة؛ لا تدفع مرة أخرى.");
    } finally { setBusy(false); }
  }
  return <GuardianShell><section className={styles.page}><Link className="back-link" href="/guardian">العودة للأبناء</Link><header><span className="eyebrow">تجديد الأسرة</span><h1>راجع تجديد أبنائك معًا</h1><p>اختر التسجيلات والباقات المناسبة لكل رياضة وفرع. يُحسب الخصم المؤهل والإجمالي على الخادم.</p></header>
    {error && <p className="error-message" role="alert">{error}</p>}{!options && !error && <p role="status">جارٍ تحميل تسجيلات الأبناء…</p>}{options?.length === 0 && <p>لا توجد تسجيلات متاحة لأبنائك.</p>}
    {!quote && <form onSubmit={e => { e.preventDefault(); void preview(); }}><fieldset className={styles.options} disabled={busy}><legend>التسجيلات المراد تجديدها</legend>{options?.map(e => <article key={e.id} className={`${styles.card} ${e.id in selection ? styles.selected : ""}`}><label className={styles.choice}><input type="checkbox" checked={e.id in selection} disabled={!e.plans.length} onChange={event => choose(e.id, event.target.checked ? "" : null)} aria-label={`تجديد ${e.player} · ${e.sport}`} /><span><strong>{e.player}</strong><small>{e.sport} · {e.branch}</small></span></label>{!e.plans.length ? <p>لا توجد باقة منشورة مناسبة. تواصل مع الأكاديمية.</p> : e.id in selection && <label className={styles.plan}>الباقة لـ{e.player} · {e.sport}<select required value={selection[e.id]} onChange={event => choose(e.id, event.target.value)}><option value="">اختر الباقة</option>{e.plans.map(p => <option key={p.id} value={p.id}>{p.name} · {formatMoneyAr(p.amount, p.currency)}</option>)}</select></label>}</article>)}</fieldset><button className="primary-button" disabled={busy || count === 0 || count > 20 || Object.values(selection).some(x => !x)}>مراجعة إجمالي {count} تسجيل</button></form>}
    {quote && <section className={styles.summary} aria-label="ملخص تجديد الأسرة"><h2>إجمالي التجديد</h2><div className={styles.totals}>{[["قبل الخصم", quote.originalAmount], ["الخصم المؤهل", quote.discountAmount], ["الإجمالي", quote.totalAmount]].map(([name, value]) => <div key={name}><small>{name}</small><strong>{formatMoneyAr(value, quote.currency)}</strong></div>)}</div>{quote.items.map(item => <article className={styles.line} key={item.sportEnrollmentId}><div><strong>{item.player}</strong><small>{item.sport} · {item.plan} · {item.branch}</small>{item.discountReason && <small>{item.discountReason}</small>}</div><strong>{formatMoneyAr(item.finalAmount, item.currency)}</strong></article>)}</section>}
    <aside className={styles.notice}>حتى هذه الخطوة لم يُنشأ طلب دفع ولم يُفعّل اشتراك. تابع لإنشاء طلب واحد للأسرة، ثم حوّل الإجمالي وارفع إثباتًا واحدًا. يبدأ التجديد بعد اعتماد الإدارة لكل التسجيلات معًا.</aside>
    {quote && <><button className="primary-button" disabled={busy} onClick={() => void create()}>{busy ? "جارٍ إنشاء الطلب…" : uncertain ? "إعادة تأكيد نفس الطلب" : "متابعة الدفع للأسرة"}</button><button className="secondary-button" disabled={busy || uncertain} onClick={() => setQuote(null)}>تعديل التسجيلات والباقات</button></>}
    <Link href="/guardian/family-renewals">طلبات تجديد الأسرة السابقة</Link>
  </section></GuardianShell>;
}
