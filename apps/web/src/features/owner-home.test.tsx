import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { DonutChart, PremiumOwnerDashboard } from "./owner-home";
import { AcademySettings } from "./academy-settings";
import { DateFilterDefaults } from "../components/date-filter-defaults";
afterEach(cleanup);
const response = (v: unknown) => Promise.resolve(new Response(JSON.stringify(v), { headers: { "Content-Type": "application/json" } }));
const summary = { from: "2026-09-01", to: "2026-09-28", currency: "EGP", collections: { today: 1800, month: 5600 }, subscriptions: { total: 30, active: 20, expiring: 5, expired: 7 }, payments: { pending: 4 }, paymentRequests: { total: 4, newEnrollments: 1, renewals: 1, products: 1, tournaments: 1 }, attendance: { present: 10, absent: 2, notRecorded: 1 }, byBranch: [{ id: "b1", name: "فرع تجريبي", amount: 1800 }], activePlayers: 20, coaches: 3, newSubscriptions: 2 };
it("has no welcome/latest blocks and every total/category retains matching drill-down", async () => {
  global.fetch = vi.fn(input => String(input).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(input).includes("options") ? response({ branches: [{ id: "b1", arabicName: "فرع تجريبي" }] }) : response(summary)) as typeof fetch;
  render(<DateFilterDefaults range={{ from: "2026-09-01", to: "2026-09-28" }}><PremiumOwnerDashboard /></DateFilterDefaults>);
  expect(await screen.findByRole("link", { name: "إجمالي الاشتراكات: 30" })).toHaveAttribute("href", expect.stringContaining("subscriptionState=subscribed"));
  expect(screen.getByRole("img", { name: /فعالة — لا تنتهي قريبًا/ })).toHaveAttribute("aria-label", "فعالة — لا تنتهي قريبًا: 15، تنتهي قريبًا: 5، منتهية: 7، مجمدة / لم تبدأ: 3");
  expect(screen.queryByText("أحدث التحصيلات")).not.toBeInTheDocument(); expect(screen.queryByText(/أهلًا/)).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: /بطولات/ })).toHaveAttribute("href", expect.stringContaining("type=Tournament"));
  fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "b1" } });
  await waitFor(() => expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining("branchId=b1")));
  expect(screen.getByRole("link", { name: "غائب2" })).toHaveAttribute("href", expect.stringContaining("status=Absent"));
});
it("does not invent a pie when there are no stored records", () => {
  render(<DonutChart center="0" caption="لاعبًا" rows={[{ name: "فعال", value: 0, href: "/players" }]} />);
  expect(screen.queryByRole("img")).not.toBeInTheDocument(); expect(screen.getByText("لا توجد سجلات ضمن الفلاتر الحالية.")).toBeVisible();
});
it("shows stored coach ratings and actionable family feedback without inventing an AI score", async () => {
  const operational = { ...summary, operations: { coachPerformance: [{ id: "coach-1", name: "مدرب تجريبي", groupCount: 2, sessionCount: 7, publishedEvaluations: 3, parentRating: 4.5, parentRatingCount: 2, complaintCount: 1 }], groupAttention: [], pendingExpenses: [{ currency: "EGP", count: 1, amount: 200 }], accessChanges: 2, openComplaints: 1, openSuggestions: 1, attentionRule: "غياب متكرر" } };
  global.fetch = vi.fn(input => String(input).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(input).includes("options") ? response({ branches: [] }) : response(operational)) as typeof fetch;
  render(<DateFilterDefaults range={{ from: "2026-09-01", to: "2026-09-28" }}><PremiumOwnerDashboard /></DateFilterDefaults>);
  expect(await screen.findByText(/4\.5\/5 من 2/)).toBeVisible();
  expect(screen.getByRole("link", { name: /شكاوى تحتاج إغلاقًا/ })).toHaveAttribute("href", "/dashboard/feedback?kind=Complaint&status=Unresolved");
  expect(screen.getByRole("link", { name: /مصروفات معتمدة غير مرحّلة/ })).toHaveTextContent("200 جنيه");
  expect(screen.getByRole("link", { name: /مصروفات معتمدة غير مرحّلة/ })).toHaveAttribute("href", expect.stringContaining("status=Approved"));
  expect(screen.queryByText(/ذكاء اصطناعي يحلل/)).not.toBeInTheDocument();
});
it("does not pretend historical refunds have a documented electronic method", async () => {
  const historic = { ...summary, collections: { today: -40, month: 5600, todayCash: 0, todayOnline: 0, todayUnspecified: -40, monthUnspecified: 0 } };
  global.fetch = vi.fn(input => String(input).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(input).includes("options") ? response({ branches: [] }) : response(historic)) as typeof fetch;
  render(<DateFilterDefaults range={{ from: "2026-09-01", to: "2026-09-28" }}><PremiumOwnerDashboard /></DateFilterDefaults>);
  const link = await screen.findByRole("link", { name: /طريقة صرف قديمة غير موثقة/ });
  expect(link).toHaveAttribute("href", expect.stringContaining("provider=Unspecified"));
  expect(link).toHaveTextContent("-40 جنيه");
});
it("an owner manages attendance/transfers without receiving platform branding controls", async () => {
  global.fetch = vi.fn(input => String(input).includes("experience") ? response({ displayName: "أكاديمية اختبار", platformBrandOperator: false, coachAttendanceEnabled: true }) : String(input).includes("my-permissions") ? response(["academy.settings.manage"]) : String(input).endsWith("/me") ? response({ role: "AcademyOwner" }) : response({ configured: false })) as typeof fetch;
  render(<AcademySettings />); expect(await screen.findByRole("checkbox")).toBeVisible(); expect(screen.queryByText("هوية الأكاديميات — مشغّل المنصة")).not.toBeInTheDocument(); expect(screen.queryByLabelText("رفع شعار")).not.toBeInTheDocument();
});
