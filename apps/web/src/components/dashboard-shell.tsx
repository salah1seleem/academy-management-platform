"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { ReactNode, useEffect, useState } from "react";

const modules = [
  { title: "الرئيسية", links: [{ label: "نظرة عامة", href: "/dashboard" }] },
  { title: "الأكاديمية", links: [
    { label: "الفروع", href: "/dashboard/academy/branches" }, { label: "الرياضات", href: "/dashboard/academy/sports" },
    { label: "الفئات العمرية", href: "/dashboard/academy/categories" }, { label: "المجموعات", href: "/dashboard/academy/groups" },
    { label: "المدربون", href: "/dashboard/academy/coaches" },
  ] },
  { title: "اللاعبون", links: [{ label: "قائمة اللاعبين", href: "/dashboard/players" }, { label: "تسجيل لاعب جديد", href: "/dashboard/players/new" }] },
  { title: "أولياء الأمور", links: [{ label: "قائمة أولياء الأمور", href: "/dashboard/guardians" }] },
];

export function DashboardShell({ children }: { children: ReactNode }) {
  const path = usePathname(); const router = useRouter(); const [open, setOpen] = useState(false); const [name, setName] = useState("");
  useEffect(() => { fetch("/api/v1/me").then(async r => { if (!r.ok) return router.replace("/login"); const me = await r.json() as { displayName: string; role: string }; if (!["AcademyOwner", "AcademyAdmin"].includes(me.role)) return router.replace(me.role === "Guardian" ? "/guardian" : "/"); setName(me.displayName); }); }, [router]);
  async function logout() { const csrf = await fetch("/api/v1/auth/csrf").then(r => r.json()) as { token: string }; await fetch("/api/v1/auth/logout", { method: "POST", headers: { "X-CSRF-TOKEN": csrf.token } }); router.replace("/login"); }
  return <div className="dashboard-shell">
    <button className="nav-toggle" aria-label="فتح القائمة" onClick={() => setOpen(!open)}>☰</button>
    <aside className={`sidebar ${open ? "open" : ""}`} aria-label="التنقل الرئيسي">
      <div className="brand">منصة الأكاديمية<small>{name}</small></div>
      {modules.map(module => <section key={module.title} className="nav-module"><h2>{module.title}</h2>{module.links.map(link => <Link aria-current={path === link.href ? "page" : undefined} className={path === link.href ? "active" : ""} href={link.href} key={link.href} onClick={() => setOpen(false)}>{link.label}</Link>)}</section>)}
      <button className="sidebar-logout" onClick={() => void logout()}>تسجيل الخروج</button>
    </aside>
    <main className="dashboard-content">{children}</main>
  </div>;
}

export function PageHeader({ title, context, action }: { title: string; context: string; action?: { label: string; href: string } }) {
  return <header className="page-header"><div><p>{context}</p><h1>{title}</h1></div>{action && <Link className="primary-action" href={action.href}>{action.label}</Link>}</header>;
}

export function RowActions() { return <div className="row-actions"><button>عرض</button><button>تعديل</button><button>إيقاف</button></div>; }
