import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { DateFilterDefaults } from "../components/date-filter-defaults";
import { monthToDate } from "../lib/date-range";
import { FinancialReport, AttendanceReport } from "./reports";
import { DailyStaffReport } from "./daily-staff-report";
import { PremiumOwnerDashboard } from "./owner-home";
import { PaymentRequestQueue } from "./manual-payments";
import { TrainingSessionsList } from "./attendance";
import { GroupSessions } from "./group-sessions";
import { MatchesWorkspace } from "./matches";
import { ContentList } from "./admin-content";
import { AdminEnrollmentRequests } from "./guardian-core";

const incoming = new URLSearchParams();
vi.mock("next/navigation", () => ({ useSearchParams: () => incoming, useRouter: () => ({ push: vi.fn() }) }));
const range = { from: "2026-10-01", to: "2026-10-08" };
const response = (v: unknown) => Promise.resolve(new Response(JSON.stringify(v)));
beforeEach(() => {
  incoming.delete("from"); incoming.delete("to"); incoming.delete("subject");
  global.fetch = vi.fn(input => {
    const u = String(input);
    if (u.endsWith("/me")) return response({ role: "AcademyOwner" });
    if (u.includes("owner-summary")) return response({ from: "2026-09-01", to: "2026-09-28", currency: "EGP", collections: { today: 0, month: 0 }, payments: { pending: 0 }, subscriptions: { active: 0, expiring: 0, expired: 0 }, activePlayers: 0, coaches: 0, attendance: { present: 0, absent: 0, notRecorded: 0 }, byBranch: [] });
    if (u.includes("sessions/options")) return response({ groups: [], coaches: [], players: [], guardians: [], branches: [], sports: [] });
    if (u.includes("matches/options")) return response({ tournaments: [], teams: [], players: [], coaches: [] });
    if (u.includes("structure/options")) return response({ groups: [], sports: [], branches: [] });
    if (u.includes("payment-settings")) return response({ configured: false });
    if (u.includes("payment-requests")) return response({ from: "2026-09-01", to: "2026-09-28", pending: 0, newEnrollments: 0, renewals: 0, products: 0, items: [] });
    if (u.includes("reports/")) return response({ from: "2026-09-01", to: "2026-09-28", totalCount: 0, totalAmount: 0, totalDeductions: 0, currency: "EGP", items: [] });
    if (u.includes("staff-attendance")) return response({ items: [] });
    return response([]);
  }) as typeof fetch;
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it.each([
  ["الحصص", <GroupSessions key="sessions" initialDate="2026-10-08" />, "مسح الفلاتر"],
  ["الرئيسية", <PremiumOwnerDashboard key="home" />, "مسح"],
  ["المالية", <FinancialReport key="financial" />, "مسح"],
  ["حضور اللاعبين", <AttendanceReport key="attendance" />, "مسح"],
  ["حضور المدربين", <DailyStaffReport key="staff" onPlayers={() => {}} />, "مسح"],
  ["طلبات الدفع", <PaymentRequestQueue key="payments" />, "مسح"],
  ["قائمة الحضور", <TrainingSessionsList key="register" />, "مسح"],
  ["المباريات", <MatchesWorkspace key="matches" />, "مسح"],
  ["السجلات الطبية", <ContentList key="medical" kind="medical" />, "إعادة الضبط"],
  ["طلبات التسجيل القديمة", <AdminEnrollmentRequests key="enrollments" />, "إعادة الضبط"],
] as const)("%s: starts month-to-date and clear remains blank after async responses", async (_name, content, reset) => {
  render(<DateFilterDefaults range={range}>{content}</DateFilterDefaults>);
  const from = await screen.findByLabelText("من تاريخ"), to = screen.getByLabelText("إلى تاريخ");
  expect(from).toHaveValue(_name === "قائمة الحضور" ? range.to : range.from); expect(to).toHaveValue(range.to);
  fireEvent.click(await screen.findByRole("button", { name: reset }));
  await waitFor(() => { expect(from).toHaveValue(""); expect(to).toHaveValue(""); });
  await Promise.resolve(); await Promise.resolve();
  expect(from).toHaveValue(""); expect(to).toHaveValue("");
});
it("keeps an explicitly linked report period instead of replacing it with defaults", async () => {
  incoming.set("from", "2026-09-01"); incoming.set("to", "2026-09-28");
  render(<DateFilterDefaults range={range}><FinancialReport /></DateFilterDefaults>);
  expect(await screen.findByLabelText("من تاريخ")).toHaveValue("2026-09-01");
  expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("2026-09-28");
  await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
});
it("uses Cairo month rollover and leap-day dates without changing the Demo clock", () => {
  expect(monthToDate(new Date("2026-09-30T22:30:00Z"))).toEqual({ from: "2026-10-01", to: "2026-10-01" });
  expect(monthToDate(new Date("2024-02-29T10:00:00Z"))).toEqual({ from: "2024-02-01", to: "2024-02-29" });
});
it("keeps an explicit date range when opening the daily coach report", async () => {
  incoming.set("subject", "staff"); incoming.set("from", "2026-09-10"); incoming.set("to", "2026-09-20");
  render(<DateFilterDefaults range={range}><AttendanceReport /></DateFilterDefaults>);
  expect(await screen.findByLabelText("من تاريخ")).toHaveValue("2026-09-10");
  expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("2026-09-20");
  await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/reports/staff-daily?from=2026-09-10&to=2026-09-20"));
});
