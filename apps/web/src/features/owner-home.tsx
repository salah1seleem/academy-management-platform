"use client";
import Link from "next/link";
import {EmptyState} from "../components/dashboard-shell";
export {RefinedOwnerHome as PremiumOwnerDashboard} from "./owner-home-refinement";
const number=(n:number)=>new Intl.NumberFormat("ar-EG-u-nu-latn",{maximumFractionDigits:2}).format(n);
export function DonutChart({ rows, center, caption }: { rows: { name: string; value: number; display?: string; href: string }[]; center: string; caption: string }) {
  const total = rows.reduce((n, r) => n + Math.max(0, r.value), 0); const colors = ["var(--staff-primary-700)", "#F0B849", "#4D78C9", "#76938C", "#AF72BC", "#58B4C1"];
  if (!total) return <EmptyState message="لا توجد سجلات ضمن الفلاتر الحالية." />;
  return <div className="donut-visual"><div className="donut-plot"><svg viewBox="0 0 120 120" role="img" aria-label={rows.map(r => `${r.name}: ${r.display ?? number(r.value)}`).join("، ")}><circle cx="60" cy="60" r="48" fill="none" stroke="var(--staff-border)" strokeWidth="12" />{rows.map((r, i) => { const length = Math.max(0, r.value) / total * 301.593; const start = rows.slice(0, i).reduce((sum, row) => sum + Math.max(0, row.value), 0) / total * 301.593; return <circle key={r.name} cx="60" cy="60" r="48" fill="none" stroke={colors[i % colors.length]} strokeWidth="12" strokeDasharray={`${length} ${301.593 - length}`} strokeDashoffset={-start} transform="rotate(-90 60 60)" />; })}</svg><div className="donut-center"><b>{center}</b><span>{caption}</span></div></div><ul className="donut-legend">{rows.map((r, i) => <li key={r.name}><Link href={r.href}><i style={{ background: colors[i % colors.length] }} /><span>{r.name}</span><b>{r.display ?? number(r.value)}</b></Link></li>)}</ul></div>;
}
