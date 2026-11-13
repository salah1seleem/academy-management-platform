"use client";

import Link from "next/link";
import { ChevronRight } from "lucide-react";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge } from "../components/dashboard-shell";
import { egyptianStaffMobile } from "../lib/number-input";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { StaffAccessEditor } from "./staff-access";
import { StaffRoleTemplates } from "./staff-role-templates";
import { StaffAccessContexts } from "./staff-access-contexts";

type Role = "AcademyAdmin" | "Coach";
type Member = { id: string; name: string; email?: string | null; phoneNumber?: string | null; role: Role; jobTitle?: string | null; isActive: boolean };
type Directory = { state: "loading" | "forbidden" | "unavailable" } | { state: "ready"; members: Member[] };
const staffPath = "/api/v1/manage/staff/";
const coachGroups = (id: string) => `/dashboard/academy/coaches/${id}`;
const hasContactEmail = (value: string | null | undefined) => Boolean(value && !/^staff\.[0-9a-f]{32}@accounts\.invalid$/i.test(value));

export function StaffManagement() {
  const [directory, setDirectory] = useState<Directory>({ state: "loading" });
  const [attempt, setAttempt] = useState(0);
  const [selected, setSelected] = useState<Member | null>(null);
  const [created, setCreated] = useState<Member | null>(null);
  const [role, setRole] = useState<Role>("AcademyAdmin");
  const [error, setError] = useState("");
  const [phoneError, setPhoneError] = useState("");
  const [uncertain, setUncertain] = useState(false);
  const [busy, setBusy] = useState(false);
  const [creating, setCreating] = useState(false);
  const [roleTemplates, setRoleTemplates] = useState(false);
  const [contextsFor, setContextsFor] = useState<Member | null>(null);
  const [advanced, setAdvanced] = useState(false);
  const [contextEditor, setContextEditor] = useState<Member | null>(null);
  const submitting = useRef(false);
  const phone = useRef<HTMLInputElement>(null);
  const recovery = useRef<HTMLButtonElement>(null);

  useEffect(() => { if (uncertain) recovery.current?.focus(); }, [uncertain]);

  useEffect(() => {
    const controller = new AbortController();
    fetch(staffPath, { cache: "no-store", signal: controller.signal }).then(async response => {
      if (response.status === 401 || response.status === 403) {
        if (!controller.signal.aborted) setDirectory({ state: "forbidden" });
        return;
      }
      if (!response.ok) throw new Error();
      const result = await response.json();
      if (!Array.isArray(result.members)) throw new Error();
      if (!controller.signal.aborted) setDirectory({ state: "ready", members: result.members });
    }).catch(() => {
      if (!controller.signal.aborted) setDirectory({ state: "unavailable" });
    });
    return () => controller.abort();
  }, [attempt]);

  const reload = useCallback(() => {
    setDirectory({ state: "loading" });
    setAttempt(value => value + 1);
  }, []);

  function select(member: Member | null) {
    setSelected(member);
    setAdvanced(false);
  }

  function closeCreate() {
    setCreating(false);
    if (uncertain) reload();
  }

  function openCreate() {
    select(null);
    setCreated(null);
    setRole("AcademyAdmin");
    setError("");
    setPhoneError("");
    setUncertain(false);
    setCreating(true);
  }

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting.current || uncertain || directory.state !== "ready") return;
    const fields = new FormData(event.currentTarget);
    const number = egyptianStaffMobile(String(fields.get("phone") ?? ""));
    if (!number) {
      setPhoneError("اكتب رقم موبايل مصري صحيح.");
      phone.current?.focus();
      return;
    }
    const name = String(fields.get("name") ?? "").trim();
    if (!name) {
      setError("اكتب اسم الموظف.");
      return;
    }
    const email = String(fields.get("email") ?? "").trim() || null;
    const jobTitle = String(fields.get("job") ?? "").trim() || null;
    submitting.current = true;
    setBusy(true);
    setError("");
    try {
      const response = await csrfRequest(staffPath, "POST", {
        name, email, phone: number, role, jobTitle, temporaryPassword: fields.get("password"),
      }, {}, { announce: false });
      const result = await response.json();
      if (typeof result?.id !== "string" || !result.id) throw new Error();
      const member = { id: result.id, name, email, phoneNumber: number, role, jobTitle, isActive: true };
      setDirectory({ state: "ready", members: [...directory.members, member].sort((a, b) => a.name.localeCompare(b.name, "ar")) });
      setCreated(member);
      setCreating(false);
      select(role === "AcademyAdmin" ? member : null);
    } catch (caught) {
      if (caught instanceof DashboardRequestError && caught.status < 500 && caught.status > 0) {
        setError(caught.message);
      } else {
        setUncertain(true);
        setError("تعذر تأكيد إنشاء الحساب. اعرض حسابات الفريق للتحقق من وجوده.");
      }
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }

  if (roleTemplates) return <><PageHeader title="أدوات الصلاحيات السابقة" context="إدارة متقدمة" /><StaffRoleTemplates onClose={() => setRoleTemplates(false)} /></>;
  if (contextsFor && contextEditor) return <><PageHeader title="صلاحيات سياق العمل" context={contextsFor.name} /><StaffAccessEditor key={contextEditor.id} id={contextEditor.id} name={contextEditor.name} onClose={() => setContextEditor(null)} /></>;
  if (contextsFor) return <><PageHeader title="نطاقات العمل الإضافية" context="إدارة متقدمة" /><StaffAccessContexts id={contextsFor.id} name={contextsFor.name} onConfigure={(id, name) => setContextEditor({ ...contextsFor, id, name })} onClose={() => setContextsFor(null)} /></>;

  if (creating) return <>
    <header className="page-header"><div className="page-title-block">
      {!uncertain && <button type="button" className="button button-ghost staff-account-back" disabled={busy} onClick={closeCreate}><ChevronRight aria-hidden="true" />العودة إلى حسابات الفريق</button>}
      <h1>إنشاء حساب دخول</h1>
    </div></header>
    {uncertain ? <section className="detail-card">
      <ErrorState message={error} />
      <div className="form-actions"><button ref={recovery} type="button" className="primary-button" onClick={closeCreate}>عرض حسابات الفريق</button></div>
    </section> : <form className="form-shell" onSubmit={event => void create(event)} aria-busy={busy}>
      {error && <ErrorState message={error} />}
      <label>الدور<select value={role} disabled={busy || uncertain} onChange={event => setRole(event.target.value as Role)}>
        <option value="AcademyAdmin">إداري</option><option value="Coach">مدرب</option>
      </select></label>
      <label>الاسم<input name="name" required maxLength={100} autoComplete="name" disabled={busy || uncertain} /></label>
      <label>المسمى الوظيفي — اختياري<input name="job" maxLength={100} disabled={busy || uncertain} /></label>
      <div className="coach-phone-field">
        <label htmlFor="staff-phone">رقم الهاتف المصري</label>
        <input id="staff-phone" ref={phone} name="phone" type="tel" autoComplete="tel" dir="ltr" required disabled={busy || uncertain}
          aria-invalid={Boolean(phoneError)} aria-describedby={phoneError ? "staff-phone-error staff-phone-hint" : "staff-phone-hint"} onChange={() => setPhoneError("")} />
        {phoneError && <small id="staff-phone-error" role="alert" className="coach-field-error">{phoneError}</small>}
        <small id="staff-phone-hint">مثل <bdi dir="ltr">01012345678</bdi> أو <bdi dir="ltr">+201012345678</bdi>. للدخول إلى التطبيق برمز تحقق.</small>
      </div>
      <label>{role === "AcademyAdmin" ? "البريد الإلكتروني" : "البريد الإلكتروني — اختياري"}
        <input name="email" type="email" dir="ltr" maxLength={200} required={role === "AcademyAdmin"} autoComplete="email" disabled={busy || uncertain} />
      </label>
      {role === "AcademyAdmin" && <label>كلمة مرور الويب
        <input name="password" type="password" dir="ltr" minLength={12} required autoComplete="new-password" disabled={busy || uncertain} aria-describedby="staff-password-hint" />
        <small id="staff-password-hint">12 خانة على الأقل، تشمل أحرفًا كبيرة وصغيرة ورقمًا ورمزًا.</small>
      </label>}
      <div className="form-actions">
        <button className="primary-button" disabled={busy}>{busy ? "جارٍ الإنشاء…" : "إنشاء الحساب"}</button>
        <button type="button" className="secondary-button" disabled={busy} onClick={closeCreate}>إلغاء</button>
      </div>
    </form>}
  </>;

  return <><PageHeader title="حسابات الفريق" context="الصلاحيات" />
    {directory.state === "loading" && <LoadingState />}
    {directory.state === "forbidden" && <ErrorState message="حسابات الفريق وصلاحياتها متاحة للمالك فقط." />}
    {directory.state === "unavailable" && <section className="detail-card"><ErrorState message="تعذر تحميل حسابات الفريق." /><button type="button" className="secondary-button" onClick={reload}>إعادة المحاولة</button></section>}
    {directory.state === "ready" && <>
      {created && <p className="success-message" role="status">تم إنشاء حساب «{created.name}». {created.role === "Coach" && <Link href={coachGroups(created.id)}>عرض مجموعات المدرب</Link>}</p>}
      <section className="permissions-toolbar">
        <div className="section-heading"><h2>صلاحيات الإداري</h2><button type="button" className="primary-button" onClick={openCreate}>إضافة حساب دخول</button></div>
        <label className="field">اختر الإداري<select value={selected?.id ?? ""} onChange={event => select(directory.members.find(member => member.id === event.target.value) ?? null)}>
          <option value="">اختر مستخدمًا</option>{directory.members.filter(member => member.role === "AcademyAdmin").map(member => <option key={member.id} value={member.id}>{member.name} · {member.jobTitle ?? "إداري"}</option>)}
        </select></label>
      </section>
      {selected ? <>
        <label className="checkbox-line"><input type="checkbox" checked={advanced} onChange={event => setAdvanced(event.target.checked)} />عرض القوالب والصلاحيات المتقدمة</label>
        <StaffAccessEditor simple={!advanced} key={selected.id} id={selected.id} name={selected.name} onClose={() => select(null)} />
      </> : <section className="list-card">
        {directory.members.length === 0 ? <EmptyState message="لا توجد حسابات للفريق بعد." /> : <AdminDataTable label="حسابات الفريق" rows={directory.members} rowKey={member => member.id} columns={[
          { key: "name", header: "الاسم", primary: true, render: member => <strong>{member.name}</strong> },
          { key: "job", header: "الوظيفة", render: member => member.jobTitle ?? "—" },
          { key: "contact", header: "التواصل", render: member => <div className="cell-stack">
            {member.phoneNumber && <bdi dir="ltr">{member.phoneNumber}</bdi>}
            {hasContactEmail(member.email) && <bdi dir="ltr">{member.email}</bdi>}
          </div> },
          { key: "role", header: "الدور", render: member => member.role === "AcademyAdmin" ? "إداري" : "مدرب" },
          { key: "status", header: "الحالة", render: member => <StatusBadge status={member.isActive ? "Active" : "Inactive"} /> },
          { key: "actions", header: "الإجراءات", render: member => member.role === "AcademyAdmin"
            ? <button type="button" className="secondary-button" onClick={() => select(member)}>تعديل الصلاحيات</button>
            : <Link className="secondary-button" href={coachGroups(member.id)}>عرض المجموعات</Link> },
        ]} />}
      </section>}
      <details className="detail-card">
        <summary>إدارة متقدمة وسجل الصلاحيات</summary>
        <div className="form-actions">
          <button type="button" className="secondary-button" onClick={() => setRoleTemplates(true)}>أدوات القوالب السابقة</button>
          {selected && <button type="button" className="secondary-button" onClick={() => setContextsFor(selected)}>نطاقات عمل إضافية لهذا الإداري</button>}
          <Link className="secondary-button" href="/dashboard/staff/access-history">سجل تغييرات الصلاحيات</Link>
        </div>
      </details>
    </>}
  </>;
}
