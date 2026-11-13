"use client";
import { HelpEntry } from "./help-entry";

import Link from "next/link";
import Image from "next/image";
import { WorkContextLink } from "../features/work-context-link";
import { operationLoginPath } from "../lib/operation-return";
import { saveWebAppearance, useWebAppearance } from "../lib/web-appearance";
import { dashboardParentPath } from "../lib/dashboard-routes";
import { usePathname, useRouter } from "next/navigation";
import {
  AlertCircle, ArrowRight, Bell, Building2, Check, ChevronDown, ChevronLeft,
  CircleDollarSign, ClipboardCheck, FileText, Filter,
  Trophy, LayoutDashboard, Layers3, LoaderCircle, LogOut, Menu,
  MoreHorizontal, Moon, Pencil, Plus, Search, ShieldCheck, Star, Sun, Users, X, Info,
} from "lucide-react";
import {
  CSSProperties, FormEvent, ReactNode, createContext, useContext, useEffect, useId, useMemo, useRef, useState,
} from "react";

type NavModule = {
  id: string;
  title: string;
  group: "الأساس" | "الإدارة" | "التشغيل" | "التحليل";
  icon: typeof LayoutDashboard;
  links: { label: string; href: string }[];
};

// V4: destinations remain canonical; navigation is not an authorization boundary.
const modules: NavModule[] = [
  {id:"home",title:"الرئيسية",group:"الأساس",icon:LayoutDashboard,links:[{label:"الرئيسية",href:"/dashboard"}]},
  {id:"academy",title:"إدارة الأكاديمية",group:"الإدارة",icon:Building2,links:[
    {label:"الرياضات",href:"/dashboard/academy/sports"},{label:"الفروع",href:"/dashboard/academy/branches"},
    {label:"الفئات العمرية",href:"/dashboard/academy/categories"},{label:"المجموعات",href:"/dashboard/academy/groups"},
    {label:"الحصص وجداول التدريب",href:"/dashboard/academy/groups/sessions"},{label:"باقات الاشتراك",href:"/dashboard/subscriptions/plans"},
    {label:"إدارة الخصومات",href:"/dashboard/discounts"},
    {label:"المدربون والموظفون",href:"/dashboard/staff/employees"},{label:"معايير تقييم اللاعبين",href:"/dashboard/evaluations/criteria"},
    {label:"استيراد البيانات",href:"/dashboard/import"},{label:"التغذية والصحة",href:"/dashboard/content/nutrition"},
    {label:"المرافق العامة",href:"/dashboard/facilities"},{label:"إعدادات الأكاديمية",href:"/dashboard/settings"}]},
  {id:"operations",title:"عمليات الأكاديمية",group:"التشغيل",icon:ClipboardCheck,links:[
    {label:"تسجيل لاعب / اشتراك جديد",href:"/dashboard/players/new"},{label:"تجديد اشتراك",href:"/dashboard/subscriptions/renew/new"},
    {label:"تسكين اللاعبين بالمجموعات",href:"/dashboard/players?placement=true"},{label:"تسجيل حضور اللاعبين",href:"/dashboard/attendance/sessions"},
    {label:"حضور وانصراف الموظفين",href:"/dashboard/attendance/staff"},{label:"سجل الحضور",href:"/dashboard/attendance/history"},
    {label:"طلبات إثبات حضور الموظفين",href:"/dashboard/attendance/staff/exceptions"}]},
  {id:"players",title:"الأعضاء والاشتراكات",group:"الإدارة",icon:Users,links:[
    {label:"اللاعبون",href:"/dashboard/players"},{label:"أولياء الأمور",href:"/dashboard/guardians"},
    {label:"الاشتراكات",href:"/dashboard/subscriptions/current"},{label:"معرض الصور والفيديوهات",href:"/dashboard/players/media"},
    {label:"الإصابات والاستشارات",href:"/dashboard/players/medical"},{label:"تقييمات اللاعبين",href:"/dashboard/evaluations"},
    {label:"سجل حضور اللاعبين",href:"/dashboard/attendance/history?subject=players"}]},
  {id:"followups",title:"المتابعات والطلبات",group:"التشغيل",icon:ClipboardCheck,links:[
    {label:"المتابعات واتخاذ القرار",href:"/dashboard/operations/follow-ups"},{label:"متابعاتي المسندة",href:"/dashboard/my-followups"},
    {label:"طلبات الدفع",href:"/dashboard/subscriptions/payments"},{label:"طلبات مبالغ إضافية",href:"/dashboard/payroll/requests"}]},
  {id:"feedback",title:"الشكاوى والاقتراحات",group:"الإدارة",icon:Star,links:[{label:"الشكاوى والاقتراحات",href:"/dashboard/feedback"}]},
  {id:"finance",title:"الحسابات والماليات",group:"التحليل",icon:CircleDollarSign,links:[{label:"الحسابات والماليات",href:"/dashboard/finance"},{label:"إيصالات الاشتراكات",href:"/dashboard/reports/receipts"}]},
  {id:"matches",title:"البطولات والمباريات",group:"التشغيل",icon:Trophy,links:[{label:"البطولات والمباريات",href:"/dashboard/matches"}]},
  {id:"catalog",title:"المنتجات الرياضية",group:"الإدارة",icon:Layers3,links:[{label:"المنتجات الرياضية",href:"/dashboard/content/catalog"}]},
  {id:"news",title:"الأخبار والفعاليات",group:"الإدارة",icon:FileText,links:[
    {label:"عرض الأخبار والفعاليات",href:"/dashboard/activities"},{label:"إدارة النشر",href:"/dashboard/news"}]},
  {id:"notifications",title:"التنبيهات",group:"التشغيل",icon:Bell,links:[{label:"التنبيهات",href:"/dashboard/notifications"}]},
  {id:"permissions",title:"الصلاحيات",group:"الإدارة",icon:ShieldCheck,links:[{label:"الصلاحيات",href:"/dashboard/staff"}]},
];

const roleLabels: Record<string, string> = { AcademyOwner: "مالك", AcademyAdmin: "إداري", Coach: "مدرب" };
const DashboardPathContext = createContext<string | null>(null);
const DashboardPermissionsContext = createContext<string[] | null>(null);

export function useDashboardPermission(permission: string) {
  return useContext(DashboardPermissionsContext)?.includes(permission);
}

function focusableWithin(element: HTMLElement) {
  return Array.from(element.querySelectorAll<HTMLElement>('a[href], button, input, select, textarea, summary, [tabindex]'))
    .filter(control => control.tabIndex >= 0 && !control.matches(':disabled, [type="hidden"]') && !control.closest('[inert], [hidden], [aria-hidden="true"], details:not([open]) > :not(summary)'));
}

function linkPermission(href: string) {
  if (href === "/dashboard/notifications") return null; // Authenticated inbox; management remains owner-only on the server.
  if (href === "/dashboard/activities") return null;
  if (href.startsWith("/dashboard/operations/follow-ups") || href.startsWith("/dashboard/feedback")) return "dashboard.owner.read";
  if (href === "/dashboard/finance") return "reports.financial.read";
  if (href.startsWith("/dashboard/subscriptions/renew/")) return "subscription.renew.manage";
  if (href === "/dashboard/subscriptions/current") return "subscription.read";
  if (href === "/dashboard/my-followups") return "followups.read";
  if (href.startsWith("/dashboard/attendance/staff/exceptions")) return "staff.provision";
  if (href === "/dashboard") return null; // The basic task home remains available without financial metrics.
  if (href === "/dashboard/import") return "people.manage";
  if (href === "/dashboard/facilities") return "facilities.read";
  if (href.includes("/settings")) return "academy.settings.manage";
  if (href.includes("/payroll") || href.includes("/staff/employees")) return "payroll.read";
  if (href.includes("/matches")) return "structure.manage";
  if (href === "/dashboard/reports/receipts" || href.includes("/reports/financial")) return "reports.financial.read";
  if (href.includes("/finance/")) return "reports.financial.read";
  if (href.includes("/reports/attendance")) return "reports.attendance.read";
  if (href.includes("/academy")) return "structure.manage";
  if (href.includes("/players/medical")) return "content.medical.read";
  if (href.includes("/players/media")) return "content.media.read";
  if (href.includes("/players") || href.includes("/guardians")) return "people.manage";
  if (href.includes("/family-renewals")) return "payment.read";
  if (href.includes("/subscriptions/payments")) return "payment.read";
  if (href.includes("/subscriptions/plans")) return "subscription.plan.manage";
  if (href.includes("/discounts")) return "subscription.discount.manage";
  if (href.includes("/content/catalog")) return "content.catalog.manage";
  if (href.includes("/content/nutrition")) return "content.nutrition.manage";
  if (href.includes("/news")) return "content.media.manage";
  if (href.includes("/attendance")) return "attendance.read";
  if (href.includes("/evaluations/criteria")) return "evaluation.criteria.manage";
  if (href.includes("/evaluations")) return "evaluation.read";
  return "staff.provision";
}

function currentModule(path: string) {
  if (path.startsWith("/dashboard/account/") || path === "/dashboard/context") return "account";
  if (path.startsWith("/dashboard/my-followups")) return "followups";
  if (path === "/dashboard/help" || path.startsWith("/dashboard/help/")) return "help";
  const match = modules.flatMap(module => module.links.filter(link => !link.href.includes("?")).map(link => ({ id: module.id, href: link.href.split("?")[0] })))
    .filter(link => path === link.href || (link.href !== "/dashboard" && path.startsWith(`${link.href}/`)))
    .sort((a, b) => b.href.length - a.href.length)[0];
  if (match) return match.id;
  if (path.startsWith("/dashboard/academy/")) return "academy";
  if (path.startsWith("/dashboard/operations/")) return "operations";
  if (path.startsWith("/dashboard/enrollment-requests")) return "followups";
  if (path.startsWith("/dashboard/community-campaigns")) return "news";
  if (path.startsWith("/dashboard/reports/financial") || path.startsWith("/dashboard/payroll") || path.startsWith("/dashboard/product-sales") || path.startsWith("/dashboard/tournament-payments")) return "finance";
  if (path.startsWith("/dashboard/reports/attendance")) return "operations";
  if (path.startsWith("/dashboard/family-renewals")) return "players";
  if (path.startsWith("/dashboard/subscriptions")) return "players";
  return "home";
}

function visibleModules(role: string) {
  if (role !== "Coach") return modules;
  return modules.filter(module => ["operations", "development", "analytics", "notifications"].includes(module.id)).map(module => ({
    ...module,
    links: module.links.filter(link => link.href !== "/dashboard/evaluations/criteria" && (!link.href.includes("/reports") || link.href === "/dashboard/reports/attendance")),
  }));
}

export function DashboardShell({ children }: { children: ReactNode }) {
  const path = usePathname();
  if (path === "/dashboard/context") return <>{children}</>;
  return <StaffDashboardShell path={path}>{children}</StaffDashboardShell>;
}

function StaffDashboardShell({ path, children }: { path: string; children: ReactNode }) {
  const router = useRouter();
  const mode = useWebAppearance();
  const activeModule = useMemo(() => currentModule(path), [path]);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [logoutBusy, setLogoutBusy] = useState(false);
  const [logoutError, setLogoutError] = useState("");
  const [saveNotice, setSaveNotice] = useState("");
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout>;
    const saved = (event: Event) => {
      setSaveNotice((event as CustomEvent<string>).detail);
      clearTimeout(timer);
      timer = setTimeout(() => setSaveNotice(""), 7000);
    };
    window.addEventListener("academy-save-success", saved);
    return () => { clearTimeout(timer); window.removeEventListener("academy-save-success", saved); };
  }, []);
  const [navOverride, setNavOverride] = useState<{ path: string; ids: string[] } | null>(null);
  const [permissions, setPermissions] = useState<string[]>([]);
  const [identity, setIdentity] = useState({ name: "", role: "", academyName: "", contextName: "" });
  const [brand, setBrand] = useState<{ displayName?: string; primaryColor?: string; secondaryColor?: string; logoUrl?: string; platformBrandOperator?: boolean; navigation?:{generalFacilitiesEnabled:boolean}; onboarding?:{features?:string[]} }>({});
  const toggleRef = useRef<HTMLButtonElement>(null);
  const sidebarRef = useRef<HTMLElement>(null);
  const openModules = navOverride?.path === path ? navOverride.ids : [activeModule];
  const navigation = useMemo(() => visibleModules(identity.role).map(m => ({ ...m, links: m.links.filter(l => {
    if (l.href === "/dashboard/facilities" && !brand.navigation?.generalFacilitiesEnabled) return false;
    if (l.href === "/dashboard/matches" && !brand.onboarding?.features?.includes("matches")) return false;
    if (l.href === "/dashboard/activities" && !brand.onboarding?.features?.includes("news")) return false;
    if (l.href === "/dashboard/my-followups") return identity.role === "AcademyAdmin" && permissions.includes("followups.read");
    if (l.href.startsWith("/dashboard/staff") && !l.href.startsWith("/dashboard/staff/employees") && identity.role !== "AcademyOwner") return false;
    if (l.href.startsWith("/dashboard/finance/provider-quarantine") && identity.role !== "AcademyOwner") return false;
    if (l.href.startsWith("/dashboard/finance/operating-expenses") && identity.role !== "AcademyOwner") return false;
    if (l.href.startsWith("/dashboard/finance/settlement-comparisons") && identity.role !== "AcademyOwner") return false;
    if (l.href === "/dashboard/academy/groups/sessions") return identity.role === "AcademyOwner" || ["structure.manage", "people.manage", "attendance.read"].every(p => permissions.includes(p));
    return identity.role === "AcademyOwner" || l.href === "/dashboard/settings" && brand.platformBrandOperator || linkPermission(l.href) === null || permissions.includes(linkPermission(l.href)!);
  }) })).filter(m => m.links.length > 0), [identity.role, permissions, brand.platformBrandOperator,brand.navigation?.generalFacilitiesEnabled,brand.onboarding?.features]);
  const activeHref = useMemo(() => navigation.flatMap(module => module.links).filter(link => path === link.href || (link.href !== "/dashboard" && path.startsWith(`${link.href}/`))).sort((a, b) => b.href.length - a.href.length)[0]?.href, [navigation, path]);

  useEffect(() => {
    fetch("/api/v1/me").then(async response => {
      if (!response.ok) return router.replace(operationLoginPath(path));
      const me = await response.json() as { displayName: string; role: string; academyName?: string; contextName?: string };
      if (!["AcademyOwner", "AcademyAdmin"].includes(me.role)) return router.replace("/");
      setIdentity({ name: me.displayName, role: me.role, academyName: me.academyName ?? "الأكاديمية", contextName: me.contextName ?? "" });
      fetch("/api/v1/academy/experience").then(r => r.ok ? r.json() : {}).then(setBrand).catch(() => setBrand({}));
      fetch("/api/v1/my-permissions").then(r => r.ok ? r.json() : []).then(p => setPermissions(Array.isArray(p) ? p : [])).catch(() => setPermissions([]));
    }).catch(() => router.replace(operationLoginPath(path)));
  }, [path, router]);

  useEffect(() => {
    if (!drawerOpen) return;
    sidebarRef.current?.focus();
    const close = (event: KeyboardEvent) => {
      if (event.key === "Escape") { event.preventDefault(); setDrawerOpen(false); toggleRef.current?.focus(); }
      if (event.key !== "Tab" || !sidebarRef.current) return;
      const controls = [...focusableWithin(sidebarRef.current), ...(toggleRef.current ? [toggleRef.current] : [])];
      const current = controls.indexOf(document.activeElement as HTMLElement);
      const next = event.shiftKey ? (current < 0 ? controls.length - 1 : (current + controls.length - 1) % controls.length) : (current + 1) % controls.length;
      event.preventDefault(); controls[next]?.focus();
    };
    document.addEventListener("keydown", close);
    document.body.classList.add("dashboard-drawer-open");
    const desktop = window.matchMedia?.("(min-width: 72rem)");
    const resize = () => { if (desktop?.matches) setDrawerOpen(false); };
    desktop?.addEventListener("change", resize);
    return () => { document.removeEventListener("keydown", close); desktop?.removeEventListener("change", resize); document.body.classList.remove("dashboard-drawer-open"); };
  }, [drawerOpen]);

  function toggleModule(id: string) {
    // Keep other sections open: collapsing a tall section above the clicked
    // heading moves that heading out of the user's viewport.
    setNavOverride({ path, ids: openModules.includes(id) ? openModules.filter(value => value !== id) : [...openModules, id] });
  }
  function closeDrawer() { setDrawerOpen(false); toggleRef.current?.focus(); }
  async function logout() {
    if (logoutBusy) return;
    setLogoutBusy(true); setLogoutError("");
    try {
      const response = await fetch("/api/v1/auth/csrf");
      if (!response.ok) throw new Error();
      const csrf = await response.json() as { token: string };
      const result = await fetch("/api/v1/auth/logout", { method: "POST", headers: { "X-CSRF-TOKEN": csrf.token } });
      if (!result.ok) throw new Error();
      router.replace("/login");
    } catch { setLogoutError("تعذر تسجيل الخروج. حاول مرة أخرى."); }
    finally { setLogoutBusy(false); }
  }

  const primary = brand.primaryColor ?? "#D71935", secondary = brand.secondaryColor ?? "#171923";
  return <div className="dashboard-shell" data-workspace-design="v5" data-mode={mode} data-brand-primary={primary.toUpperCase()} style={{ "--staff-primary-950": secondary, "--staff-primary-900": secondary, "--staff-primary-800": `color-mix(in srgb, ${primary}, black 25%)`, "--staff-primary-700": primary, "--staff-primary-600": primary, "--staff-primary-100": `color-mix(in srgb, ${primary}, white 92%)`, "--staff-primary-50": `color-mix(in srgb, ${primary}, white 97%)` } as CSSProperties}>
    <a className="workspace-skip-link" href="#main-content" inert={drawerOpen}>انتقل إلى محتوى الصفحة</a>
    <button ref={toggleRef} className="nav-toggle" aria-label={drawerOpen ? "إغلاق القائمة" : "فتح القائمة"} aria-expanded={drawerOpen} aria-controls="staff-sidebar" onClick={() => setDrawerOpen(!drawerOpen)}>{drawerOpen ? <X /> : <Menu />}</button>
    {drawerOpen && <button className="nav-backdrop" aria-hidden="true" tabIndex={-1} onClick={closeDrawer} />}
    <aside ref={sidebarRef} id="staff-sidebar" tabIndex={-1} className={`sidebar ${drawerOpen ? "open" : ""}`} aria-label="التنقل الرئيسي" data-workspace={activeModule}>
      {brand.logoUrl && <div className="sidebar-watermark" aria-hidden="true" style={{ backgroundImage: `url(${brand.logoUrl})` }} />}
      <div className="sidebar-brand">{brand.logoUrl ? <Image unoptimized width={52} height={64} className="academy-logo" src={brand.logoUrl} alt={brand.displayName ?? identity.academyName} /> : <span className="brand-mark" aria-hidden="true"><Trophy /></span>}<span><b>{brand.displayName || identity.academyName || "منصة الأكاديمية"}</b><small>نظام إدارة الأكاديمية</small></span></div>
      <div className="sidebar-account sidebar-account-top"><span className="account-avatar" aria-hidden="true">{identity.name.trim().slice(0, 1) || "…"}</span><span><b>{identity.name || "جارٍ تحميل الحساب…"}</b><small className="role-badge"><ShieldCheck />{identity.contextName || roleLabels[identity.role] || "حساب موظف"}</small></span></div>
      <WorkContextLink />
      <nav className="sidebar-navigation" aria-label={identity.role === "Coach" ? "مساحة المدرب" : "مساحة الإدارة"}>
        {navigation.map(module => {
          const isExpanded = openModules.includes(module.id);
          const Icon = module.icon;
          return <div key={module.id}><section className={`nav-module ${activeModule === module.id ? "current" : ""}`}>
            {module.links.length === 1 && module.id !== "players" ? <div className="module-direct"><Link className="module-toggle" href={module.links[0].href} aria-current={activeHref === module.links[0].href ? "page" : undefined} onClick={() => setDrawerOpen(false)}><span><Icon aria-hidden="true" />{module.title}</span></Link></div> : <button className="module-toggle" aria-expanded={isExpanded} aria-controls={`nav-${module.id}`} onClick={() => toggleModule(module.id)}><span><Icon aria-hidden="true" />{module.title}</span><ChevronDown className="nav-chevron" aria-hidden="true" /></button>}

            <div className={`nav-reveal ${isExpanded ? "expanded" : ""}`} inert={!isExpanded} aria-hidden={!isExpanded}><div className="nav-reveal-inner"><div id={`nav-${module.id}`} className="submodule-list">{module.links.filter(() => module.links.length > 1 || module.id === "players").map(link => {
              const isActive = activeHref === link.href;
              return <Link aria-current={isActive ? "page" : undefined} className={isActive ? "active" : ""} href={link.href} key={link.href} onClick={() => setDrawerOpen(false)}><span>{link.label}</span><ChevronLeft aria-hidden="true" /></Link>;
            })}</div></div></div>
          </section></div>;
        })}
      </nav>
      <details className="sidebar-security-link" open={activeModule==="account"?true:undefined}><summary>حسابي والأمان</summary><Link href="/dashboard/account/verification" aria-current={path==="/dashboard/account/verification"?"page":undefined} onClick={closeDrawer}>وسائل التحقق الخاصة بي</Link><Link href="/dashboard/account/attendance" aria-current={path==="/dashboard/account/attendance"?"page":undefined} onClick={closeDrawer}>حضوري الوظيفي</Link></details>
      <HelpEntry key={path} current={activeModule === "help"} onNavigate={closeDrawer} />
      {logoutError && <p className="sidebar-logout-error" role="alert">{logoutError}</p>}
      <div className="sidebar-account sidebar-account-bottom"><span className="account-avatar" aria-hidden="true">{identity.name.trim().slice(0, 1) || "…"}</span><span className="sidebar-account-copy"><b>{identity.name || "حساب الموظف"}</b><small>{roleLabels[identity.role] ?? ""}</small></span><button className="sidebar-logout" aria-label="تسجيل الخروج" title="تسجيل الخروج" disabled={logoutBusy} aria-busy={logoutBusy || undefined} onClick={() => void logout()}>{logoutBusy ? <LoaderCircle className="spin" aria-hidden="true" /> : <LogOut aria-hidden="true" />}</button></div>
    </aside>
    <main className="dashboard-content" id="main-content" tabIndex={-1} inert={drawerOpen}><DashboardPathContext.Provider value={path}>
      {path !== "/dashboard" && <div className="workspace-route-bar"><nav aria-label="مسار مساحة العمل"><Link href="/dashboard">الرئيسية</Link><ChevronLeft aria-hidden="true"/><span>{path === "/dashboard/activities" ? "الأخبار والفعاليات" : navigation.find(module => module.id === activeModule)?.title ?? (activeModule==="help"?"دليل الاستخدام":activeModule==="account"?"حسابي والأمان":"الأكاديمية")}</span></nav><div className="workspace-route-tools"><label className="workspace-jump"><Search aria-hidden="true"/><span className="sr-only">انتقل مباشرة إلى</span><select aria-label="انتقل مباشرة إلى" value="" onChange={event => { if(event.target.value) router.push(event.target.value); }}><option value="" disabled>انتقل مباشرة إلى…</option>{navigation.map(module => <optgroup key={module.id} label={module.title}>{module.links.map(link => <option key={link.href} value={link.href}>{link.label}</option>)}</optgroup>)}</select></label><button type="button" className="workspace-mode" aria-label={mode==="dark"?"تفعيل المظهر الفاتح":"تفعيل المظهر الداكن"} title={mode==="dark"?"المظهر الفاتح":"المظهر الداكن"} onClick={()=>saveWebAppearance(mode==="dark"?"light":"dark")}>{mode==="dark"?<Sun aria-hidden="true"/>:<Moon aria-hidden="true"/>}</button></div></div>}
      {saveNotice && <div className="save-notice" role="status"><Check size={18} /><span>{saveNotice}</span><button type="button" aria-label="إغلاق رسالة النجاح" onClick={() => setSaveNotice("")}><X size={16} /></button></div>}<DashboardPermissionsContext.Provider value={permissions}>{children}</DashboardPermissionsContext.Provider></DashboardPathContext.Provider></main>
  </div>;
}

export type HeaderAction = { label: string; href: string; tone?: "primary" | "secondary" };
export function ContextHelp({ title = "إرشادات الصفحة", children }: { title?: string; children: ReactNode }) {
  return <details className="workspace-context-help"><summary><Info size={16} aria-hidden="true"/><span>{title}</span></summary><div className="workspace-help-content">{children}</div></details>;
}
export function PageHeader({ title, context, description, action, secondaryActions = [], back }: { title: string; context: string; description?: string; action?: HeaderAction; secondaryActions?: HeaderAction[]; back?: Pick<HeaderAction, "label" | "href"> }) {
  const path = useContext(DashboardPathContext);
  const parent = path ? dashboardParentPath(path) : null;
  const parentName = modules.flatMap(module => module.links).find(link => link.href === parent?.split("?")[0])?.label;
  const parentTitle = parentName === "اللاعبون" ? "اللاعبين" : parentName;
  const backLabel = parent === "/dashboard" ? "العودة إلى الرئيسية" : parentTitle ? `العودة إلى ${parentTitle}` : parent && /\/[a-f\d-]{20,}$/i.test(parent) ? "العودة إلى الملف" : "العودة إلى القائمة";
  const actions = [...secondaryActions.map(item=>({...item,tone:item.tone??"secondary" as const})), ...(action ? [action] : [])];
  const backLink = back ?? (parent && !actions.some(item => /عودة|رجوع/.test(item.label)) ? { href: parent, label: backLabel } : null);
  return <header className="page-header" aria-label={context}><div className="page-title-block">{backLink && <Link className="page-back-link" href={backLink.href}><ArrowRight aria-hidden="true"/>{backLink.label}</Link>}<h1>{title}</h1></div>{(actions.length > 0 || description) && <div className="page-header-actions">{description && <ContextHelp><p>{description}</p></ContextHelp>}{actions.map(item => <Link className={`button ${item.tone === "secondary" ? "button-secondary" : "button-primary"}`} href={item.href} key={`${item.label}-${item.href}`}>{/عودة|رجوع/.test(item.label) ? <ArrowRight /> : /تعديل/.test(item.label) ? <Pencil /> : /إضافة|إنشاء|تسجيل/.test(item.label) ? <Plus /> : <FileText />}{item.label}</Link>)}</div>}</header>;
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
  const menuId = useId();
  useEffect(() => {
    const dismiss = (event: PointerEvent) => { if (event.target instanceof Node && !detailsRef.current?.contains(event.target)) detailsRef.current?.removeAttribute("open"); };
    document.addEventListener("pointerdown", dismiss);
    return () => document.removeEventListener("pointerdown", dismiss);
  }, []);
  if (actions.length === 0) return null;
  function close(restoreFocus = false) {
    detailsRef.current?.removeAttribute("open");
    if (restoreFocus) detailsRef.current?.querySelector("summary")?.focus();
  }
  return <details ref={detailsRef} className="row-action-menu" onBlur={event => { if (event.relatedTarget instanceof Node && !event.currentTarget.contains(event.relatedTarget)) close(); }} onKeyDown={event => {
    if (event.key === "Escape" && detailsRef.current?.open) { event.preventDefault(); event.stopPropagation(); close(true); return; }
    if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    detailsRef.current?.setAttribute("open", "");
    const controls = Array.from(detailsRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? []);
    const current = controls.indexOf(document.activeElement as HTMLElement);
    const next = event.key === "Home" ? 0 : event.key === "End" ? controls.length - 1 : event.key === "ArrowDown" ? (current + 1) % controls.length : (current < 0 ? controls.length - 1 : (current + controls.length - 1) % controls.length);
    controls[next]?.focus();
  }}><summary role="button" aria-haspopup="menu" aria-controls={menuId} aria-label={label}><MoreHorizontal aria-hidden="true" /></summary><div id={menuId} className="row-action-popover" role="menu" aria-label="الإجراءات الإضافية">{actions.map(action => action.href
    ? <Link role="menuitem" className={action.tone === "danger" ? "danger" : ""} href={action.href} onClick={() => close()} key={`${action.label}-${action.href}`}>{action.label}</Link>
    : <button role="menuitem" className={action.tone === "danger" ? "danger" : ""} type="button" onClick={() => { close(true); action.onClick?.(); }} key={action.label}>{action.label}</button>)}</div></details>;
}
export function RowActions({ actions }: { actions: RowAction[] }) {
  if (actions.length === 0) return null;
  return <div className="row-actions"><ActionControl key={`${actions[0].label}-${actions[0].href ?? "button"}`} action={actions[0]} />{actions.length === 2 ? <ActionControl key={`${actions[1].label}-${actions[1].href ?? "button"}`} action={actions[1]} /> : <RowActionMenu actions={actions.slice(1)} />}</div>;
}

export function SearchInput({ value, onChange, label = "بحث", placeholder = "بحث…" }: { value: string; onChange: (value: string) => void; label?: string; placeholder?: string }) {
  return <label className="search-input"><Search aria-hidden="true" /><span className="sr-only">{label}</span><input aria-label={label} value={value} onChange={event => onChange(event.target.value)} placeholder={placeholder} /></label>;
}
export function ListSearch(props: { value: string; onChange: (value: string) => void; label?: string; placeholder?: string }) { return <div className="filter-bar"><SearchInput {...props} /></div>; }
export function FilterToolbar({ children, resultCount, onReset, advanced, activeAdvancedCount = 0 }: { children: ReactNode; resultCount?: number; onReset?: () => void; advanced?: ReactNode; activeAdvancedCount?: number }) {
  return <div className="filter-toolbar"><div className="filter-toolbar-fields"><span className="filter-toolbar-icon" aria-hidden="true"><Filter /></span>{children}</div>{(resultCount !== undefined || onReset) && <div className="filter-toolbar-meta">{resultCount !== undefined && <span>{resultCount} نتيجة</span>}{onReset && <button className="button button-ghost" type="button" onClick={onReset}>إعادة الضبط</button>}</div>}{advanced && <details className="toolbar-advanced"><summary>فلاتر وترتيب إضافي{activeAdvancedCount>0&&<span className="status-badge status-info">{activeAdvancedCount} مطبق</span>}</summary><div className="filter-toolbar-fields">{advanced}</div></details>}</div>;
}

type StatusTone = "success" | "warning" | "danger" | "info" | "neutral";
const statusMap: Record<string, { label: string; tone: StatusTone }> = {
  Late: { label: "متأخر", tone: "warning" }, Excused: { label: "معتذر", tone: "info" },
  Created: { label: "تم الإنشاء", tone: "info" }, PendingPayment: { label: "بانتظار الدفع", tone: "warning" }, Archived: { label: "مؤرشف", tone: "neutral" },
  Active: { label: "فعال", tone: "success" }, Confirmed: { label: "مؤكد", tone: "success" }, Paid: { label: "مدفوع", tone: "success" }, Approved: { label: "مقبول", tone: "success" }, Present: { label: "حاضر", tone: "success" }, Published: { label: "منشور", tone: "success" }, PublishedInternal: { label: "للاستخدام الداخلي", tone: "info" }, Held: { label: "تمت", tone: "success" }, Reviewed: { label: "بيانات مراجعة", tone: "success" },
  Expiring: { label: "ينتهي قريبًا", tone: "warning" }, Pending: { label: "قيد الانتظار", tone: "warning" }, UnderReview: { label: "قيد المراجعة", tone: "warning" }, Frozen: { label: "مجمد", tone: "warning" }, PaymentInProgress: { label: "جارٍ الدفع", tone: "warning" }, DemoUnreviewed: { label: "بيانات تجريبية غير مراجعة", tone: "warning" },
  Expired: { label: "منتهي", tone: "danger" }, Rejected: { label: "مرفوض", tone: "danger" }, Absent: { label: "غائب", tone: "danger" }, Cancelled: { label: "ملغي", tone: "danger" }, Failed: { label: "فشل", tone: "danger" },
  Scheduled: { label: "مجدولة", tone: "info" }, Draft: { label: "مسودة", tone: "neutral" }, NotRecorded: { label: "لم يُسجل", tone: "neutral" }, Inactive: { label: "متوقف", tone: "neutral" }, Open: { label: "مفتوح", tone: "info" }, Monitoring: { label: "متابعة", tone: "warning" }, Resolved: { label: "مغلق", tone: "success" },
};
export function StatusBadge({ status, label }: { status: string; label?: string }) {
  const mapped = statusMap[status] ?? { label: /[\u0600-\u06ff]/.test(status) ? status : "غير معروفة", tone: "neutral" as const };
  return <span className={`status-badge status-${mapped.tone}`} data-status={status}><span aria-hidden="true" />{label ?? mapped.label}</span>;
}

export type DataColumn<T> = { key: string; header: string; width?: string; primary?: boolean; render: (row: T) => ReactNode; onSort?: () => void; sortDirection?: "ascending" | "descending" | "none" };
type ServerPagination = { page: number; pageSize: number; total: number; onPageChange: (page: number) => void; onPageSizeChange: (size: number) => void };
type TableColumnVisibility = { hidden: string[]; onChange: (hidden: string[]) => void };
export function AdminDataTable<T>({ columns, rows, rowKey, rowClassName, label, serverPagination, paginate = true, showTableControls = true, columnVisibility, pageRange }: { columns: DataColumn<T>[]; rows: T[]; rowKey: (row: T) => string; rowClassName?: (row: T) => string; label: string; serverPagination?: ServerPagination; paginate?: boolean; showTableControls?: boolean; columnVisibility?: TableColumnVisibility; pageRange?: { first: number; total: number } }) {
  const [pageSize, setPageSize] = useState(25);
  const [localHidden, setLocalHidden] = useState<string[]>([]);
  const hidden = columnVisibility?.hidden ?? localHidden;
  const setHidden = columnVisibility?.onChange ?? setLocalHidden;
  const columnMenu = useRef<HTMLDetailsElement>(null);
  useEffect(() => {
    const dismiss = (event: PointerEvent) => {
      const menu = columnMenu.current;
      if (menu?.open && event.target instanceof Node && !menu.contains(event.target)) menu.open = false;
    };
    document.addEventListener("pointerdown", dismiss);
    return () => document.removeEventListener("pointerdown", dismiss);
  }, []);
  const signature = rows.map(rowKey).join("|");
  const [position, setPosition] = useState({ signature, page: 0 });
  const size = serverPagination?.pageSize ?? pageSize, total = serverPagination?.total ?? rows.length;
  const pages = Math.max(1, Math.ceil(total / size));
  const page = serverPagination?.page ?? (position.signature === signature ? Math.min(position.page, pages - 1) : 0);
  const changePage = (next: number) => serverPagination ? serverPagination.onPageChange(next) : setPosition({ signature, page: next });
  const visible = columns.filter(column => column.primary || !hidden.includes(column.key));
  const template = visible.map(column => column.width ?? "minmax(0, 1fr)").join(" ");
  const first = pageRange?.first ?? (paginate ? page * size : 0);
  return <div className="data-workspace">
    {showTableControls && <div className="table-controls"><span>{pageRange ? <><bdi dir="ltr">{first + 1}–{first + rows.length}</bdi> من {pageRange.total}</> : `${total} ${paginate ? "سجل" : "سجل في هذه الصفحة"}`}</span><details ref={columnMenu} className="table-columns" onKeyDown={event => {
      if (event.key !== "Escape" || !event.currentTarget.open) return;
      event.preventDefault();
      event.stopPropagation();
      event.currentTarget.open = false;
      event.currentTarget.querySelector("summary")?.focus();
    }}><summary>الأعمدة</summary><div>{columns.map(column => <label key={column.key}><input type="checkbox" aria-label={`إظهار عمود ${column.header}`} checked={visible.includes(column)} disabled={column.primary || visible.length === 1 && visible.includes(column)} onChange={event => setHidden(event.target.checked ? hidden.filter(key => key !== column.key) : [...hidden, column.key])} />إظهار عمود {column.header}</label>)}</div></details></div>}
    <div className="admin-data-table" role="table" aria-label={label} aria-rowcount={(pageRange?.total ?? total) + 1} aria-colcount={visible.length} style={{ "--table-columns": template } as CSSProperties}>
      <div className="admin-data-header" role="row" aria-rowindex={1}>{visible.map(column => <div role="columnheader" aria-sort={column.sortDirection} key={column.key}>{column.onSort ? <button className="table-sort" type="button" onClick={column.onSort}>{column.header} <span aria-hidden="true">{column.sortDirection === "ascending" ? "↑" : column.sortDirection === "descending" ? "↓" : "↕"}</span></button> : column.header}</div>)}</div>
      <div className="admin-data-body" role="rowgroup">{(serverPagination || !paginate ? rows : rows.slice(first, first + size)).map((row, index) => <article className={`admin-data-row ${rowClassName?.(row) ?? ""}`.trim()} role="row" aria-rowindex={first + index + 2} key={rowKey(row)}>{visible.map(column => <div className={column.primary ? "data-primary" : ""} role="cell" data-label={column.header} key={column.key}>{column.render(row)}</div>)}</article>)}</div>
    </div>
    {paginate && <nav className="table-pagination" aria-label={`صفحات ${label}`}><label>صفوف الصفحة<select value={size} onChange={event => { const value = Number(event.target.value); if (serverPagination) serverPagination.onPageSizeChange(value); else { setPageSize(value); setPosition({ signature, page: 0 }); } }}>{[10, 25, 50].map(value => <option key={value} value={value}>{value}</option>)}</select></label><span role="status">{total ? first + 1 : 0}–{Math.min(first + size, total)} من {total}</span><button type="button" disabled={page === 0} onClick={() => changePage(page - 1)} aria-label="الصفحة السابقة"><ChevronDown style={{ transform: "rotate(-90deg)" }} /></button><button type="button" disabled={page >= pages - 1} onClick={() => changePage(page + 1)} aria-label="الصفحة التالية"><ChevronDown style={{ transform: "rotate(90deg)" }} /></button></nav>}
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

export function ConfirmationDialog({ open, title, message, confirmLabel, cancelLabel = "إلغاء", busy, busyLabel = "جارٍ الحفظ…", tone = "danger", onConfirm, onCancel }: { open: boolean; title: string; message: string; confirmLabel: string; cancelLabel?: string; busy?: boolean; busyLabel?: string; tone?: "danger" | "primary"; onConfirm: () => void; onCancel: () => void }) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useRef<HTMLElement>(null);
  const id = useId();
  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    if (cancelRef.current?.disabled) dialogRef.current?.focus(); else cancelRef.current?.focus();
    const containFocus = (event: FocusEvent) => {
      if (event.target instanceof Node && !dialogRef.current?.contains(event.target)) dialogRef.current?.focus();
    };
    document.addEventListener("focusin", containFocus);
    return () => {
      document.removeEventListener("focusin", containFocus);
      document.body.style.overflow = overflow;
      if (previous?.isConnected) previous.focus();
    };
  }, [open]);
  useEffect(() => { if (open && busy) dialogRef.current?.focus(); }, [open, busy]);
  if (!open) return null;
  return <div className="dialog-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget && !busy) { event.preventDefault(); onCancel(); } }}>
    <section ref={dialogRef} tabIndex={-1} className={`confirmation-dialog dialog-${tone}`} role="dialog" aria-modal="true" aria-labelledby={`${id}-title`} aria-describedby={`${id}-message`} aria-busy={busy || undefined} onKeyDown={event => {
      if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); if (!busy) onCancel(); }
      if (event.key !== "Tab") return;
      const controls = focusableWithin(event.currentTarget), first = controls[0], last = controls[controls.length - 1];
      if (!first) { event.preventDefault(); event.currentTarget.focus(); }
      else if (event.shiftKey && (document.activeElement === first || document.activeElement === event.currentTarget)) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && (document.activeElement === last || document.activeElement === event.currentTarget)) { event.preventDefault(); first.focus(); }
    }}>
      <div className="dialog-icon" aria-hidden="true">{tone === "danger" ? <AlertCircle /> : <Check />}</div>
      <div><h2 id={`${id}-title`}>{title}</h2><p id={`${id}-message`}>{message}</p></div>
      <div className="form-actions"><Button variant={tone} disabled={busy} onClick={onConfirm}>{busy ? <><LoaderCircle className="spin" aria-hidden="true" /> {busyLabel}</> : confirmLabel}</Button><button ref={cancelRef} className="button button-secondary" type="button" disabled={busy} onClick={onCancel}>{cancelLabel}</button></div>
    </section>
  </div>;
}

export function LoadingState({ message = "جارٍ تحميل البيانات…" }: { message?: string }) { return <div className="state-message loading-state" role="status"><span>{message}</span><div className="sports-skeleton" aria-hidden="true"><i /><i /><i /></div></div>; }
export function ErrorState({ message }: { message: string }) { return <div className="state-message error-message" role="alert"><AlertCircle aria-hidden="true" /><span>{message}</span></div>; }
export function EmptyState({ message, title, action }: { message?: string; title?: string; action?: ReactNode }) { return <div className="empty-state"><FileText aria-hidden="true" />{title && <h3>{title}</h3>}{(message || !title) && <p>{message || "لا توجد سجلات مطابقة."}</p>}{action}</div>; }
export function preventDefaultSubmit(handler: () => void) { return (event: FormEvent) => { event.preventDefault(); handler(); }; }
