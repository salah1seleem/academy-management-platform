"use client";

import { FormEvent, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { AdminDataTable, EmptyState, ErrorState, ListSearch, LoadingState, PageHeader, RowActions } from "../components/dashboard-shell";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { csrfRequest, uploadAsset } from "./dashboard-api";
import { usePayrollManage } from "./payroll-access";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";

type Employee = { id: string; name: string; phone: string; jobTitle: string; branchId: string; monthlySalary: number; currency: string; contractStart: string; contractEnd?: string; membershipId?: string; photoAssetId?: string; photoUrl?: string; isActive: boolean };
type Options = { items: Employee[]; branches: { id: string; name: string }[]; accounts: { id: string; name: string; phone: string }[] };

export function EmployeeList() {
  const canManage = usePayrollManage();
  const [data, setData] = useState<Options | null>(null), [error, setError] = useState(""), [search, setSearch] = useState("");
  useEffect(() => { fetch("/api/v1/manage/employees/").then(async r => { if (!r.ok) throw new Error(); setData(await r.json()); }).catch(() => setError("تعذر تحميل الموظفين أو لا توجد صلاحية.")); }, []);
  const rows = data?.items.filter(x => `${x.name} ${x.phone} ${x.jobTitle}`.includes(search)) ?? [];
  return <><PageHeader title="الموظفون" context="فريق الأكاديمية" action={canManage ? { label: "إضافة موظف", href: "/dashboard/staff/employees/new" } : undefined} secondaryActions={[{ label: "الحسابات والصلاحيات", href: "/dashboard/staff" }, { label: "الرواتب", href: "/dashboard/payroll" }]} />
    {error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : <section className="list-card"><ListSearch label="البحث بالاسم أو الهاتف" value={search} onChange={setSearch} />
      {!rows.length ? <EmptyState message="لا يوجد موظفون مطابقون." /> : <AdminDataTable label="الموظفون" rows={rows} rowKey={x => x.id} columns={[
        { key: "name", header: "الموظف", primary: true, render: x => <><b>{x.name}</b><small dir="ltr">{x.phone}</small></> },
        { key: "job", header: "الوظيفة / الفرع", render: x => <>{x.jobTitle}<small>{data?.branches.find(b => b.id === x.branchId)?.name}</small></> },
        { key: "salary", header: "الراتب الشهري", render: x => formatMoneyAr(x.monthlySalary, x.currency) },
        { key: "contract", header: "العقد", render: x => x.contractEnd ? `${formatDateAr(x.contractStart)} — ${formatDateAr(x.contractEnd)}` : "دائم" },
        { key: "account", header: "الحساب", render: x => x.membershipId ? "مرتبط بحساب مستقل" : "بدون حساب دخول" },
        { key: "actions", header: "الإجراء", render: x => <RowActions actions={[...(canManage ? [{ label: "تعديل", href: `/dashboard/staff/employees/${x.id}` }] : []), { label: "المستحقات", href: `/dashboard/payroll?employeeId=${x.id}` }]} /> },
      ]} />}</section>}</>;
}

export function EmployeeForm({ id }: { id?: string }) {
  const router = useRouter(), defaults = useDateFilterDefaults();
  const [options, setOptions] = useState<Options | null>(null), [employee, setEmployee] = useState<Employee | null>(null);
  const [fixed, setFixed] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState("");
  useEffect(() => { const abort = new AbortController(); Promise.all([fetch("/api/v1/manage/employees/", { signal: abort.signal }).then(r => r.ok ? r.json() : Promise.reject()), id ? fetch(`/api/v1/manage/employees/${id}`, { signal: abort.signal }).then(r => r.ok ? r.json() : Promise.reject()) : Promise.resolve(null)]).then(([o, e]) => { setOptions(o); setEmployee(e); setFixed(Boolean(e?.contractEnd)); }).catch(e => { if (e.name !== "AbortError") setError("تعذر تحميل بيانات الموظف."); }); return () => abort.abort(); }, [id]);
  async function save(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); if (busy) return; const form = new FormData(e.currentTarget); setBusy(true); setError("");
    try {
      const file = form.get("photo") as File; const asset = file?.size ? await uploadAsset(file, "StaffPhoto") : null;
      await csrfRequest(`/api/v1/manage/employees/${id ?? ""}`, id ? "PUT" : "POST", { name: form.get("name"), phone: form.get("phone"), jobTitle: form.get("job"), branchId: form.get("branch"), monthlySalary: Number(form.get("salary")), contractStart: form.get("start"), contractEnd: fixed ? form.get("end") : null, membershipId: form.get("account") || null, photoAssetId: asset?.id ?? employee?.photoAssetId ?? null, isActive: form.get("active") === "on" });
      router.push("/dashboard/staff/employees");
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر حفظ الموظف."); } finally { setBusy(false); }
  }
  return <><PageHeader title={id ? "تعديل الموظف" : "إضافة موظف"} context="فريق الأكاديمية" secondaryActions={[{ label: "عودة للموظفين", href: "/dashboard/staff/employees" }]} />{error && <ErrorState message={error} />}
    {!options && !error ? <LoadingState /> : options && <form className="form-shell detail-card" onSubmit={e => void save(e)}>
      <label>الاسم<input name="name" required maxLength={160} defaultValue={employee?.name} /></label>
      <label>رقم الهاتف<input name="phone" type="tel" required dir="ltr" maxLength={32} defaultValue={employee?.phone} /></label>
      <label>الوظيفة<input name="job" required maxLength={100} defaultValue={employee?.jobTitle} placeholder="مدرب، إداري، عامل…" /></label>
      <label>الفرع<select name="branch" required defaultValue={employee?.branchId ?? ""}><option value="">اختر الفرع</option>{options.branches.map(b => <option value={b.id} key={b.id}>{b.name}</option>)}</select></label>
      <label>الراتب الشهري<input name="salary" type="number" required min="0" max="100000000" step="0.01" defaultValue={employee?.monthlySalary ?? 0} /></label>
      <label>الصورة (اختياري)<input type="file" name="photo" accept="image/png,image/jpeg,image/webp" /></label>
      <fieldset className="contract-choice"><legend>نوع العقد</legend><label><input type="radio" name="contract" checked={!fixed} onChange={() => setFixed(false)} />دائم</label><label><input type="radio" name="contract" checked={fixed} onChange={() => setFixed(true)} />محدد المدة</label></fieldset>
      <label>تاريخ الانضمام<input name="start" type="date" required defaultValue={employee?.contractStart ?? defaults.to} /></label>
      {fixed && <label>تاريخ انتهاء العقد<input name="end" type="date" required defaultValue={employee?.contractEnd} /></label>}
      <label>ربط حساب موجود (اختياري)<select name="account" defaultValue={employee?.membershipId ?? ""}><option value="">بدون حساب دخول</option>{employee?.membershipId && <option value={employee.membershipId}>الحساب الحالي للموظف</option>}{options.accounts.map(a => <option key={a.id} value={a.id}>{a.name}</option>)}</select></label>
      <label className="check-inline"><input name="active" type="checkbox" defaultChecked={employee?.isActive ?? true} />موظف فعال</label>
      <div className="form-actions"><button className="primary-button" disabled={busy}>{busy ? "جارٍ الحفظ…" : id ? "حفظ التعديلات" : "إنشاء الموظف"}</button><Link className="secondary-button" href="/dashboard/staff/employees">إلغاء</Link></div>
    </form>}</>;
}
