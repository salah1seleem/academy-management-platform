"use client";

import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { AdminDataTable, EmptyState, ErrorState, ListSearch, LoadingState, MetricCard, PageHeader } from "../components/dashboard-shell";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { csrfRequest } from "./dashboard-api";
import { usePayrollManage, usePayrollPermission } from "./payroll-access";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";

type Salary = { employeeId: string; name: string; monthlySalary: number; salaryBasis?:string; contractDays: number; baseDue: number; deductions: number; additions: number; privateCompensation?: number; coachMembershipId?: string; netDue: number; currency: string; isPaid: boolean; statementId?:string; paidAmount?:number; remainingAmount?:number };
type Report = { currency: string; totalBase: number; totalDeductions: number; totalAdditions: number; totalNet: number; unpaid: number; items: Salary[] };
type Claim = { id: string; employeeId: string; name: string; amount: number | null; currency: string; reasonName: string; status: string; dueDate?: string; reviewNote?: string };
const states: Record<string, string> = { Pending: "بانتظار المراجعة", Approved: "مقبول", Rejected: "مرفوض" };
function useOperationKey() {
  const keys = useRef(new Map<string, string>());
  return (payload?: unknown): Record<string, string> => {
    if (payload === undefined) { keys.current.clear(); return {}; }
    const signature = JSON.stringify(payload);
    if (!keys.current.has(signature)) keys.current.set(signature, crypto.randomUUID());
    return { "Idempotency-Key": keys.current.get(signature)! };
  };
}

export function PayrollWorkspace() {
  const defaults = useDateFilterDefaults(), incoming = useSearchParams();
  const idempotency = useOperationKey();
  const [month, setMonth] = useState(incoming.get("month") && /^\d{4}-\d{2}$/.test(incoming.get("month")!) ? incoming.get("month")! : defaults.to.slice(0, 7)), [search, setSearch] = useState("");
  const [data, setData] = useState<Report | null>(null), [error, setError] = useState(""), [busy, setBusy] = useState("");
  const canManage = usePayrollPermission("payroll.pay");
  const branch = incoming.get("branchId") ?? "";
  const query = `/api/v1/manage/payroll/?month=${month}-01${branch ? `&branchId=${encodeURIComponent(branch)}` : ""}`;
  const requestVersion = useRef(0), currentQuery = useRef(query);
  const load = useCallback(() => {
    if (currentQuery.current !== query) return Promise.resolve();
    const version = ++requestVersion.current;
    return fetch(query).then(async response => {
      if (!response.ok) throw new Error();
      return response.json() as Promise<Report>;
    }).then(result => {
      if (version === requestVersion.current && currentQuery.current === query) { setData(result); setError(""); }
    }).catch(() => {
      if (version === requestVersion.current && currentQuery.current === query) setError("تعذر تحميل الرواتب أو لا توجد صلاحية.");
    });
  }, [query]);
  useEffect(() => { const requests = requestVersion; currentQuery.current = query; void load(); return () => { ++requests.current; }; }, [load, query]);
  async function pay(x: Salary) { if (busy || !window.confirm(`تأكيد صرف ${formatMoneyAr(x.netDue, x.currency)} إلى ${x.name}؟`)) return; setBusy(x.employeeId); try { const payload = { month: `${month}-01`, expectedNetDue: x.netDue }; await csrfRequest(`/api/v1/manage/payroll/${x.employeeId}/pay`, "POST", payload, idempotency({ employee: x.employeeId, ...payload })); await load(); } catch (e) { await load(); setError(e instanceof Error ? e.message : "تعذر تسجيل السداد."); } finally { setBusy(""); } }
  const rows = data?.items.filter(x => x.name.includes(search) && (!incoming.get("employeeId") || x.employeeId === incoming.get("employeeId"))) ?? [];
  // Display totals follow the same visible records as the table. Aggregate cents,
  // not floating-point money, without changing the server's settlement calculation.
  const total = (amount: (row: Salary) => number) => rows.reduce((sum, row) => sum + Math.round(amount(row) * 100), 0) / 100;
  return <><PageHeader title="الرواتب والمستحقات" context="فريق الأكاديمية" secondaryActions={[{ label: "الخصومات", href: "/dashboard/payroll/deductions" }, { label: "المبالغ الإضافية", href: "/dashboard/payroll/requests" }, { label: "الموظفون", href: "/dashboard/staff/employees" }]} />
    <div className="report-filters"><label>شهر الرواتب<input type="month" required value={month} onChange={e => { if (e.target.value) { setMonth(e.target.value); setData(null); } }} /></label><ListSearch label="البحث بالاسم" value={search} onChange={setSearch} /></div>
    {error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : data && <><section className="payroll-stats">
      <MetricCard label="الراتب الأساسي المستحق" value={formatMoneyAr(total(x => x.baseDue), data.currency)} /><Link href="/dashboard/payroll/deductions"><MetricCard label="الخصومات" value={formatMoneyAr(total(x => x.deductions), data.currency)} /></Link><Link href="/dashboard/payroll/requests"><MetricCard label="الإضافات المعتمدة" value={formatMoneyAr(total(x => x.additions), data.currency)} /></Link><MetricCard label="إجمالي المستحق" value={formatMoneyAr(total(x => x.netDue), data.currency)} detail={`لم يُدفع: ${formatMoneyAr(total(x => x.remainingAmount ?? (x.isPaid ? 0 : x.netDue)), data.currency)}`} />
    </section><section className="list-card">{!rows.length ? <EmptyState message="لا يوجد موظفون مطابقون." /> : <AdminDataTable label="المستحقات" rows={rows} rowKey={x => x.employeeId} columns={[
      { key: "name", header: "الموظف", primary: true, render: x => <>{x.name}<small>{x.contractDays} يوم عقد في الشهر</small></> },
      { key: "base", header: "الأساسي المستحق", render: x => <Link href={`/dashboard/staff/employees/${x.employeeId}/salary`}>{formatMoneyAr(x.baseDue, x.currency)}{x.salaryBasis==="LegacyUnverified"&&<small>مرجع قديم غير مؤرخ · مراجعة السجل</small>}</Link> },
      { key: "deduct", header: "الخصومات", render: x => <Link href={`/dashboard/payroll/deductions?employeeId=${x.employeeId}`}>{formatMoneyAr(x.deductions, x.currency)}</Link> },
      { key: "add", header: "الإضافات", render: x => formatMoneyAr(x.additions, x.currency) },
      { key: "private", header: "منها صافي عمولة الخاص", render: x => x.privateCompensation && x.coachMembershipId ? <Link href={`/dashboard/academy/coaches/${x.coachMembershipId}?tab=accounts`}>{formatMoneyAr(x.privateCompensation,x.currency)}<small>محتسبة بالصافي بالفعل · مراجعة المصدر</small></Link> : "—" },
      { key: "net", header: "الصافي", render: x => formatMoneyAr(x.netDue, x.currency) },
      { key: "paid", header: "السداد", render: x => x.statementId ? <><span>{x.isPaid?"تم الدفع":x.paidAmount?"صرف جزئي":"لم يُدفع"}</span><small>المدفوع {formatMoneyAr(x.paidAmount??0,x.currency)} · المتبقي {formatMoneyAr(x.remainingAmount??x.netDue,x.currency)}</small></> : x.isPaid ? "تم الدفع" : canManage ? <button className="primary-action" disabled={Boolean(busy) || x.netDue < 0 || !x.contractDays} onClick={() => void pay(x)}>{busy === x.employeeId ? "جارٍ الحفظ…" : "تأكيد الدفع"}</button> : "لم يُدفع" },
      { key:"statement",header:"الكشف",render:x=><Link href={`/dashboard/payroll/${x.employeeId}/statement?month=${month}-01`}>الكشف والتفاصيل</Link> },
      { key:"account",header:"حساب الموظف",render:x=><Link href={`/dashboard/payroll/${x.employeeId}/account`}>الأشهر السابقة والصرف</Link> },
    ]} />}</section></>}</>;
}

export function EmployeeDeductions() {
  const canManage = usePayrollManage();
  const defaults = useDateFilterDefaults(), incoming = useSearchParams();
  const idempotency = useOperationKey();
  const [employees, setEmployees] = useState<{ id: string; name: string }[]>([]), [rows, setRows] = useState<{ id: string; name: string; amount: number; currency: string; dueDate: string; reason?: string }[]>([]);
  const [error, setError] = useState(""), [busy, setBusy] = useState(false), [selected, setSelected] = useState(incoming.get("employeeId") ?? ""), [target, setTarget] = useState(incoming.get("employeeId") ?? "");
  const load = useCallback(() => fetch(`/api/v1/manage/payroll/adjustments?kind=Deduction${selected ? `&employeeId=${selected}` : ""}`).then(r => r.ok ? r.json() : Promise.reject()).then(setRows).catch(() => setError("تعذر تحميل خصومات الموظفين.")), [selected]);
  useEffect(() => { void load(); }, [load]); useEffect(() => { fetch("/api/v1/manage/employees/").then(r => r.ok ? r.json() : Promise.reject()).then(x => setEmployees(x.items)).catch(() => setError("تعذر تحميل الموظفين.")); }, []);
  async function save(e: FormEvent<HTMLFormElement>) { e.preventDefault(); if (busy) return; const form = e.currentTarget, f = new FormData(form); const payload = { employeeId: f.get("employee"), kind: "Deduction", amount: Number(f.get("amount")), dueDate: f.get("date"), reason: f.get("reason") }; setBusy(true); try { await csrfRequest("/api/v1/manage/payroll/adjustments", "POST", payload, idempotency(payload)); form.reset(); idempotency(); setError(""); await load(); } catch (e) { setError(e instanceof Error ? e.message : "تعذر حفظ الخصم."); } finally { setBusy(false); } }
  return <><PageHeader title="خصومات الموظفين" context="الرواتب" secondaryActions={[{ label: "عودة للرواتب", href: "/dashboard/payroll" }]} />{error && <ErrorState message={error} />}
    {canManage && <section className="detail-card"><h2>إضافة خصم</h2><form className="form-shell" onSubmit={e => void save(e)}><label>الموظف<select name="employee" required value={target} onChange={e => setTarget(e.target.value)}><option value="">اختر الموظف</option>{employees.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label>المبلغ<input name="amount" required type="number" min="0.01" step="0.01" /></label><label>تاريخ الاستحقاق<input name="date" required type="date" defaultValue={defaults.to} /></label><label>سبب الخصم<textarea name="reason" required maxLength={500} /></label><button className="primary-button" disabled={busy}>حفظ الخصم</button></form></section>}
    <section className="list-card"><label>فلتر الموظف<select value={selected} onChange={e => setSelected(e.target.value)}><option value="">كل الموظفين</option>{employees.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>{!rows.length ? <EmptyState message="لا توجد خصومات مطابقة." /> : <AdminDataTable label="الخصومات" rows={rows} rowKey={x => x.id} columns={[{ key: "name", header: "الموظف", primary: true, render: x => x.name }, { key: "amount", header: "المبلغ", render: x => formatMoneyAr(x.amount, x.currency) }, { key: "date", header: "تاريخ الاستحقاق", render: x => formatDateAr(x.dueDate) }, { key: "reason", header: "السبب", render: x => x.reason ?? "—" }]} />}</section></>;
}

export function PayrollRequests() {
  const canManage = usePayrollManage();
  const canReview = usePayrollPermission("payroll.addition.review");
  const defaults = useDateFilterDefaults(); const [rows, setRows] = useState<Claim[] | null>(null), [selected, setSelected] = useState<Claim | null>(null), [error, setError] = useState(""), [busy, setBusy] = useState(false), [accept, setAccept] = useState(true);
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { const node = dialog.current; if (selected && node && !node.open) node.showModal(); return () => node?.close(); }, [selected]);
  const load = useCallback(() => fetch("/api/v1/manage/payroll/requests").then(r => r.ok ? r.json() : Promise.reject()).then(setRows).catch(() => setError("تعذر تحميل المبالغ الإضافية.")), []); useEffect(() => { void load(); }, [load]);
  async function review(e: FormEvent<HTMLFormElement>) { e.preventDefault(); if (!selected || busy) return; const f = new FormData(e.currentTarget); const approving = f.get("decision") === "accept"; setBusy(true); try { await csrfRequest(`/api/v1/manage/payroll/requests/${selected.id}/review`, "POST", { accept: approving, amount: approving ? Number(f.get("amount")) : null, dueDate: approving ? f.get("date") || null : null, note: f.get("note") }); setSelected(null); await load(); } catch (e) { setError(e instanceof Error ? e.message : "تعذر مراجعة الطلب."); } finally { setBusy(false); } }
  return <><PageHeader title="المبالغ الإضافية" context="الرواتب" secondaryActions={[{ label: "الرواتب", href: "/dashboard/payroll" }]} />{error && <ErrorState message={error} />}{!rows && !error ? <LoadingState /> : <section className="list-card">{!rows?.length ? <EmptyState message="لا توجد طلبات مبالغ إضافية." /> : <AdminDataTable label="طلبات الإضافة" rows={rows} rowKey={x => x.id} columns={[{ key: "name", header: "الموظف", primary: true, render: x => x.name }, { key: "reason", header: "السبب", render: x => x.reasonName }, { key: "amount", header: "المبلغ", render: x => x.amount == null ? "يحدده المسؤول" : formatMoneyAr(x.amount, x.currency) }, { key: "state", header: "الحالة", render: x => states[x.status] }, { key: "action", header: "الإجراء", render: x => x.status === "Pending" && canReview ? <button onClick={() => { setAccept(true); setSelected(x); }}>مراجعة</button> : <span>{x.reviewNote ?? "—"}</span> }]} />}</section>}
    {selected && <dialog ref={dialog} aria-label={`مراجعة طلب ${selected.name}`} className="detail-card payroll-dialog" onCancel={e => { if (busy) e.preventDefault(); else setSelected(null); }}><h2>طلب {selected.name}</h2><p>{selected.reasonName} · يحدد المسؤول القيمة عند الاعتماد</p><form className="form-shell" onSubmit={e => void review(e)}><label>القرار<select name="decision" defaultValue="accept" onChange={e => setAccept(e.target.value === "accept")}><option value="accept">قبول</option><option value="reject">رفض</option></select></label>{accept && <label>المبلغ المعتمد<input name="amount" type="number" min="0.01" max="100000000" step="0.01" required /></label>}<label>تاريخ الاستحقاق<input name="date" type="date" defaultValue={defaults.to} required={accept} disabled={!accept} /></label><label>ملاحظة (اختياري)<textarea name="note" maxLength={500} /></label><div className="form-actions"><button className="primary-button" disabled={busy}>حفظ القرار</button><button type="button" disabled={busy} onClick={() => { setSelected(null); setAccept(true); }}>إلغاء</button></div></form></dialog>}
    {canManage && <PayrollReasons />}
  </>;
}

function PayrollReasons() {
  const [rows, setRows] = useState<{ id: string; name: string }[]>([]), [message, setMessage] = useState(""), [busy, setBusy] = useState(false);
  const load = useCallback(() => fetch("/api/v1/manage/payroll/reasons").then(r => r.ok ? r.json() : Promise.reject()).then(setRows).catch(() => setMessage("تعذر تحميل أسباب الإضافة.")), []); useEffect(() => { void load(); }, [load]);
  async function save(e: FormEvent<HTMLFormElement>) { e.preventDefault(); if (busy) return; const form = e.currentTarget; setBusy(true); try { await csrfRequest("/api/v1/manage/payroll/reasons", "POST", { name: new FormData(form).get("name") }); form.reset(); setMessage(""); await load(); } catch (e) { setMessage(e instanceof Error ? e.message : "تعذر حفظ السبب."); } finally { setBusy(false); } }
  return <section className="detail-card"><h2>أسباب المبالغ الإضافية</h2><p>{rows.map(x => x.name).join(" · ")}</p><form className="form-shell" onSubmit={e => void save(e)}><label>سبب جديد<input name="name" required maxLength={100} placeholder="ساعة إضافية، حضور مباراة…" /></label><button disabled={busy}>إضافة السبب</button></form>{message && <p role="status">{message}</p>}</section>;
}
