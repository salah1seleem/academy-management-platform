"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import {
  AlertCircle, BarChart3, Building2, Check, ChevronDown, ChevronLeft,
  CircleDollarSign, ClipboardCheck, CreditCard, FileText, Filter,
  Trophy, LayoutDashboard, Layers3, LoaderCircle, LogOut, Menu,
  MoreHorizontal, Plus, Search, ShieldCheck, Star, UserRound, Users, X,
} from "lucide-react";
import {
  CSSProperties, FormEvent, ReactNode, useEffect, useMemo, useRef, useState,
} from "react";

type NavModule = {
  id: string;
  title: string;
  group: "الأساس" | "الإدارة" | "التشغيل" | "التحليل";
  icon: typeof LayoutDashboard;
  links: { label: string; href: string }[];
};

const modules: NavModule[] = [
  { id: "home", title: "الرئيسية", group: "الأساس", icon: LayoutDashboard, links: [{ label: "نظرة عامة", href: "/dashboard" }] },
  { id: "academy", title: "الأكاديمية", group: "الإدارة", icon: Building2, links: [
    { label: "الفروع", href: "/dashboard/academy/branches" }, { label: "الرياضات", href: "/dashboard/academy/sports" },
    { label: "الفئات العمرية", href: "/dashboard/academy/categories" }, { label: "المجموعات", href: "/dashboard/academy/groups" },
    { label: "المدربون", href: "/dashboard/academy/coaches" },
  ] },
  { id: "players", title: "اللاعبون", group: "الإدارة", icon: Users, links: [
    { label: "قائمة اللاعبين", href: "/dashboard/players" }, { label: "تسجيل لاعب جديد", href: "/dashboard/players/new" },
    { label: "طلبات الاشتراك الجديدة", href: "/dashboard/enrollment-requests" }, { label: "الإصابات والاستشارات", href: "/dashboard/players/medical" },
    { label: "معرض اللاعبين", href: "/dashboard/players/media" },
  ] },
  { id: "guardians", title: "أولياء الأمور", group: "الإدارة", icon: UserRound, links: [{ label: "قائمة أولياء الأمور", href: "/dashboard/guardians" }] },
  { id: "content", title: "المحتوى", group: "الإدارة", icon: Layers3, links: [{ label: "المنتجات الرياضية", href: "/dashboard/content/catalog" }, { label: "التغذية والصحة", href: "/dashboard/content/nutrition" }] },
  { id: "subscriptions", title: "الاشتراكات", group: "التشغيل", icon: CreditCard, links: [
    { label: "الباقات", href: "/dashboard/subscriptions/plans" }, { label: "الاشتراكات الحالية", href: "/dashboard/subscriptions/current" },
    { label: "تجديد اشتراك", href: "/dashboard/subscriptions/renew/new" },
    { label: "طلبات التجديد", href: "/dashboard/subscriptions/renewals" }, { label: "طلبات الدفع", href: "/dashboard/subscriptions/payments" },
    { label: "التحصيلات", href: "/dashboard/subscriptions/collections" }, { label: "تنتهي قريبًا", href: "/dashboard/subscriptions/expiring" },
    { label: "الاشتراكات المنتهية", href: "/dashboard/subscriptions/expired" },
  ] },
  { id: "attendance", title: "الحضور", group: "التشغيل", icon: ClipboardCheck, links: [
    { label: "جلسات التدريب", href: "/dashboard/attendance/sessions" }, { label: "حضور المدربين", href: "/dashboard/attendance/staff" },
    { label: "سجل الحضور", href: "/dashboard/attendance/history" },
  ] },
  { id: "evaluations", title: "التقييمات", group: "التشغيل", icon: Star, links: [
    { label: "معايير التقييم", href: "/dashboard/evaluations/criteria" }, { label: "تقييمات اللاعبين", href: "/dashboard/evaluations" },
    { label: "التقارير المنشورة", href: "/dashboard/evaluations/reports" },
  ] },
  { id: "reports", title: "التقارير", group: "التحليل", icon: BarChart3, links: [
    { label: "التقارير المالية", href: "/dashboard/reports/financial" }, { label: "تقارير الحضور", href: "/dashboard/reports/attendance" },
    { label: "الإيصالات", href: "/dashboard/reports/receipts" },
  ] },
];

const roleLabels: Record<string, string> = { AcademyOwner: "مالك", AcademyAdmin: "إداري", Coach: "مدرب" };

function currentModule(path: string) {
  if (path.startsWith("/dashboard/academy/")) return "academy";
  if (path.startsWith("/dashboard/players") || path.startsWith("/dashboard/enrollment-requests")) return "players";
  if (path.startsWith("/dashboard/content")) return "content";
  if (path.startsWith("/dashboard/guardians")) return "guardians";
  if (path.startsWith("/dashboard/subscriptions")) return "subscriptions";
  if (path.startsWith("/dashboard/attendance")) return "attendance";
  if (path.startsWith("/dashboard/evaluations")) return "evaluations";
  if (path.startsWith("/dashboard/reports")) return "reports";
  return "home";
}

function visibleModules(role: string) {
  if (role !== "Coach") return modules;
  return modules.filter(module => ["attendance", "evaluations", "reports"].includes(module.id)).map(module => ({
    ...module,
    links: module.links.filter(link => link.href !== "/dashboard/evaluations/criteria" && (!link.href.includes("/reports") || link.href === "/dashboard/reports/attendance")),
  }));
}

export function DashboardShell({ children }: { children: ReactNode }) {
  const path = usePathname();
  const router = useRouter();
  const activeModule = useMemo(() => currentModule(path), [path]);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [navOverride, setNavOverride] = useState<{ path: string; id: string | null } | null>(null);
  const [identity, setIdentity] = useState({ name: "", role: "", academyName: "" });
  const toggleRef = useRef<HTMLButtonElement>(null);
  const sidebarRef = useRef<HTMLElement>(null);
  const openModule = navOverride?.path === path ? navOverride.id : activeModule;
  const navigation = useMemo(() => visibleModules(identity.role), [identity.role]);
  const activeHref = useMemo(() => navigation.flatMap(module => module.links).filter(link => path === link.href || (link.href !== "/dashboard" && path.startsWith(`${link.href}/`))).sort((a, b) => b.href.length - a.href.length)[0]?.href, [navigation, path]);

  useEffect(() => {
    fetch("/api/v1/me").then(async response => {
      if (!response.ok) return router.replace("/login");
      const me = await response.json() as { displayName: string; role: string; academyName?: string };
      if (!["AcademyOwner", "AcademyAdmin", "Coach"].includes(me.role)) return router.replace(me.role === "Guardian" ? "/guardian" : "/");
      setIdentity({ name: me.displayName, role: me.role, academyName: me.academyName ?? "الأكاديمية" });
      if (me.role === "Coach" && path === "/dashboard") router.replace("/dashboard/attendance/sessions");
    }).catch(() => router.replace("/login"));
  }, [path, router]);

  useEffect(() => {
    if (!drawerOpen) return;
    sidebarRef.current?.focus();
    const close = (event: KeyboardEvent) => { if (event.key === "Escape") { setDrawerOpen(false); toggleRef.current?.focus(); } };
    document.addEventListener("keydown", close);
    document.body.classList.add("dashboard-drawer-open");
    return () => { document.removeEventListener("keydown", close); document.body.classList.remove("dashboard-drawer-open"); };
  }, [drawerOpen]);

  function toggleModule(id: string) { setNavOverride({ path, id: openModule === id ? null : id }); }
  function closeDrawer() { setDrawerOpen(false); toggleRef.current?.focus(); }
  async function logout() {
    const csrf = await fetch("/api/v1/auth/csrf").then(response => response.json()) as { token: string };
    await fetch("/api/v1/auth/logout", { method: "POST", headers: { "X-CSRF-TOKEN": csrf.token } });
    router.replace("/login");
  }

  return <div className="dashboard-shell">
    <button ref={toggleRef} className="nav-toggle" aria-label={drawerOpen ? "إغلاق القائمة" : "فتح القائمة"} aria-expanded={drawerOpen} aria-controls="staff-sidebar" onClick={() => setDrawerOpen(!drawerOpen)}>{drawerOpen ? <X /> : <Menu />}</button>
    {drawerOpen && <button className="nav-backdrop" aria-label="إغلاق القائمة" onClick={closeDrawer} />}
    <aside ref={sidebarRef} id="staff-sidebar" tabIndex={-1} className={`sidebar ${drawerOpen ? "open" : ""}`} aria-label="التنقل الرئيسي">
      <div className="sidebar-brand"><span className="brand-mark" aria-hidden="true"><Trophy /></span><span><b>{identity.academyName || "منصة الأكاديمية"}</b><small>نظام إدارة الأكاديمية</small></span></div>
      <div className="sidebar-account sidebar-account-top"><span className="account-avatar" aria-hidden="true">{identity.name.trim().slice(0, 1) || "…"}</span><span><b>{identity.name || "جارٍ تحميل الحساب…"}</b><small className="role-badge"><ShieldCheck />{roleLabels[identity.role] ?? "حساب موظف"}</small></span></div>
      <nav className="sidebar-navigation" aria-label={identity.role === "Coach" ? "مساحة المدرب" : "مساحة الإدارة"}>
        {navigation.map((module, index) => {
          const showGroup = index === 0 || module.group !== navigation[index - 1].group;
          const isExpanded = openModule === module.id;
          const Icon = module.icon;
          return <div key={module.id}>{showGroup && <p className="nav-group-label">{identity.role === "Coach" && module.group === "التشغيل" ? "مهامك" : module.group}</p>}<section className={`nav-module ${activeModule === module.id ? "current" : ""}`}>
            <button className="module-toggle" aria-expanded={isExpanded} aria-controls={`nav-${module.id}`} onClick={() => toggleModule(module.id)}><span><Icon aria-hidden="true" />{module.title}</span><ChevronDown className="nav-chevron" aria-hidden="true" /></button>
            <div className={`nav-reveal ${isExpanded ? "expanded" : ""}`} inert={!isExpanded} aria-hidden={!isExpanded}><div className="nav-reveal-inner"><div id={`nav-${module.id}`} className="submodule-list">{module.links.map(link => {
              const isActive = activeHref === link.href;
              return <Link aria-current={isActive ? "page" : undefined} className={isActive ? "active" : ""} href={link.href} key={link.href} onClick={() => setDrawerOpen(false)}><span>{link.label}</span><ChevronLeft aria-hidden="true" /></Link>;
            })}</div></div></div>
          </section></div>;
        })}
      </nav>
      <div className="sidebar-account sidebar-account-bottom"><span className="account-avatar" aria-hidden="true">{identity.name.trim().slice(0, 1) || "…"}</span><span className="sidebar-account-copy"><b>{identity.name || "حساب الموظف"}</b><small>{roleLabels[identity.role] ?? ""}</small></span><button className="sidebar-logout" aria-label="تسجيل الخروج" title="تسجيل الخروج" onClick={() => void logout()}><LogOut /></button></div>
    </aside>
    <main className="dashboard-content" id="main-content">{children}</main>
  </div>;
}

export type HeaderAction = { label: string; href: string; tone?: "primary" | "secondary" };
export function PageHeader({ title, context, description, action, secondaryActions = [] }: { title: string; context: string; description?: string; action?: HeaderAction; secondaryActions?: HeaderAction[] }) {
  const actions = [...secondaryActions, ...(action ? [action] : [])];
  return <header className="page-header"><div className="page-title-block"><p className="breadcrumb">{context}</p><h1>{title}</h1>{description && <p className="page-description">{description}</p>}</div>{actions.length > 0 && <div className="page-header-actions">{actions.map(item => <Link className={`button ${item.tone === "secondary" ? "button-secondary" : "button-primary"}`} href={item.href} key={`${item.label}-${item.href}`}>{item.tone === "secondary" ? <FileText /> : <Plus />}{item.label}</Link>)}</div>}</header>;
}

export function Button({ children, variant = "primary", type = "button", disabled, onClick, className = "" }: { children: ReactNode; variant?: "primary" | "secondary" | "ghost" | "danger"; type?: "button" | "submit"; disabled?: boolean; onClick?: () => void; className?: string }) {
  return <button className={`button button-${variant} ${className}`.trim()} type={type} disabled={disabled} onClick={onClick}>{children}</button>;
}

export type RowAction = { label: string; href?: string; onClick?: () => void; tone?: "danger" | "normal" };
function ActionControl({ action, menu = false }: { action: RowAction; menu?: boolean }) {
  const className = `${menu ? "row-menu-action" : "row-primary-action"} ${action.tone === "danger" ? "danger" : ""}`.trim();
  return action.href ? <Link className={className} href={action.href}>{action.label}</Link> : <button className={className} type="button" onClick={action.onClick}>{action.label}</button>;
}
export function RowActionMenu({ actions, label = "المزيد من الإجراءات" }: { actions: RowAction[]; label?: string }) {
  const detailsRef = useRef<HTMLDetailsElement>(null);
  if (actions.length === 0) return null;
  function closeAfterAction(action: RowAction) { action.onClick?.(); detailsRef.current?.removeAttribute("open"); }
  return <details ref={detailsRef} className="row-action-menu"><summary role="button" aria-haspopup="menu" aria-label={label}><MoreHorizontal aria-hidden="true" /></summary><div className="row-action-popover" role="menu">{actions.map(action => action.href
    ? <Link role="menuitem" className={action.tone === "danger" ? "danger" : ""} href={action.href} key={`${action.label}-${action.href}`}>{action.label}</Link>
    : <button role="menuitem" className={action.tone === "danger" ? "danger" : ""} type="button" onClick={() => closeAfterAction(action)} key={action.label}>{action.label}</button>)}</div></details>;
}
export function RowActions({ actions }: { actions: RowAction[] }) {
  if (actions.length === 0) return null;
  return <div className="row-actions"><ActionControl action={actions[0]} />{actions.length === 2 ? <ActionControl action={actions[1]} /> : <RowActionMenu actions={actions.slice(1)} />}</div>;
}

export function SearchInput({ value, onChange, label = "بحث", placeholder = "بحث…" }: { value: string; onChange: (value: string) => void; label?: string; placeholder?: string }) {
  return <label className="search-input"><Search aria-hidden="true" /><span className="sr-only">{label}</span><input aria-label={label} value={value} onChange={event => onChange(event.target.value)} placeholder={placeholder} /></label>;
}
export function ListSearch(props: { value: string; onChange: (value: string) => void; label?: string; placeholder?: string }) { return <div className="filter-bar"><SearchInput {...props} /></div>; }
export function FilterToolbar({ children, resultCount, onReset }: { children: ReactNode; resultCount?: number; onReset?: () => void }) {
  return <div className="filter-toolbar"><div className="filter-toolbar-fields"><span className="filter-toolbar-icon" aria-hidden="true"><Filter /></span>{children}</div>{(resultCount !== undefined || onReset) && <div className="filter-toolbar-meta">{resultCount !== undefined && <span>{resultCount} نتيجة</span>}{onReset && <button className="button button-ghost" type="button" onClick={onReset}>إعادة الضبط</button>}</div>}</div>;
}

type StatusTone = "success" | "warning" | "danger" | "info" | "neutral";
const statusMap: Record<string, { label: string; tone: StatusTone }> = {
  Active: { label: "فعال", tone: "success" }, Confirmed: { label: "مؤكد", tone: "success" }, Paid: { label: "مدفوع", tone: "success" }, Approved: { label: "مقبول", tone: "success" }, Present: { label: "حاضر", tone: "success" }, Published: { label: "منشور", tone: "success" }, Held: { label: "تمت", tone: "success" }, Reviewed: { label: "بيانات مراجعة", tone: "success" },
  Expiring: { label: "ينتهي قريبًا", tone: "warning" }, Pending: { label: "قيد الانتظار", tone: "warning" }, UnderReview: { label: "قيد المراجعة", tone: "warning" }, Frozen: { label: "مجمد", tone: "warning" }, PaymentInProgress: { label: "جارٍ الدفع", tone: "warning" }, DemoUnreviewed: { label: "بيانات تجريبية غير مراجعة", tone: "warning" },
  Expired: { label: "منتهي", tone: "danger" }, Rejected: { label: "مرفوض", tone: "danger" }, Absent: { label: "غائب", tone: "danger" }, Cancelled: { label: "ملغي", tone: "danger" }, Failed: { label: "فشل", tone: "danger" },
  Scheduled: { label: "مجدولة", tone: "info" }, Draft: { label: "مسودة", tone: "neutral" }, NotRecorded: { label: "لم يُسجل", tone: "neutral" }, Inactive: { label: "متوقف", tone: "neutral" }, Open: { label: "مفتوح", tone: "info" }, Monitoring: { label: "متابعة", tone: "warning" }, Resolved: { label: "مغلق", tone: "success" },
};
export function StatusBadge({ status, label }: { status: string; label?: string }) {
  const mapped = statusMap[status] ?? { label: label ?? status, tone: "neutral" as const };
  return <span className={`status-badge status-${mapped.tone}`} data-status={status}><span aria-hidden="true" />{label ?? mapped.label}</span>;
}

export type DataColumn<T> = { key: string; header: string; width?: string; primary?: boolean; render: (row: T) => ReactNode };
export function AdminDataTable<T>({ columns, rows, rowKey, rowClassName, label }: { columns: DataColumn<T>[]; rows: T[]; rowKey: (row: T) => string; rowClassName?: (row: T) => string; label: string }) {
  const template = columns.map(column => column.width ?? "minmax(0, 1fr)").join(" ");
  return <div className="admin-data-table" role="table" aria-label={label} style={{ "--table-columns": template } as CSSProperties}>
    <div className="admin-data-header" role="row">{columns.map(column => <div role="columnheader" key={column.key}>{column.header}</div>)}</div>
    <div className="admin-data-body" role="rowgroup">{rows.map(row => <article className={`admin-data-row ${rowClassName?.(row) ?? ""}`.trim()} role="row" key={rowKey(row)}>{columns.map(column => <div className={column.primary ? "data-primary" : ""} role="cell" data-label={column.header} key={column.key}>{column.render(row)}</div>)}</article>)}</div>
  </div>;
}

export function MetricCard({ label, value, icon, emphasis = "secondary", detail }: { label: string; value: ReactNode; icon?: ReactNode; emphasis?: "primary" | "secondary"; detail?: string }) {
  return <article className={`metric-card metric-card-${emphasis}`}><div className="metric-icon" aria-hidden="true">{icon ?? <CircleDollarSign />}</div><div><span>{label}</span><strong>{value}</strong>{detail && <small>{detail}</small>}</div></article>;
}
export function SectionCard({ title, description, children, action }: { title: string; description?: string; children: ReactNode; action?: ReactNode }) {
  return <section className="section-card"><header><div><h2>{title}</h2>{description && <p>{description}</p>}</div>{action}</header>{children}</section>;
}
export function FormSection({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return <fieldset className="form-section"><legend>{title}</legend>{description && <p>{description}</p>}<div className="form-section-grid">{children}</div></fieldset>;
}

export function ConfirmationDialog({ open, title, message, confirmLabel, busy, tone = "danger", onConfirm, onCancel }: { open: boolean; title: string; message: string; confirmLabel: string; busy?: boolean; tone?: "danger" | "primary"; onConfirm: () => void; onCancel: () => void }) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    cancelRef.current?.focus();
    const close = (event: KeyboardEvent) => { if (event.key === "Escape" && !busy) onCancel(); };
    document.addEventListener("keydown", close);
    return () => document.removeEventListener("keydown", close);
  }, [busy, onCancel, open]);
  if (!open) return null;
  return <div className="dialog-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget && !busy) onCancel(); }}><section className={`confirmation-dialog dialog-${tone}`} role="dialog" aria-modal="true" aria-labelledby="confirmation-title" aria-describedby="confirmation-message"><div className="dialog-icon" aria-hidden="true">{tone === "danger" ? <AlertCircle /> : <Check />}</div><div><h2 id="confirmation-title">{title}</h2><p id="confirmation-message">{message}</p></div><div className="form-actions"><Button variant={tone} disabled={busy} onClick={onConfirm}>{busy ? <><LoaderCircle className="spin" /> جارٍ الحفظ…</> : confirmLabel}</Button><button ref={cancelRef} className="button button-secondary" type="button" disabled={busy} onClick={onCancel}>إلغاء</button></div></section></div>;
}

export function LoadingState({ message = "جارٍ تحميل البيانات…" }: { message?: string }) { return <div className="state-message loading-state" role="status"><span>{message}</span><div className="sports-skeleton" aria-hidden="true"><i /><i /><i /></div></div>; }
export function ErrorState({ message }: { message: string }) { return <div className="state-message error-message" role="alert"><AlertCircle aria-hidden="true" /><span>{message}</span></div>; }
export function EmptyState({ message = "لا توجد سجلات مطابقة.", title = "لا توجد بيانات", action }: { message?: string; title?: string; action?: ReactNode }) { return <div className="empty-state"><FileText aria-hidden="true" /><h3>{title}</h3><p>{message}</p>{action}</div>; }
export function preventDefaultSubmit(handler: () => void) { return (event: FormEvent) => { event.preventDefault(); handler(); }; }
