"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { ReactNode, useEffect, useMemo, useState } from "react";

type NavModule = { id: string; title: string; links: { label: string; href: string }[] };
const modules: NavModule[] = [
  { id: "home", title: "الرئيسية", links: [{ label: "نظرة عامة", href: "/dashboard" }] },
  { id: "academy", title: "الأكاديمية", links: [
    { label: "الفروع", href: "/dashboard/academy/branches" }, { label: "الرياضات", href: "/dashboard/academy/sports" },
    { label: "الفئات العمرية", href: "/dashboard/academy/categories" }, { label: "المجموعات", href: "/dashboard/academy/groups" },
    { label: "المدربون", href: "/dashboard/academy/coaches" },
  ] },
  { id: "players", title: "اللاعبون", links: [{ label: "قائمة اللاعبين", href: "/dashboard/players" }, { label: "تسجيل لاعب جديد", href: "/dashboard/players/new" }] },
  { id: "guardians", title: "أولياء الأمور", links: [{ label: "قائمة أولياء الأمور", href: "/dashboard/guardians" }] },
  { id: "subscriptions", title: "الاشتراكات", links: [
    { label: "الباقات", href: "/dashboard/subscriptions/plans" }, { label: "الاشتراكات الحالية", href: "/dashboard/subscriptions/current" },
    { label: "طلبات التجديد", href: "/dashboard/subscriptions/renewals" }, { label: "طلبات الدفع", href: "/dashboard/subscriptions/payments" },
    { label: "التحصيلات", href: "/dashboard/subscriptions/collections" }, { label: "الاشتراكات التي تنتهي قريباً", href: "/dashboard/subscriptions/expiring" },
    { label: "الاشتراكات المنتهية", href: "/dashboard/subscriptions/expired" },
  ] },
];

function currentModule(path: string) {
  if (path.startsWith("/dashboard/academy/")) return "academy";
  if (path.startsWith("/dashboard/players")) return "players";
  if (path.startsWith("/dashboard/guardians")) return "guardians";
  if (path.startsWith("/dashboard/subscriptions")) return "subscriptions";
  return "home";
}

export function DashboardShell({ children }: { children: ReactNode }) {
  const path = usePathname();
  const router = useRouter();
  const activeModule = useMemo(() => currentModule(path), [path]);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [navOverride, setNavOverride] = useState<{ path: string; id: string | null } | null>(null);
  const [name, setName] = useState("");
  const openModule = navOverride?.path === path ? navOverride.id : activeModule;

  useEffect(() => { fetch("/api/v1/me").then(async response => { if (!response.ok) return router.replace("/login"); const me = await response.json() as { displayName: string; role: string }; if (!["AcademyOwner", "AcademyAdmin"].includes(me.role)) return router.replace(me.role === "Guardian" ? "/guardian" : "/"); setName(me.displayName); }); }, [router]);

  function toggleModule(id: string) { setNavOverride({ path, id: openModule === id ? null : id }); }
  async function logout() { const csrf = await fetch("/api/v1/auth/csrf").then(response => response.json()) as { token: string }; await fetch("/api/v1/auth/logout", { method: "POST", headers: { "X-CSRF-TOKEN": csrf.token } }); router.replace("/login"); }

  return <div className="dashboard-shell">
    <button className="nav-toggle" aria-label={drawerOpen ? "إغلاق القائمة" : "فتح القائمة"} aria-expanded={drawerOpen} onClick={() => setDrawerOpen(!drawerOpen)}>☰</button>
    {drawerOpen && <button className="nav-backdrop" aria-label="إغلاق القائمة" onClick={() => setDrawerOpen(false)} />}
    <aside className={`sidebar ${drawerOpen ? "open" : ""}`} aria-label="التنقل الرئيسي">
      <div className="brand">منصة الأكاديمية<small>{name}</small></div>
      {modules.map(module => {
        const isExpanded = openModule === module.id;
        return <section key={module.id} className={`nav-module ${activeModule === module.id ? "current" : ""}`}>
          <button className="module-toggle" aria-expanded={isExpanded} aria-controls={`nav-${module.id}`} onClick={() => toggleModule(module.id)}><span>{module.title}</span><span aria-hidden="true">{isExpanded ? "−" : "+"}</span></button>
          <div id={`nav-${module.id}`} className="submodule-list" hidden={!isExpanded}>{module.links.map(link => {
            const isActive = path === link.href;
            return <Link aria-current={isActive ? "page" : undefined} className={isActive ? "active" : ""} href={link.href} key={link.href} onClick={() => setDrawerOpen(false)}>{link.label}</Link>;
          })}</div>
        </section>;
      })}
      <button className="sidebar-logout" onClick={() => void logout()}>تسجيل الخروج</button>
    </aside>
    <main className="dashboard-content">{children}</main>
  </div>;
}

export function PageHeader({ title, context, action }: { title: string; context: string; action?: { label: string; href: string } }) {
  return <header className="page-header"><div><p>{context}</p><h1>{title}</h1></div>{action && <Link className="primary-action" href={action.href}>{action.label}</Link>}</header>;
}

export type RowAction = { label: string; href?: string; onClick?: () => void; tone?: "danger" | "normal" };
export function RowActions({ actions }: { actions: RowAction[] }) {
  return <div className="row-actions">{actions.map(action => action.href
    ? <Link className={action.tone === "danger" ? "danger" : ""} href={action.href} key={`${action.label}-${action.href}`}>{action.label}</Link>
    : <button className={action.tone === "danger" ? "danger" : ""} type="button" onClick={action.onClick} key={action.label}>{action.label}</button>)}</div>;
}

export function ListSearch({ value, onChange, label = "بحث", placeholder = "بحث…" }: { value: string; onChange: (value: string) => void; label?: string; placeholder?: string }) {
  return <div className="filter-bar"><input aria-label={label} value={value} onChange={event => onChange(event.target.value)} placeholder={placeholder} /></div>;
}

export function ConfirmationDialog({ open, title, message, confirmLabel, busy, onConfirm, onCancel }: { open: boolean; title: string; message: string; confirmLabel: string; busy?: boolean; onConfirm: () => void; onCancel: () => void }) {
  if (!open) return null;
  return <div className="dialog-backdrop" role="presentation"><section className="confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="confirmation-title"><h2 id="confirmation-title">{title}</h2><p>{message}</p><div className="form-actions"><button className="primary-button" type="button" disabled={busy} onClick={onConfirm}>{busy ? "جارٍ الحفظ…" : confirmLabel}</button><button type="button" disabled={busy} onClick={onCancel}>إلغاء</button></div></section></div>;
}

export function LoadingState() { return <p className="state-message" role="status">جارٍ تحميل البيانات…</p>; }
export function ErrorState({ message }: { message: string }) { return <p className="state-message error-message" role="alert">{message}</p>; }
export function EmptyState({ message = "لا توجد سجلات مطابقة." }: { message?: string }) { return <div className="empty-state">{message}</div>; }
