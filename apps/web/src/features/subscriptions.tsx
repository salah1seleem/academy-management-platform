"use client";
import Link from "next/link";
import { FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { AdminDataTable, ConfirmationDialog, EmptyState, ErrorState, FilterToolbar, LoadingState, PageHeader, RowActions, SearchInput, StatusBadge } from "../components/dashboard-shell";
import { GuardianShell } from "../components/guardian-shell";
import { csrfRequest } from "./dashboard-api";
import { formatDateAr, formatDateTimeAr, formatMoneyAr, planTypeLabel, statusLabel, workflowStatusLabels } from "../lib/formatters";

type Row = Record<string, string | number | null> & { id: string };
const listConfig: Record<string, { title: string; endpoint: string }> = {
  current: { title: "الاشتراكات الحالية", endpoint: "/api/v1/subscriptions/periods?state=active" },
  expiring: { title: "الاشتراكات التي تنتهي قريباً", endpoint: "/api/v1/subscriptions/periods?state=expiring" },
  expired: { title: "الاشتراكات المنتهية", endpoint: "/api/v1/subscriptions/periods?state=expired" },
  renewals: { title: "طلبات التجديد", endpoint: "/api/v1/subscriptions/renewals" },
  payments: { title: "طلبات الدفع", endpoint: "/api/v1/subscriptions/payments" },
};
const money = formatMoneyAr;
export function SubscriptionOperationalList({ kind }: { kind: keyof typeof listConfig }) {
  const config = listConfig[kind]; const [rows, setRows] = useState<Row[]>([]); const [search, setSearch] = useState(""); const [loading, setLoading] = useState(true); const [error, setError] = useState("");
  useEffect(() => { fetch(config.endpoint).then(async r => { if (!r.ok) throw new Error(); setRows(await r.json() as Row[]); }).catch(() => setError("تعذر تحميل البيانات.")).finally(() => setLoading(false)); }, [config.endpoint]);
  const filtered = useMemo(() => rows.filter(row => JSON.stringify(row).toLocaleLowerCase("ar").includes(search.toLocaleLowerCase("ar"))), [rows, search]);
  return <><PageHeader title={config.title} context={`الاشتراكات / ${config.title}`} description="متابعة دورة الاشتراك والتحصيل من السجلات المحفوظة." />{(kind === "expiring" || kind === "expired") && <p className="subscription-warning">{kind === "expired" ? "اشتراكات منتهية — راجع سجل اللاعب واستخدم التجديد لمتابعة التدريب." : "تنتهي قريبًا — تواصل مع ولي الأمر قبل انتهاء الاشتراك."}</p>}<FilterToolbar resultCount={filtered.length} onReset={() => setSearch("")}><SearchInput value={search} onChange={setSearch} placeholder="بحث باللاعب أو الرياضة أو المرجع" /></FilterToolbar><section className="list-card">{error && <ErrorState message={error} />}{loading ? <LoadingState /> : !filtered.length ? <EmptyState /> : <AdminDataTable label={config.title} rows={filtered} rowKey={row => row.id} columns={[
    { key: "player", header: "اللاعب / المرجع", width: "minmax(170px, 1.3fr)", primary: true, render: row => String(row.player ?? row.reference ?? "سجل") },
    { key: "context", header: "الرياضة والباقة", width: "minmax(180px, 1.3fr)", render: row => <>{String(row.sport ?? "—")}<small>{String(row.plan ?? "—")}</small></> },
    { key: "period", header: "الفترة / المبلغ", width: "minmax(155px, 1fr)", render: row => row.amount != null ? money(row.amount, row.currency) : <>{formatDateAr(String(row.startDate ?? ""))}<small>{formatDateAr(String(row.endDate ?? ""))}</small></> },
    { key: "status", header: "الحالة", width: "135px", render: row => <StatusBadge status={String(row.status ?? "Pending")} label={workflowStatusLabels[String(row.status)]} /> },
    { key: "actions", header: "الإجراءات", width: "150px", render: row => kind === "current" || kind === "expiring" || kind === "expired" ? <RowActions actions={[{ label: "تجديد", href: `/dashboard/subscriptions/renew/new?enrollmentId=${String(row.sportEnrollmentId ?? "")}` }, { label: "عرض وإدارة", href: `/dashboard/subscriptions/periods/${row.id}` }]} /> : kind === "renewals" ? <RowActions actions={[{ label: "عرض وإدارة الخصم", href: `/dashboard/subscriptions/renewals/${row.id}` }]} /> : kind === "payments" ? <RowActions actions={[{ label: "عرض الدفع", href: `/dashboard/subscriptions/payments/${row.id}` }]} /> : <span>—</span> },
  ]} />}</section></>;
}

type DiscountAudit = { id: string; previousDiscountType?: string; previousDiscountValue?: number; newDiscountType?: string; newDiscountValue?: number; previousFinalAmount: number; newFinalAmount: number; reason: string; performedBy: string; createdAtUtc: string };
type RenewalDiscount = { id: string; player: string; sport: string; plan: string; originalAmount: number; discountType?: "Percentage" | "FixedAmount"; discountValue?: number; discountAmount: number; finalAmount: number; currency: string; status: string; paymentStatus: string; version: number; canModify: boolean; adjustments: DiscountAudit[] };

export function RenewalDiscountDetail({ id }: { id: string }) {
  const [data, setData] = useState<RenewalDiscount | null>(null); const [loading, setLoading] = useState(true); const [error, setError] = useState(""); const [editing, setEditing] = useState(false); const [type, setType] = useState<"Percentage" | "FixedAmount">("Percentage"); const [value, setValue] = useState(""); const [reason, setReason] = useState(""); const [busy, setBusy] = useState(false);
  const load = () => fetch(`/api/v1/subscriptions/renewals/${id}`).then(async response => { if (!response.ok) throw new Error(); setData(await response.json() as RenewalDiscount); }).catch(() => setError("تعذر تحميل طلب التجديد.")).finally(() => setLoading(false));
  useEffect(() => { fetch(`/api/v1/subscriptions/renewals/${id}`).then(async response => { if (!response.ok) throw new Error(); setData(await response.json() as RenewalDiscount); }).catch(() => setError("تعذر تحميل طلب التجديد.")).finally(() => setLoading(false)); }, [id]);
  function openForm() { if (!data) return; setType(data.discountType ?? "Percentage"); setValue(data.discountValue?.toString() ?? ""); setReason(""); setEditing(true); setError(""); }
  const numericValue = Number(value || 0); const previewDiscount = !data ? 0 : type === "Percentage" ? Math.round(data.originalAmount * numericValue) / 100 : numericValue; const previewFinal = data ? Math.max(0, data.originalAmount - previewDiscount) : 0;
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); if (!data) return; setBusy(true); setError(""); try { await csrfRequest(`/api/v1/subscriptions/renewals/${data.id}/discount`, "POST", { type, value: numericValue, reason, expectedVersion: data.version }, { "Idempotency-Key": crypto.randomUUID() }); setEditing(false); await load(); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر حفظ الخصم."); } finally { setBusy(false); } }
  async function remove() { if (!data || !reason.trim()) { setError("اكتب سبب إزالة الخصم أولًا."); return; } setBusy(true); setError(""); try { await csrfRequest(`/api/v1/subscriptions/renewals/${data.id}/discount/remove`, "POST", { reason, expectedVersion: data.version }, { "Idempotency-Key": crypto.randomUUID() }); setEditing(false); await load(); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر إزالة الخصم."); } finally { setBusy(false); } }
  if (loading) return <LoadingState />; if (!data) return <ErrorState message={error || "طلب التجديد غير موجود."} />;
  return <><PageHeader title="تفاصيل طلب التجديد" context="الاشتراكات / طلبات التجديد" />{error && <ErrorState message={error} />}<section className="detail-card subscription-period-detail"><div className="period-summary"><div><span>اللاعب</span><b>{data.player}</b></div><div><span>الرياضة والباقة</span><b>{data.sport} · {data.plan}</b></div><div><span>حالة التجديد</span><StatusBadge status={data.status} label={workflowStatusLabels[data.status]} /></div><div><span>حالة الدفع</span><StatusBadge status={data.paymentStatus} label={workflowStatusLabels[data.paymentStatus]} /></div><div><span>السعر الأصلي</span><b>{money(data.originalAmount, data.currency)}</b></div><div><span>الخصم</span><b>{data.discountAmount ? `${data.discountType === "Percentage" ? `${data.discountValue}%` : money(data.discountValue, data.currency)} (-${money(data.discountAmount, data.currency)})` : "لا يوجد"}</b></div><div><span>الإجمالي بعد الخصم</span><b>{money(data.finalAmount, data.currency)}</b></div></div>{data.canModify && <button className="primary-button" onClick={openForm}>{data.discountType ? "تعديل الخصم" : "تطبيق خصم"}</button>}{editing && <form className="adjustment-form" onSubmit={event => void submit(event)}><h2>{data.discountType ? "تعديل الخصم" : "تطبيق خصم"}</h2><label>نوع الخصم<select aria-label="نوع الخصم" value={type} onChange={event => setType(event.target.value as "Percentage" | "FixedAmount")}><option value="Percentage">نسبة مئوية</option><option value="FixedAmount">مبلغ ثابت</option></select></label><label>القيمة<input aria-label="قيمة الخصم" type="number" min="0.01" max={type === "Percentage" ? 100 : data.originalAmount} step="0.01" required value={value} onChange={event => setValue(event.target.value)} /></label><label>السبب<textarea aria-label="سبب الخصم" required maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></label><div className="adjustment-preview"><span>السعر الأصلي: <b>{money(data.originalAmount, data.currency)}</b></span><span>قيمة الخصم: <b>{money(previewDiscount, data.currency)}</b></span><span>الإجمالي المتوقع: <b>{money(previewFinal, data.currency)}</b></span><small>الحساب النهائي المعتمد يتم على الخادم.</small></div><div className="form-actions"><button className="primary-button" disabled={busy}>{busy ? "جارٍ الحفظ…" : "حفظ الخصم"}</button>{data.discountType && <button className="danger-button" type="button" disabled={busy || !reason.trim()} onClick={() => void remove()}>إزالة الخصم بهذا السبب</button>}<button type="button" onClick={() => setEditing(false)}>إلغاء</button></div></form>}<section className="adjustment-history"><h2>سجل الخصم</h2>{data.adjustments.length === 0 ? <EmptyState message="لا توجد تعديلات خصم." /> : data.adjustments.map(item => <article key={item.id}><div><b>{item.newDiscountType ? "تطبيق/تعديل خصم" : "إزالة الخصم"}</b><small>{item.performedBy} · {new Date(item.createdAtUtc).toLocaleString("ar-EG")}</small></div><p>{item.reason}</p><small>{money(item.previousFinalAmount, data.currency)} ← {money(item.newFinalAmount, data.currency)}</small></article>)}</section></section></>;
}
export function CollectionList() { const [data, setData] = useState<{ total: number; items: Row[] } | null>(null); useEffect(() => { fetch("/api/v1/subscriptions/collections").then(r => r.json()).then(setData); }, []); return <><PageHeader title="التحصيلات" context="الاشتراكات / التحصيلات" />{!data ? <LoadingState /> : <section className="list-card"><p className="metric">إجمالي التحصيل المؤكد: {money(data.total, "EGP")}</p><AdminDataTable label="التحصيلات المؤكدة" rows={data.items} rowKey={row => row.id} columns={[
  { key: "receipt", header: "الإيصال واللاعب", width: "minmax(190px, 1.4fr)", primary: true, render: row => <>{String(row.receiptNumber)}<small>{String(row.player)}</small></> },
  { key: "sport", header: "الرياضة", render: row => String(row.sport ?? "—") },
  { key: "amount", header: "المبلغ", render: row => money(row.amount, row.currency) },
  { key: "provider", header: "طريقة التحصيل", render: row => row.provider === "Internal" ? "بوابة الاختبار الداخلية" : String(row.provider ?? "—") },
  { key: "actions", header: "الإجراءات", width: "125px", render: row => <RowActions actions={[{ label: "عرض الإيصال", href: `/dashboard/subscriptions/receipts/${row.receiptId}` }]} /> },
]} /></section>}</> }

type Plan = { id: string; arabicName: string; sport: string; sportId: string; planType: string; price: number; currency: string; durationDays?: number; sessionCount?: number; isActive: boolean };
export function PlanList() {
  const [plans, setPlans] = useState<Plan[]>([]); const [search, setSearch] = useState(""); const [loading, setLoading] = useState(true); const [error, setError] = useState("");
  const load = () => fetch("/api/v1/subscriptions/plans").then(async response => { if (!response.ok) throw new Error(); setPlans(await response.json() as Plan[]); }).catch(() => setError("تعذر تحميل الباقات.")).finally(() => setLoading(false));
  useEffect(() => { void load(); }, []);
  async function status(plan: Plan) { try { await csrfRequest(`/api/v1/subscriptions/plans/${plan.id}/status`, "PUT", { isActive: !plan.isActive }); await load(); } catch { setError("تعذر تحديث حالة الباقة."); } }
  const visible = plans.filter(x => `${x.arabicName} ${x.sport}`.includes(search));
  return <><PageHeader title="الباقات" context="الاشتراكات / الباقات" description="باقات التدريب بالمدة أو الحصص، بنفس قواعد الاشتراك والتحصيل المعتمدة." action={{ label: "إضافة باقة", href: "/dashboard/subscriptions/plans/new" }} /><FilterToolbar resultCount={visible.length} onReset={() => setSearch("")}><SearchInput value={search} onChange={setSearch} placeholder="بحث باسم الباقة أو الرياضة" /></FilterToolbar>
    {error && <ErrorState message={error} />}{loading ? <LoadingState /> : visible.length === 0 ? <EmptyState /> : <section className="sports-plan-grid" aria-label="باقات الاشتراك">{visible.map(plan => <article key={plan.id} className={`sports-plan-card ${plan.isActive ? "" : "inactive-record"}`}>
      <div className="plan-card-top"><span className="team-chip">{plan.sport}</span><StatusBadge status={plan.isActive ? "Active" : "Inactive"} /></div>
      <h2>{plan.arabicName}</h2><span className="plan-kind">{planTypeLabel(plan.planType)}</span><strong className="plan-price">{money(plan.price, plan.currency)}</strong>
      <div className="plan-entitlements">{plan.durationDays && <span>{plan.durationDays} يوم</span>}{plan.sessionCount && <span>{plan.sessionCount} حصة</span>}</div>
      <RowActions actions={[{ label: "عرض", href: `/dashboard/subscriptions/plans/${plan.id}` }, { label: "تعديل", href: `/dashboard/subscriptions/plans/${plan.id}/edit` }, { label: plan.isActive ? "إيقاف" : "تفعيل", onClick: () => void status(plan) }]} />
    </article>)}</section>}
  </>;
}

export function PlanForm({ id, readOnly = false }: { id?: string; readOnly?: boolean }) { const router = useRouter(); const [sports, setSports] = useState<{ id: string; arabicName: string }[]>([]); const [plan, setPlan] = useState<Partial<Plan>>({ planType: "Duration", currency: "EGP" }); const [error, setError] = useState(""); useEffect(() => { fetch("/api/v1/manage/structure/options").then(r => r.json()).then(x => setSports(x.sports)); if (id) fetch(`/api/v1/subscriptions/plans/${id}`).then(r => r.json()).then(setPlan); }, [id]); async function submit(e: FormEvent<HTMLFormElement>) { e.preventDefault(); const f = new FormData(e.currentTarget); const type = String(f.get("planType")); const body = { arabicName: f.get("arabicName"), englishName: f.get("englishName") || null, sportId: f.get("sportId"), planType: type === "Duration" ? 1 : type === "Sessions" ? 2 : 3, price: Number(f.get("price")), durationDays: f.get("durationDays") ? Number(f.get("durationDays")) : null, sessionCount: f.get("sessionCount") ? Number(f.get("sessionCount")) : null, displayOrder: Number(f.get("displayOrder") ?? 0) }; try { await csrfRequest(id ? `/api/v1/subscriptions/plans/${id}` : "/api/v1/subscriptions/plans", id ? "PUT" : "POST", body); router.push("/dashboard/subscriptions/plans"); } catch (e) { setError(e instanceof Error ? e.message : "تعذر الحفظ"); } } return <><PageHeader title={readOnly ? "عرض الباقة" : id ? "تعديل الباقة" : "إضافة باقة"} context="الاشتراكات / الباقات" />{readOnly ? <section className="detail-card"><h2>{plan.arabicName}</h2><p>{plan.sport} · {planTypeLabel(plan.planType)} · {money(plan.price, plan.currency)}</p><Link className="primary-action" href={`/dashboard/subscriptions/plans/${id}/edit`}>تعديل</Link></section> : <form className="form-shell" onSubmit={e => void submit(e)}><label>الاسم العربي<input name="arabicName" required defaultValue={plan.arabicName ?? ""} /></label><label>الاسم الإنجليزي — اختياري<input name="englishName" defaultValue="" /></label><label>الرياضة<select name="sportId" required value={plan.sportId ?? ""} onChange={e => setPlan({ ...plan, sportId: e.target.value })}><option value="">اختر</option>{sports.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label><label>النوع<select name="planType" value={plan.planType ?? "Duration"} onChange={e => setPlan({ ...plan, planType: e.target.value })}><option value="Duration">بالمدة</option><option value="Sessions">بالحصص</option><option value="Combined">بالمدة والحصص</option></select></label><label>السعر بالجنيه<input name="price" type="number" min="0.01" step="0.01" required defaultValue={plan.price ?? ""} /></label><label>عدد الأيام<input name="durationDays" type="number" min="1" defaultValue={plan.durationDays ?? ""} /></label><label>عدد الحصص<input name="sessionCount" type="number" min="1" defaultValue={plan.sessionCount ?? ""} /></label><input name="displayOrder" type="hidden" value="0" />{error && <ErrorState message={error} />}<button className="primary-button">حفظ الباقة</button></form>}</>; }

type Adjustment = { id: string; type: string; effectiveDate: string; daysDelta?: number; oldEndDate?: string; newEndDate?: string; reason: string; performedBy: string; performedAtUtc: string };
type PeriodDetails = { id: string; sportEnrollmentId: string; player: string; playerCode: string; sport: string; branch: string; group: string; plan: string; planType: "Duration" | "Sessions" | "Combined"; startDate: string; endDate?: string; frozenFromDate?: string; initialSessions?: number; remainingSessions?: number; status: string; version: number; priceSnapshot: number; currencySnapshot: string; receiptId?: string; adjustments: Adjustment[] };
type ActionKind = "freeze" | "resume" | "add" | "deduct" | "cancel";
const adjustmentLabels: Record<string, string> = { FreezeStarted: "بدء التجميد", FreezeEnded: "استئناف الاشتراك", DaysAdded: "إضافة أيام", DaysDeducted: "خصم أيام", Cancelled: "إلغاء الاشتراك" };

function addCalendarDays(value: string, days: number) { const date = new Date(`${value}T00:00:00Z`); date.setUTCDate(date.getUTCDate() + days); return date.toISOString().slice(0, 10); }
function dayDifference(from: string, to: string) { return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000); }

export function SubscriptionPeriodDetail({ id }: { id: string }) {
  const [data, setData] = useState<PeriodDetails | null>(null); const [loading, setLoading] = useState(true); const [error, setError] = useState(""); const [mode, setMode] = useState<ActionKind | null>(null); const [reason, setReason] = useState(""); const [date, setDate] = useState(""); const [days, setDays] = useState(""); const [confirming, setConfirming] = useState(false); const [busy, setBusy] = useState(false);
  const load = () => { setLoading(true); setError(""); return fetch(`/api/v1/subscriptions/periods/${id}`).then(async response => { if (!response.ok) throw new Error(); setData(await response.json() as PeriodDetails); }).catch(() => setError("تعذر تحميل تفاصيل الاشتراك.")).finally(() => setLoading(false)); };
  useEffect(() => { fetch(`/api/v1/subscriptions/periods/${id}`).then(async response => { if (!response.ok) throw new Error(); setData(await response.json() as PeriodDetails); }).catch(() => setError("تعذر تحميل تفاصيل الاشتراك.")).finally(() => setLoading(false)); }, [id]);
  function choose(next: ActionKind) { setMode(next); setReason(""); setDays(""); setDate(next === "resume" ? data?.frozenFromDate ?? "" : ""); setError(""); }
  const payload = () => mode === "add" || mode === "deduct" ? { direction: mode === "add" ? 1 : 2, days: Number(days), reason, expectedVersion: data!.version } : { effectiveDate: date, reason, expectedVersion: data!.version };
  async function execute() { if (!data || !mode) return; setBusy(true); setError(""); const endpoint = mode === "add" || mode === "deduct" ? "days" : mode; try { await csrfRequest(`/api/v1/subscriptions/periods/${data.id}/${endpoint}`, "POST", payload(), { "Idempotency-Key": crypto.randomUUID() }); setMode(null); setConfirming(false); await load(); } catch (caught) { setConfirming(false); setError(caught instanceof Error ? caught.message : "تعذر تنفيذ التعديل."); } finally { setBusy(false); } }
  function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); if (mode === "freeze" || mode === "deduct" || mode === "cancel") setConfirming(true); else void execute(); }
  if (loading) return <LoadingState />; if (!data) return <ErrorState message={error || "الاشتراك غير موجود."} />;
  const timed = data.planType !== "Sessions" && Boolean(data.endDate); const canCancel = data.status !== "Cancelled"; const preview = mode === "resume" && data.frozenFromDate && data.endDate && date && dayDifference(data.frozenFromDate, date) > 0 ? addCalendarDays(data.endDate, dayDifference(data.frozenFromDate, date)) : null;
  const dialog = mode === "cancel" ? { title: "تأكيد إلغاء الاشتراك", message: "سيتم إلغاء صلاحية هذه الفترة فقط. لن يتم حذف التحصيل أو الإيصال ولن يتم رد الأموال تلقائيًا.", label: "تأكيد الإلغاء" } : mode === "freeze" ? { title: "تأكيد تجميد الاشتراك", message: "سيُوقف استهلاك الحصص المؤهلة خلال التجميد، ولن يتغير التحصيل أو الإيصال.", label: "تأكيد التجميد" } : { title: "تأكيد خصم الأيام", message: `سيتم خصم ${days || 0} يوم من تاريخ نهاية هذه الفترة دون تغيير أي تحصيل مالي.`, label: "تأكيد الخصم" };
  return <><PageHeader title="تفاصيل الاشتراك" context="الاشتراكات / الاشتراكات الحالية" />{error && <ErrorState message={error} />}<section className="detail-card subscription-period-detail"><div className="period-summary"><div><span>اللاعب</span><b>{data.player} · {data.playerCode}</b></div><div><span>الرياضة والمجموعة</span><b>{data.sport} · {data.group}</b></div><div><span>الباقة</span><b>{data.plan} · {planTypeLabel(data.planType)}</b></div><div><span>الحالة</span><b className={`status-pill ${data.status.toLowerCase()}`}>{statusLabel(data.status)}</b></div><div><span>الفترة</span><b>{formatDateAr(data.startDate)} — {data.endDate ? formatDateAr(data.endDate) : "بالحصص"}</b></div><div><span>الرصيد</span><b>{data.remainingSessions == null ? "غير منطبق" : `${data.remainingSessions} حصة`}</b></div>{data.frozenFromDate && <div><span>مجمد منذ</span><b>{formatDateAr(data.frozenFromDate)}</b></div>}</div>{data.receiptId && <Link href={`/dashboard/subscriptions/receipts/${data.receiptId}`}>عرض الإيصال المحفوظ</Link>}<div className="adjustment-actions"><Link className="primary-action" href={`/dashboard/subscriptions/renew/new?enrollmentId=${data.sportEnrollmentId}`}>تجديد الاشتراك</Link>{data.status === "Active" && timed && <button onClick={() => choose("freeze")}>تجميد</button>}{data.status === "Frozen" && <button onClick={() => choose("resume")}>استئناف</button>}{timed && data.status !== "Cancelled" && <><button onClick={() => choose("add")}>إضافة أيام</button><button onClick={() => choose("deduct")}>خصم أيام</button></>}{canCancel && <button className="danger-button" onClick={() => choose("cancel")}>إلغاء الاشتراك</button>}</div>{data.planType === "Sessions" && <p className="form-hint">الباقة بالحصة فقط لا تحتوي مدة زمنية قابلة للتجميد أو تعديل الأيام.</p>}{mode && <form className="adjustment-form" onSubmit={submit}><h2>{mode === "freeze" ? "تجميد الاشتراك" : mode === "resume" ? "استئناف الاشتراك" : mode === "add" ? "إضافة أيام" : mode === "deduct" ? "خصم أيام" : "إلغاء الاشتراك"}</h2>{mode === "add" || mode === "deduct" ? <label>عدد الأيام<input aria-label="عدد الأيام" type="number" min="1" max="365" required value={days} onChange={event => setDays(event.target.value)} /></label> : <label>{mode === "resume" ? "تاريخ الاستئناف" : mode === "freeze" ? "تاريخ بدء التجميد" : "تاريخ سريان الإلغاء"}<input aria-label="التاريخ الفعلي" type="date" required value={date} onChange={event => setDate(event.target.value)} /></label>}<label>سبب التعديل<textarea aria-label="سبب التعديل" required maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></label>{preview && <p className="adjustment-preview">النهاية المتوقعة بعد الاستئناف: <b>{formatDateAr(preview)}</b> — الحساب النهائي من الخادم.</p>}<div className="form-actions"><button className="primary-button" disabled={busy}>{busy ? "جارٍ الحفظ…" : "متابعة"}</button><button type="button" onClick={() => setMode(null)}>إلغاء</button></div></form>}<section className="adjustment-history"><h2>سجل التعديلات</h2>{data.adjustments.length === 0 ? <EmptyState message="لا توجد تعديلات تاريخية على هذه الفترة." /> : data.adjustments.map(item => <article key={item.id}><div><b>{adjustmentLabels[item.type] ?? "تعديل اشتراك"}</b><small>{formatDateAr(item.effectiveDate)} · {item.performedBy}</small></div><p>{item.reason}</p>{item.daysDelta != null && <span>{item.daysDelta > 0 ? `+${item.daysDelta}` : item.daysDelta} يوم</span>}{item.oldEndDate && item.newEndDate && <small>{formatDateAr(item.oldEndDate)} ← {formatDateAr(item.newEndDate)}</small>}</article>)}</section></section><ConfirmationDialog open={confirming} title={dialog.title} message={dialog.message} confirmLabel={dialog.label} busy={busy} onConfirm={() => void execute()} onCancel={() => setConfirming(false)} /></>;
}

type GuardianPeriod = { id: string; plan: string; sport: string; startDate: string; endDate?: string; frozenFromDate?: string; remainingSessions?: number; status: string; adjustments: { type: string; effectiveDate: string; daysDelta?: number; newEndDate?: string }[] };
export function GuardianSubscriptionStatus({ id }: { id: string }) {
  const [data, setData] = useState<GuardianPeriod | null>(null); const [error, setError] = useState("");
  useEffect(() => { fetch(`/api/v1/guardian/subscriptions/periods/${id}/adjustments`).then(async response => { if (!response.ok) throw new Error(); setData(await response.json() as GuardianPeriod); }).catch(() => setError("تعذر تحميل حالة الاشتراك.")); }, [id]);
  return <GuardianShell><Link className="back-link" href="/guardian">العودة لملف الطفل</Link>{error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : data && <><header className="guardian-page-header"><span>{data.sport}</span><h1>حالة الاشتراك</h1><p>{data.plan}</p></header><section className="profile-section"><div className="period-summary"><div><span>الحالة الحالية</span><b className={`status-pill ${data.status.toLowerCase()}`}>{statusLabel(data.status)}</b></div><div><span>الفترة</span><b>{formatDateAr(data.startDate)} — {data.endDate ? formatDateAr(data.endDate) : "بالحصص"}</b></div>{data.frozenFromDate && <div><span>مجمد منذ</span><b>{formatDateAr(data.frozenFromDate)}</b></div>}{data.remainingSessions != null && <div><span>الحصص المتبقية</span><b>{data.remainingSessions}</b></div>}</div></section><section className="profile-section adjustment-history"><h2>آخر تحديثات الاشتراك</h2>{data.adjustments.length === 0 ? <EmptyState message="لا توجد تعديلات على هذه الفترة." /> : data.adjustments.map((item, index) => <article key={`${item.type}-${item.effectiveDate}-${index}`}><div><b>{adjustmentLabels[item.type] ?? "تعديل اشتراك"}</b><small>{formatDateAr(item.effectiveDate)}</small></div>{item.type === "FreezeStarted" && <p>الاشتراك مجمد.</p>}{item.type === "FreezeEnded" && <p>تم استئناف الاشتراك{item.newEndDate ? ` حتى ${formatDateAr(item.newEndDate)}` : ""}.</p>}{item.type === "DaysAdded" && <p>تم تمديد الاشتراك{item.newEndDate ? ` حتى ${formatDateAr(item.newEndDate)}` : ""}.</p>}{item.type === "DaysDeducted" && <p>تم تعديل نهاية الاشتراك{item.newEndDate ? ` إلى ${formatDateAr(item.newEndDate)}` : ""}.</p>}{item.type === "Cancelled" && <p>الاشتراك ملغي.</p>}</article>)}</section></>}</GuardianShell>;
}

type AdminRenewalEnrollmentRow = {
  id: string;
  playerId: string;
  playerCode: string;
  player: string;
  sport: string;
  branch: string;
  group: string;
  guardianPhone?: string;
  period?: { id: string; plan: string; startDate: string; endDate?: string; status: string };
};
type AdminRenewalPlan = { id: string; arabicName: string; planType: string; price: number; currency: string; durationDays?: number; sessionCount?: number };
type AdminRenewalContext = {
  enrollment: Omit<AdminRenewalEnrollmentRow, "period" | "guardianPhone"> & { sportId: string };
  current?: { id: string; plan: string; startDate: string; endDate?: string; status: string };
  previewStart: string;
  plans: AdminRenewalPlan[];
};

export function AdminRenewalFlow() {
  const router = useRouter();
  const params = useSearchParams();
  const initialEnrollment = params.get("enrollmentId") ?? "";
  const [search, setSearch] = useState("");
  const [rows, setRows] = useState<AdminRenewalEnrollmentRow[]>([]);
  const [selectedEnrollment, setSelectedEnrollment] = useState(initialEnrollment);
  const [context, setContext] = useState<AdminRenewalContext | null>(null);
  const [selectedPlan, setSelectedPlan] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    fetch(`/api/v1/subscriptions/admin-renewals/enrollments?search=${encodeURIComponent(search)}`)
      .then(async response => { if (!response.ok) throw new Error(); setRows(await response.json() as AdminRenewalEnrollmentRow[]); })
      .catch(() => setError("تعذر تحميل تسجيلات اللاعبين."))
      .finally(() => setLoading(false));
  }, [search]);
  useEffect(() => {
    if (!selectedEnrollment) return;
    fetch(`/api/v1/subscriptions/admin-renewals/enrollments/${selectedEnrollment}`)
      .then(async response => { if (!response.ok) throw new Error(); return response.json() as Promise<AdminRenewalContext>; })
      .then(value => { setContext(value); setSelectedPlan(""); })
      .catch(() => setError("تعذر تحميل بيانات التسجيل أو أنه خارج نطاق الأكاديمية."));
  }, [selectedEnrollment]);

  const plan = context?.plans.find(item => item.id === selectedPlan);
  async function create() {
    if (!context || !plan) return;
    setBusy(true); setError("");
    try {
      const response = await csrfRequest("/api/v1/subscriptions/admin-renewals", "POST", {
        sportEnrollmentId: context.enrollment.id,
        subscriptionPlanId: plan.id,
      }, { "Idempotency-Key": crypto.randomUUID() });
      const result = await response.json() as { paymentId: string };
      router.push(`/dashboard/subscriptions/payments/${result.paymentId}`);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "تعذر إنشاء طلب التجديد.");
    } finally { setBusy(false); }
  }

  return <>
    <PageHeader title="تجديد اشتراك" context="الاشتراكات / تجديد لاعب حالي" description="أنشئ طلب تجديد ودفع فقط؛ لا يتم التحصيل أو تمديد الاشتراك قبل تأكيد الدفع التجريبي." />
    {error && <ErrorState message={error} />}
    <FilterToolbar resultCount={rows.length} onReset={() => setSearch("")}>
      <SearchInput value={search} onChange={setSearch} placeholder="اسم اللاعب أو الكود أو هاتف ولي الأمر" />
    </FilterToolbar>
    <section className="list-card">
      <h2>اختر تسجيل اللاعب</h2>
      {loading ? <LoadingState /> : rows.length === 0 ? <EmptyState message="لا توجد تسجيلات مطابقة." /> : <div className="data-list">
        {rows.map(row => <article key={row.id} className={selectedEnrollment === row.id ? "selected-record" : ""}>
          <div><strong>{row.player} · {row.playerCode}</strong><small>{row.sport} · {row.branch} · {row.group}</small>{row.period && <small>{row.period.plan} — {statusLabel(row.period.status)} — حتى {formatDateAr(row.period.endDate)}</small>}</div>
          <button type="button" onClick={() => setSelectedEnrollment(row.id)}>اختيار</button>
        </article>)}
      </div>}
    </section>
    {context && <section className="detail-card subscription-period-detail">
      <h2>مراجعة التجديد</h2>
      <div className="period-summary">
        <div><span>اللاعب</span><b>{context.enrollment.player} · {context.enrollment.playerCode}</b></div>
        <div><span>السياق</span><b>{context.enrollment.sport} · {context.enrollment.branch} · {context.enrollment.group}</b></div>
        <div><span>الاشتراك الحالي</span><b>{context.current ? `${context.current.plan} — ${statusLabel(context.current.status)}` : "لا توجد فترة اشتراك سابقة"}</b></div>
        <div><span>نهاية الاشتراك الحالية</span><b>{formatDateAr(context.current?.endDate)}</b></div>
        <div><span>بداية الفترة المتوقعة</span><b>{formatDateAr(context.previewStart)}</b></div>
      </div>
      <h3>اختر باقة متوافقة</h3>
      <div className="choice-list">{context.plans.map(item => <label key={item.id}><input type="radio" name="admin-renewal-plan" checked={selectedPlan === item.id} onChange={() => setSelectedPlan(item.id)} /><strong>{item.arabicName}</strong><span>{planTypeLabel(item.planType)} · {formatMoneyAr(item.price, item.currency)}</span></label>)}</div>
      {plan && <div className="adjustment-preview"><span>السعر الأصلي: <b>{formatMoneyAr(plan.price, plan.currency)}</b></span><span>الخصم عند الإنشاء: <b>لا يوجد</b></span><span>الإجمالي: <b>{formatMoneyAr(plan.price, plan.currency)}</b></span><small>يمكن تطبيق خصم بصلاحية مستقلة بعد إنشاء الطلب وقبل تأكيد الدفع.</small></div>}
      <div className="form-actions"><button className="primary-button" disabled={!plan || busy} onClick={() => void create()}>{busy ? "جارٍ الإنشاء…" : "إنشاء طلب التجديد والدفع"}</button><Link href="/dashboard/subscriptions/current">إلغاء</Link></div>
    </section>}
  </>;
}

type AdminPaymentData = { id: string; renewalId: string; providerReference: string; amount: number; currency: string; status: string; renewalStatus: string; player: string; playerCode: string; sport: string; plan: string; originalAmount: number; discountType?: string; discountValue?: number; discountAmount: number; finalAmount: number; createdAtUtc: string; confirmedAtUtc?: string; currentPaymentId?: string; isCurrent: boolean; receiptId?: string };
export function AdminPaymentDetail({ id }: { id: string }) {
  const router = useRouter(); const [data, setData] = useState<AdminPaymentData | null>(null); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const load = useCallback(() => fetch(`/api/v1/subscriptions/payments/${id}`).then(async response => { if (!response.ok) throw new Error(); setData(await response.json() as AdminPaymentData); }).catch(() => setError("تعذر تحميل طلب الدفع.")), [id]);
  useEffect(() => { void load(); }, [load]);
  async function simulate(outcome: "Success" | "Failed") { setBusy(true); setError(""); try { await csrfRequest(`/api/v1/subscriptions/payments/${id}/simulate`, "POST", { outcome }); await load(); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تنفيذ المحاكاة."); } finally { setBusy(false); } }
  async function retry() { setBusy(true); setError(""); try { const response = await csrfRequest(`/api/v1/subscriptions/payments/${id}/retry`, "POST", {}, { "Idempotency-Key": crypto.randomUUID() }); const result = await response.json() as { paymentId: string }; router.push(`/dashboard/subscriptions/payments/${result.paymentId}`); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر إعادة المحاولة."); } finally { setBusy(false); } }
  return <><PageHeader title="طلب الدفع" context="الاشتراكات / الدفع التجريبي" description="بيئة Demo فقط — لا توجد أموال حقيقية." />{error && <ErrorState message={error} />}{!data ? <LoadingState /> : <section className="detail-card subscription-period-detail"><div className="period-summary"><div><span>اللاعب</span><b>{data.player} · {data.playerCode}</b></div><div><span>الرياضة والباقة</span><b>{data.sport} · {data.plan}</b></div><div><span>حالة الدفع</span><StatusBadge status={data.status} label={statusLabel(data.status)} /></div><div><span>حالة التجديد</span><StatusBadge status={data.renewalStatus} label={statusLabel(data.renewalStatus)} /></div><div><span>السعر الأصلي</span><b>{formatMoneyAr(data.originalAmount, data.currency)}</b></div><div><span>الخصم</span><b>{data.discountAmount ? formatMoneyAr(data.discountAmount, data.currency) : "لا يوجد"}</b></div><div><span>الإجمالي</span><b>{formatMoneyAr(data.finalAmount, data.currency)}</b></div><div><span>أُنشئ في</span><b>{formatDateTimeAr(data.createdAtUtc)}</b></div></div>{!data.isCurrent && data.currentPaymentId && <Link className="primary-action" href={`/dashboard/subscriptions/payments/${data.currentPaymentId}`}>فتح طلب الدفع الحالي</Link>}<div className="adjustment-actions"><Link href={`/dashboard/subscriptions/renewals/${data.renewalId}`}>إدارة الخصم</Link>{data.isCurrent && data.status === "Pending" && <><button className="primary-button" disabled={busy} onClick={() => void simulate("Success")}>محاكاة نجاح الدفع</button><button disabled={busy} onClick={() => void simulate("Failed")}>محاكاة فشل الدفع</button></>}{data.isCurrent && ["Failed", "Cancelled", "Expired"].includes(data.status) && <button className="primary-button" disabled={busy} onClick={() => void retry()}>إعادة محاولة الدفع</button>}{data.receiptId && <Link href={`/dashboard/subscriptions/receipts/${data.receiptId}`}>عرض الإيصال</Link>}</div></section>}</>;
}
