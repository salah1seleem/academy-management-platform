"use client";
import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { formatDateAr, formatDateTimeAr, formatMoneyAr } from "../lib/formatters";
import { useStaffAction } from "./use-staff-action";
import { clearFinancialIntent, pendingFinancialIntent } from "../lib/pending-financial-intent";
type Balance = { id: string; player: string; branch: string; description: string; currency: string; charge: string; credits: string; allocated: string; outstanding: string; dueDate: string; overdue: boolean; source: string };
type Ledger = { asOf: string; currency: string; count: number; outstanding: string; overdue: string; notYetDue: string; overpaymentCredit: string; items: Balance[] };
type Option = { id: string; player: string; branch: string; sport: string };
const states = [{ id: "", label: "كل المطالبات" }, { id: "open", label: "قائمة" }, { id: "overdue", label: "متأخرة" }, { id: "future", label: "غير مستحقة بعد" }, { id: "settled", label: "مسددة" }, { id: "credit", label: "رصيد دائن" }];
function initialLedgerFilter(key: "state" | "branchId") {
  if (typeof window === "undefined") return "";
  const value = new URLSearchParams(window.location.search).get(key) ?? "";
  return key === "state" ? states.some(x => x.id === value) ? value : "" : /^[0-9a-f-]{36}$/i.test(value) ? value : "";
}
export function Receivables() {
  const canAdd = useStaffAction("finance", "add");
  const pendingLoad = useRef<AbortController | null>(null);
  const [data, setData] = useState<Ledger | null>(null), [state, setState] = useState(() => initialLedgerFilter("state")), [branchId, setBranchId] = useState(() => initialLedgerFilter("branchId")), [branches, setBranches] = useState<{ id: string; arabicName: string }[]>([]), [error, setError] = useState("");
  const [search, setSearch] = useState(""), [options, setOptions] = useState<Option[]>([]), [create, setCreate] = useState(false), [busy, setBusy] = useState(false);
  const load = useCallback(async () => { pendingLoad.current?.abort(); const controller = new AbortController(); pendingLoad.current = controller; setData(null); setError(""); const query = new URLSearchParams({ state }); if (branchId) query.set("branchId", branchId); const r = await fetch(`/api/v1/finance/receivables?${query}`, { cache: "no-store", signal: controller.signal }); if (!r.ok) throw new Error("تعذر تحميل المطالبات أو أنها خارج صلاحياتك."); const value = await r.json(); if (!controller.signal.aborted) setData(value); }, [state, branchId]);
  useEffect(() => { void Promise.resolve().then(load).catch(e => { if (e.name !== "AbortError") setError(e.message); }); return () => pendingLoad.current?.abort(); }, [load]);
  useEffect(() => { void fetch("/api/v1/manage/structure/options").then(r => r.ok ? r.json() : null).then(o => setBranches(o?.branches ?? [])).catch(() => setBranches([])); }, []);
  useEffect(() => { if (!create) return; const controller = new AbortController(); void fetch(`/api/v1/finance/receivables/options?search=${encodeURIComponent(search)}`, { signal: controller.signal }).then(async r => { if (!r.ok) throw new Error("تعذر البحث عن التسجيل."); setOptions((await r.json()).enrollments); }).catch(e => { if (e.name !== "AbortError") setError(e.message); }); return () => controller.abort(); }, [create, search]);
  async function submit(form: HTMLFormElement) {
    if (busy) return; setBusy(true); setError(""); const f = new FormData(form);
    const body = { sportEnrollmentId: f.get("enrollment"), planId: null, amount: f.get("amount"), dueDate: f.get("dueDate"), source: f.get("source"), description: f.get("description") };
    try { await csrfRequest("/api/v1/finance/receivables", "POST", body, { "Idempotency-Key": await pendingFinancialIntent("charge", body) }); clearFinancialIntent("charge"); setCreate(false); await load(); } catch (e) { setError(e instanceof Error ? e.message : "تعذر تثبيت المطالبة."); } finally { setBusy(false); }
  }
  return <><PageHeader title="المطالبات والأرصدة" context="المالية" description="الرصيد مستند إلى مطالبات مثبتة وحركات تخصيص. انتهاء الاشتراك وحده ليس دينًا، وفرصة التجديد ليست تحصيلًا." />{canAdd && <div className="form-actions"><button className="primary-button" onClick={() => setCreate(x => !x)}>إضافة مطالبة</button></div>}
    {error && <ErrorState message={error} />}
    {create && <section className="detail-card"><h2>مطالبة موثقة — لا تُفعّل اشتراكًا</h2><form className="form-shell" onSubmit={e => { e.preventDefault(); void submit(e.currentTarget); }}>
      <label>البحث عن اللاعب<input value={search} onChange={e => setSearch(e.target.value)} placeholder="الاسم أو كود اللاعب" /></label>
      <label>تسجيل اللاعب<select name="enrollment" required><option value="">اختر تسجيلًا</option>{options.map(x => <option key={x.id} value={x.id}>{x.player} · {x.sport} · {x.branch}</option>)}</select></label>
      <label>{data?.currency === "EGP" ? "المبلغ بالجنيه" : `المبلغ ${data?.currency ?? ""}`}<input name="amount" type="number" min="0.01" max="999999999" step="0.01" required /></label><label>تاريخ الاستحقاق<input name="dueDate" type="date" required /></label>
      <label>المصدر<select name="source"><option value="ManualCharge">مطالبة معتمدة</option><option value="OpeningBalance">رصيد افتتاحي موثق — ليس تحصيلًا اليوم</option></select></label>
      <label>الوصف<input name="description" maxLength={400} required /></label><div className="form-actions"><button className="primary-button" disabled={busy}>تثبيت المطالبة</button><button type="button" className="secondary-button" onClick={() => setCreate(false)}>إلغاء</button></div>
    </form></section>}
    {!data ? !error && <LoadingState /> : <>
      <div className="receivable-metrics">{[{ value: data.outstanding, label: "الرصيد القائم", state: "open", tone: "blue" }, { value: data.overdue, label: "المتأخر المستحق", state: "overdue", tone: "rose" }, { value: data.notYetDue, label: "غير مستحق بعد", state: "future", tone: "gold" }, { value: data.overpaymentCredit, label: "رصيد دائن للأسرة", state: "credit", tone: "green" }].map(x => <button key={x.state} className={`receivable-metric ${x.tone}`} onClick={() => setState(x.state)}><span>{x.label}</span><strong>{formatMoneyAr(x.value, data.currency)}</strong><small>عرض التفاصيل ←</small></button>)}</div>
      <section className="list-card"><div className="filter-toolbar"><label>حالة الرصيد<select value={state} onChange={e => setState(e.target.value)}>{states.map(s => <option key={s.id} value={s.id}>{s.label}</option>)}</select></label><label>الفرع<select value={branchId} onChange={e => setBranchId(e.target.value)}><option value="">كل الفروع</option>{branches.map(b => <option key={b.id} value={b.id}>{b.arabicName}</option>)}{branchId && !branches.some(b => b.id === branchId) && <option value={branchId}>الفرع المحدد</option>}</select></label><button className="secondary-button" onClick={() => { setState(""); setBranchId(""); }}>مسح الفلتر</button><a className="secondary-button" href={`/api/v1/finance/receivables/export?state=${state}&branchId=${branchId}&asOf=${encodeURIComponent(data.asOf)}`}>تصدير CSV بنفس تاريخ الرصيد</a></div><p className="muted">رصيد لحظي حتى {formatDateTimeAr(data.asOf)} · {data.count} مطالبة. لا يشمل توقعات التجديد أو مدفوعات غير مؤكدة.</p>
      {data.items.length === 0 ? <EmptyState message="لا توجد مطالبات في هذا النطاق." /> : <AdminDataTable label="المطالبات المالية" rows={data.items} rowKey={r => r.id} columns={[
        { key: "player", header: "اللاعب", primary: true, render: r => <><b>{r.player}</b><small>{r.description}</small></> }, { key: "branch", header: "الفرع", render: r => r.branch },
        { key: "due", header: "الاستحقاق", render: r => formatDateAr(r.dueDate) }, { key: "amount", header: "المطالبة", render: r => formatMoneyAr(r.charge, r.currency) }, { key: "balance", header: "القائم", render: r => <b className={r.overdue ? "text-danger" : ""}>{formatMoneyAr(r.outstanding, r.currency)}</b> },
        { key: "actions", header: "الإجراءات", render: r => <Link className="secondary-button" href={`/dashboard/finance/receivables/${r.id}`}>التفاصيل والحركات</Link> },
      ]} />}</section>
    </>}
  </>;
}
type Document = { id: string; description: string; amount: string; currency: string; dueDate: string; planNameSnapshot?: string; planPriceSnapshot?: string; source: string };
type Movement = { id: string; kind: string; amount: string; reason: string; createdAtUtc: string };
type CreditApplication = { id: string; sourceDocumentId: string; targetDocumentId: string; amount: string; reason: string; createdAtUtc: string };
export function ReceivableDetail({ id }: { id: string }) {
  const canAdd = useStaffAction("finance", "add");
  const router = useRouter(); const [data, setData] = useState<{ document: Document; movements: Movement[]; canUseCredit?: boolean; creditApplications?: CreditApplication[] } | null>(null), [collections, setCollections] = useState<{ id: string; available: string; confirmedAtUtc: string }[]>([]), [error, setError] = useState(""), [kind, setKind] = useState("CreditNote"), [busy, setBusy] = useState(false);
  useEffect(() => { let active = true; void Promise.all([fetch(`/api/v1/finance/receivables/${id}`), fetch(`/api/v1/finance/receivables/${id}/collections`)]).then(async ([d, c]) => { if (!d.ok || !c.ok) throw new Error("تعذر تحميل المطالبة أو أنها خارج نطاقك."); const values = await Promise.all([d.json(), c.json()]); if (active) { setData(values[0]); setCollections(values[1]); } }).catch(e => setError(e.message)); return () => { active = false; }; }, [id]);
  async function save(form: HTMLFormElement) { if (busy) return; setBusy(true); const f = new FormData(form); const body = { kind, amount: f.get("amount"), collectionId: kind === "PaymentAllocation" ? f.get("collection") : null, reason: f.get("reason") }; const scope = `movement:${id}`; try { await csrfRequest(`/api/v1/finance/receivables/${id}/movements`, "POST", body, { "Idempotency-Key": await pendingFinancialIntent(scope, body) }); clearFinancialIntent(scope); router.push("/dashboard/finance/receivables"); } catch (e) { setError(e instanceof Error ? e.message : "تعذر تثبيت الحركة."); } finally { setBusy(false); } }
  return <><PageHeader title="تفاصيل المطالبة" context="المالية / المطالبات" action={{ label: "العودة للمطالبات", href: "/dashboard/finance/receivables", tone: "secondary" }} />{error && <ErrorState message={error} />}{!data ? !error && <LoadingState /> : <>
    <section className="detail-card"><h2>{data.document.description}</h2><strong>{formatMoneyAr(data.document.amount, data.document.currency)}</strong><p>الاستحقاق: {formatDateAr(data.document.dueDate)}</p>{data.document.planNameSnapshot && <p>لقطة الباقة: {data.document.planNameSnapshot} · {formatMoneyAr(data.document.planPriceSnapshot, data.document.currency)}</p>}<p className="notice">مستند مثبت لا يُعدل ولا يُحذف. الإشعار الدائن يصحح المطالبة؛ تخصيص تحصيل قائم لا يسجل قبضًا جديدًا. الاسترداد لا ينشئ دينًا تلقائيًا.</p></section>
    {data.canUseCredit && <div className="form-actions"><Link className="primary-button" href={`/dashboard/finance/receivables/${id}/credit`}>استخدام الرصيد الدائن</Link></div>}
    {canAdd && <section className="detail-card"><h2>إضافة حركة موثقة</h2><form className="form-shell" onSubmit={e => { e.preventDefault(); void save(e.currentTarget); }}><label>نوع الحركة<select value={kind} onChange={e => setKind(e.target.value)}><option value="CreditNote">إشعار دائن لتخفيض المطالبة</option><option value="PaymentAllocation">تخصيص من تحصيل مؤكد</option></select></label>
      {kind === "PaymentAllocation" && <label>التحصيل المتاح<select name="collection" required><option value="">اختر تحصيلًا</option>{collections.map(c => <option key={c.id} value={c.id}>{formatDateTimeAr(c.confirmedAtUtc)} · متاح {formatMoneyAr(c.available, data.document.currency)}</option>)}</select></label>}
      <label>المبلغ<input name="amount" type="number" min="0.01" max="999999999" step="0.01" required /></label><label>سبب الحركة<input name="reason" maxLength={400} required /></label><button className="primary-button" disabled={busy}>تثبيت الحركة</button></form></section>}
    <section className="detail-card"><h2>التاريخ المالي</h2>{data.movements.length === 0 ? <EmptyState message="لم تسجل حركات على المطالبة." /> : data.movements.map(m => <article className="payment-queue-row" key={m.id}><div><b>{m.kind === "CreditNote" ? "إشعار دائن" : "تخصيص تحصيل"}</b><small>{m.reason} · {formatDateTimeAr(m.createdAtUtc)}</small></div><strong>{formatMoneyAr(m.amount, data.document.currency)}</strong></article>)}{data.creditApplications?.map(a => <article className="payment-queue-row" key={a.id}><div><b>{a.sourceDocumentId === id ? "رصيد دائن مستخدم" : "تسوية برصيد دائن"}</b><small>{a.reason} · {formatDateTimeAr(a.createdAtUtc)}</small><Link href={`/dashboard/finance/receivables/${a.sourceDocumentId === id ? a.targetDocumentId : a.sourceDocumentId}`}>عرض المطالبة المرتبطة</Link></div><strong>{formatMoneyAr(a.amount, data.document.currency)}</strong></article>)}</section>
  </>}</>;
}
export function GuardianReceivables() {
  const [data, setData] = useState<Ledger | null>(null), [failed, setFailed] = useState(false);
  useEffect(() => { let active = true; void fetch("/api/v1/guardian/receivables", { cache: "no-store" }).then(async r => { if (!r.ok) throw new Error(); const value = await r.json(); if (!Array.isArray(value.items) || typeof value.outstanding !== "string" || typeof value.overpaymentCredit !== "string") throw new Error(); if (active) setData(value); }).catch(() => { if (active) setFailed(true); }); return () => { active = false; }; }, []);
  if (failed) return <p role="status">تعذر تحميل الأرصدة المالية الآن.</p>;
  if (!data || Number(data.outstanding) === 0 && Number(data.overpaymentCredit) === 0) return null;
  return <section className="profile-section"><h2>أرصدة الأسرة</h2><p>قائم {formatMoneyAr(data.outstanding, data.currency)} · متأخر {formatMoneyAr(data.overdue, data.currency)}</p>{data.items.filter(x => Number(x.outstanding) !== 0).map(x => <article key={x.id}><b>{x.player}</b><p>{x.description} · {formatMoneyAr(x.outstanding, x.currency)} · {formatDateAr(x.dueDate)}</p></article>)}</section>;
}
