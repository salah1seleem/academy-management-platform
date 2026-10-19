"use client";

import { FormEvent, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { AdminDataTable, EmptyState, ErrorState, ListSearch, LoadingState, PageHeader, RowActions } from "../components/dashboard-shell";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { csrfRequest, uploadAsset } from "./dashboard-api";
import { usePayrollManage, usePayrollPermission } from "./payroll-access";
import { formatMoneyAr } from "../lib/formatters";

type EmployeeFinancial={baseDue:number;deductions:number;additions:number;netDue:number};
type Employee = { financial?:EmployeeFinancial|null; id: string; name: string; phone: string; jobTitle: string; branchId: string; monthlySalary: number; currency: string; contractStart: string; contractEnd?: string; hasContractHistory?: boolean; membershipId?: string; photoAssetId?: string; photoUrl?: string; isActive: boolean };
type Options = { items: Employee[]; branches: { id: string; name: string }[]; accounts: { id: string; name: string; phone: string }[] };

export function EmployeeList() {
  const canManage=usePayrollManage(),canSalary=usePayrollPermission("payroll.salary.manage"),defaults=useDateFilterDefaults();
  const [month,setMonth]=useState(defaults.to.slice(0,7)),[data,setData]=useState<Options|null>(null),[error,setError]=useState(""),[search,setSearch]=useState(""),[branch,setBranch]=useState(""),[sort,setSort]=useState("name");
  useEffect(()=>{const c=new AbortController();fetch(`/api/v1/manage/employees/?month=${month}-01`,{signal:c.signal,cache:"no-store"}).then(async r=>{if(!r.ok)throw new Error();return r.json();}).then(d=>{if(!c.signal.aborted){setData(d);setError("");}}).catch(()=>{if(!c.signal.aborted){setData(null);setError("تعذر تحميل الموظفين ومستحقات الشهر.");}});return()=>c.abort();},[month]);
  const rows=(data?.items??[]).filter(x=>`${x.name} ${x.phone} ${x.jobTitle}`.includes(search)&&(!branch||x.branchId===branch)).sort((a,b)=>sort==="net"?(b.financial?.netDue??-Infinity)-(a.financial?.netDue??-Infinity)||a.name.localeCompare(b.name,"ar"):a.name.localeCompare(b.name,"ar"));
  const amount=(x:Employee,key:keyof EmployeeFinancial)=>x.financial?formatMoneyAr(Number(x.financial[key]),x.currency):"لا كشف مؤهل لهذا الشهر";
  return <><PageHeader title="المدربون والموظفون" context="إدارة الأكاديمية" action={canManage&&canSalary?{label:"إضافة موظف",href:"/dashboard/staff/employees/new"}:undefined} secondaryActions={[{label:"إسناد مجموعات المدربين",href:"/dashboard/academy/coaches"}]}/>
    <div className="report-filters"><ListSearch label="البحث بالاسم أو الهاتف" value={search} onChange={setSearch}/><label>شهر المستحقات<input type="month" required value={month} onChange={e=>{if(e.target.value){setMonth(e.target.value);setData(null);}}}/></label><label>الفرع<select value={branch} onChange={e=>setBranch(e.target.value)}><option value="">كل الفروع</option>{data?.branches.map(b=><option key={b.id} value={b.id}>{b.name}</option>)}</select></label><label>الترتيب<select value={sort} onChange={e=>setSort(e.target.value)}><option value="name">الاسم</option><option value="net">الأعلى مستحقًا</option></select></label><button onClick={()=>{setSearch("");setBranch("");setSort("name");}}>مسح البحث والفلاتر</button></div>
    {error&&<ErrorState message={error}/>} {!data&&!error?<LoadingState/>:<section className="list-card">{!rows.length?<EmptyState message="لا يوجد موظفون مطابقون."/>:<AdminDataTable label="الموظفون" rows={rows} rowKey={x=>x.id} columns={[
      {key:"name",header:"الموظف",primary:true,render:x=><Link href={`/dashboard/staff/employees/${x.id}`}>{x.name}</Link>},
      {key:"phone",header:"رقم الهاتف",render:x=><bdi>{x.phone}</bdi>},{key:"job",header:"الوظيفة",render:x=>x.jobTitle},
      {key:"branch",header:"الفرع",render:x=>data?.branches.find(b=>b.id===x.branchId)?.name??"—"},
      {key:"base",header:"الأساسي الشهري",render:x=>formatMoneyAr(x.monthlySalary,x.currency)},
      {key:"add",header:"إضافات الشهر",render:x=>amount(x,"additions")},{key:"deduct",header:"خصومات الشهر",render:x=>amount(x,"deductions")},
      {key:"net",header:"مستحق الشهر",render:x=><strong>{amount(x,"netDue")}</strong>},
      {key:"actions",header:"الإجراء",render:x=><RowActions actions={[{label:"فتح الملف",href:`/dashboard/staff/employees/${x.id}?month=${month}`},...(canManage?[{label:"تعديل",href:`/dashboard/staff/employees/${x.id}/edit`}]:[]),{label:"المستحقات والصرف",href:`/dashboard/staff/employees/${x.id}?tab=payroll&month=${month}`} ]}/>}
    ]}/>}<small>الاستحقاق من نفس كشف الرواتب: الأساسي المستحق بحسب أيام العقد − الخصومات + الإضافات. عدم وجود كشف مؤهل ليس صفرًا.</small></section>}</>;
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
      await csrfRequest(`/api/v1/manage/employees/${id ?? ""}`, id ? "PUT" : "POST", { name: form.get("name"), phone: form.get("phone"), jobTitle: form.get("job"), branchId: form.get("branch"), monthlySalary: Number(form.get("salary")), contractStart: employee?.hasContractHistory ? employee.contractStart : form.get("start"), contractEnd: employee?.hasContractHistory ? employee.contractEnd ?? null : fixed ? form.get("end") : null, membershipId: form.get("account") || null, photoAssetId: asset?.id ?? employee?.photoAssetId ?? null, isActive: form.get("active") === "on" });
      router.push("/dashboard/staff/employees");
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر حفظ الموظف."); } finally { setBusy(false); }
  }
  return <><PageHeader title={id ? "تعديل الموظف" : "إضافة موظف"} context="فريق الأكاديمية" secondaryActions={[{ label: "عودة للموظفين", href: "/dashboard/staff/employees" }]} />{error && <ErrorState message={error} />}
    {!options && !error ? <LoadingState /> : options && <form className="form-shell detail-card" onSubmit={e => void save(e)}>
      <label>الاسم<input name="name" required maxLength={160} defaultValue={employee?.name} /></label>
      <label>رقم الهاتف<input name="phone" type="tel" required dir="ltr" maxLength={32} defaultValue={employee?.phone} /></label>
      <label>الوظيفة<input name="job" required maxLength={100} defaultValue={employee?.jobTitle} placeholder="مدرب، إداري، عامل…" /></label>
      <label>الفرع<select name="branch" required defaultValue={employee?.branchId ?? ""}><option value="">اختر الفرع</option>{options.branches.map(b => <option value={b.id} key={b.id}>{b.name}</option>)}</select></label>
      <label>الراتب الشهري<input aria-label="الراتب الشهري" name="salary" type="number" required readOnly={Boolean(id)} min="0" max="100000000" step="0.01" defaultValue={employee?.monthlySalary ?? 0} />{id&&<Link href={`/dashboard/staff/employees/${id}/salary`}>تغيير الراتب من السجل المؤرخ</Link>}</label>
      <label>الصورة (اختياري)<input type="file" name="photo" accept="image/png,image/jpeg,image/webp" /></label>
      {id && <Link href={`/dashboard/staff/employees/${id}/contract`}>تغيير مدة العقد من السجل المؤرخ</Link>}
      <fieldset className="contract-choice" disabled={employee?.hasContractHistory}><legend>نوع العقد</legend><label><input type="radio" name="contract" checked={!fixed} onChange={() => setFixed(false)} />دائم</label><label><input type="radio" name="contract" checked={fixed} onChange={() => setFixed(true)} />محدد المدة</label></fieldset>
      <label>تاريخ الانضمام<input name="start" type="date" required readOnly={employee?.hasContractHistory} defaultValue={employee?.contractStart ?? defaults.to} /></label>
      {fixed && <label>تاريخ انتهاء العقد<input name="end" type="date" required readOnly={employee?.hasContractHistory} defaultValue={employee?.contractEnd} /></label>}
      <label>ربط حساب موجود (اختياري)<select name="account" defaultValue={employee?.membershipId ?? ""}><option value="">بدون حساب دخول</option>{employee?.membershipId && <option value={employee.membershipId}>الحساب الحالي للموظف</option>}{options.accounts.map(a => <option key={a.id} value={a.id}>{a.name}</option>)}</select></label>
      <label className="check-inline"><input name="active" type="checkbox" defaultChecked={employee?.isActive ?? true} />موظف فعال</label>
      <div className="form-actions"><button className="primary-button" disabled={busy}>{busy ? "جارٍ الحفظ…" : id ? "حفظ التعديلات" : "إنشاء الموظف"}</button><Link className="secondary-button" href="/dashboard/staff/employees">إلغاء</Link></div>
    </form>}</>;
}
