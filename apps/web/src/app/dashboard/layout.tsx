import { DashboardShell } from "../../components/dashboard-shell";
import "./dashboard.css";
export default function Layout({ children }: { children: React.ReactNode }) { return <DashboardShell>{children}</DashboardShell>; }
