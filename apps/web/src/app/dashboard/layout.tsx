import { DashboardShell } from "../../components/dashboard-shell";
import { Suspense } from "react";
import { connection } from "next/server";
import { DateFilterDefaults } from "../../components/date-filter-defaults";
import { monthToDate } from "../../lib/date-range";
import "./dashboard.css";
import "./premium-experience.css";
export default async function Layout({ children }: { children: React.ReactNode }) { await connection(); return <DateFilterDefaults range={monthToDate()}><DashboardShell><Suspense fallback={<p role="status">جارٍ التحميل…</p>}>{children}</Suspense></DashboardShell></DateFilterDefaults>; }
