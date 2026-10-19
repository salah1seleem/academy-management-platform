import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { DonutChart, PremiumOwnerDashboard } from "./owner-home";
import { AcademySettings } from "./academy-settings";
import { DateFilterDefaults } from "../components/date-filter-defaults";
// These independently fetched panels have their own source/interaction suites.
vi.mock("./owner-cockpit",()=>({OwnerCockpit:()=>null}));
vi.mock("./operating-statement",()=>({OperatingStatementSnapshot:()=>null}));
const incoming=new URLSearchParams();
vi.mock("next/navigation",()=>({useSearchParams:()=>incoming}));
afterEach(()=>{cleanup();for(const key of [...incoming.keys()])incoming.delete(key);});
it("incoming date and branch scope is reflected by the controls and actual source requests",async()=>{
  incoming.set("from","2026-09-01");incoming.set("to","2026-09-28");incoming.set("branchId","b1");
  const fetcher=vi.fn((input)=>String(input).endsWith("/me")?response({role:"AcademyOwner"}):String(input).includes("options")?response({branches:[{id:"b1",arabicName:"فرع اصطناعي"}]}):response(summary));global.fetch=fetcher as typeof fetch;
  render(<DateFilterDefaults range={{from:"2026-10-01",to:"2026-10-19"}}><PremiumOwnerDashboard/></DateFilterDefaults>);
  expect(await screen.findByLabelText("من تاريخ")).toHaveValue("2026-09-01");expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("2026-09-28");
  await waitFor(()=>expect(fetcher.mock.calls.some(([p])=>String(p).includes("owner-summary?from=2026-09-01&to=2026-09-28&branchId=b1"))).toBe(true));
});
it("requests have their own actionable owner drill-down, not a complaint score", async () => {
  const data={...summary,operations:{coachPerformance:[],groupAttention:[],pendingExpenses:[],unpaidExpenses:[],receivables:[],accessChanges:0,openRequests:2,openComplaints:0,openSuggestions:0}};
  global.fetch=vi.fn(input=>String(input).endsWith("/me")?response({role:"AcademyOwner"}):String(input).includes("options")?response({branches:[{id:"b1",arabicName:"فرع اصطناعي"}]}):response(data)) as typeof fetch;
  render(<DateFilterDefaults range={{from:"2026-10-01",to:"2026-10-19"}}><PremiumOwnerDashboard/></DateFilterDefaults>);
  expect(await screen.findByRole("link",{name:/راجع طلبات الأسر/})).toHaveAttribute("href","/dashboard/feedback?kind=Request&status=Unresolved&allDates=true");
  fireEvent.click(screen.getByRole("button",{name:/الفترة والفرع/}));fireEvent.change(screen.getByLabelText("الفرع"),{target:{value:"b1"}});
  await waitFor(()=>expect(screen.getByRole("link",{name:/راجع طلبات الأسر/})).toHaveAttribute("href","/dashboard/feedback?kind=Request&status=Unresolved&branchId=b1&allDates=true"));
});
const response = (v: unknown) => Promise.resolve(new Response(JSON.stringify(v), { headers: { "Content-Type": "application/json" } }));
const summary = { from: "2026-09-01", to: "2026-09-28", currency: "EGP", collections: { today: 1800, month: 5600 }, subscriptions: { total: 30, active: 20, expiring: 5, expired: 7 }, payments: { pending: 4 }, paymentRequests: { total: 4, newEnrollments: 1, renewals: 1, products: 1, tournaments: 1 }, attendance: { present: 10, absent: 2, notRecorded: 1 }, byBranch: [{ id: "b1", name: "فرع تجريبي", amount: 1800 }], activePlayers: 20, coaches: 3, newSubscriptions: 2 };
it("drills down from a real post-contract payable without presenting it as monthly base salary",async()=>{
  const data={...summary,payroll:{month:"2026-10-01",employeeCount:3,expiringContracts:0,baseDue:9000,deductions:0,additions:0,netDue:9000,unpaid:0,postContract:[{currency:"EGP",remaining:30,count:1}],postContractEmployees:[{employeeId:"ended",name:"مدرب عقد منتهٍ",currency:"EGP",remaining:30}]}};global.fetch=vi.fn(url=>String(url).endsWith("/me")?response({role:"AcademyOwner"}):String(url).includes("options")?response({branches:[]}):response(data)) as typeof fetch;
  render(<DateFilterDefaults range={{from:"2026-10-01",to:"2026-10-15"}}><PremiumOwnerDashboard/></DateFilterDefaults>);const link=await screen.findByRole("link",{name:/مدرب عقد منتهٍ/});expect(link).toHaveAttribute("href","/dashboard/payroll/ended/account");expect(link).toHaveTextContent("30 جنيه");expect(screen.getByText(/مستقل عن شهر الرسم/)).toBeVisible();
});
it("has no welcome/latest blocks and every total/category retains matching drill-down", async () => {
  global.fetch = vi.fn(input => String(input).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(input).includes("options") ? response({ branches: [{ id: "b1", arabicName: "فرع تجريبي" }] }) : response(summary)) as typeof fetch;
  render(<DateFilterDefaults range={{ from: "2026-09-01", to: "2026-09-28" }}><PremiumOwnerDashboard /></DateFilterDefaults>);
  const mobileFilters = await screen.findByRole("button", { name: /الفترة والفرع/ });
  expect(mobileFilters).toHaveAttribute("aria-expanded", "false");
  fireEvent.click(mobileFilters);
  expect(mobileFilters).toHaveAttribute("aria-expanded", "true");
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
  const operational = { ...summary, paymentRequests: { total: 0, newEnrollments: 0, renewals: 0, products: 0 }, operations: { coachPerformance: [{ id: "coach-1", name: "مدرب تجريبي", groupCount: 2, sessionCount: 7, publishedEvaluations: 3, parentRating: 4.5, parentRatingCount: 2, complaintCount: 1 }], groupAttention: [{ id: "group-1", name: "مجموعة تجربة", hasCoach: false, sessions: 1, absent: 0, notRecorded: 0 }], pendingExpenses: [{ currency: "EGP", count: 1, amount: 200 }], unpaidExpenses: [{ currency: "EGP", count: 2, amount: 450 }], receivables: [{ currency: "EGP", outstanding: 1740, overdue: 1240, openCount: 3 }], accessChanges: 2, pendingPaymentBacklog: 7, openComplaints: 1, openSuggestions: 1, overdueFeedback: 1, attentionRule: "غياب متكرر" } };
  global.fetch = vi.fn(input => String(input).endsWith("/me") ? response({ role: "AcademyOwner" }) : String(input).includes("options") ? response({ branches: [] }) : response(operational)) as typeof fetch;
  render(<DateFilterDefaults range={{ from: "2026-09-01", to: "2026-09-28" }}><PremiumOwnerDashboard /></DateFilterDefaults>);
  expect(await screen.findByText(/4\.5 \/ 5 · 2 تقييمات/)).toBeVisible();
  expect(screen.getByRole("heading",{name:"ملخص المدربين"})).toBeVisible();
  expect(screen.getByRole("link", { name: /طلبات دفع تحتاج قرارًا/ })).toHaveTextContent("7 طلب · كل الفترات");
  expect(screen.getByRole("link", { name: /طلبات دفع تحتاج قرارًا/ })).toHaveAttribute("href", expect.stringContaining("allDates=true"));
  expect(screen.getByRole("link", { name: /مجموعة تجربة/ })).toHaveAttribute("href", "/dashboard/academy/groups/group-1?tab=performance");
  expect(screen.getByRole("link", { name: /شكاوى تحتاج إغلاقًا/ })).toHaveAttribute("href", "/dashboard/feedback?kind=Complaint&status=Unresolved&allDates=true");
  expect(screen.getByRole("link", { name: /ملاحظات تجاوزت موعد المتابعة/ })).toHaveAttribute("href", expect.stringContaining("overdue=true"));
  expect(screen.getByRole("link", { name: /مصروفات معتمدة غير مرحّلة/ })).toHaveTextContent("200 جنيه");
  expect(screen.getByRole("link", { name: /مصروفات معتمدة غير مرحّلة/ })).toHaveAttribute("href", expect.stringContaining("status=Approved"));
  expect(screen.getByRole("link", { name: /مصروفات تشغيلية غير مسددة/ })).toHaveAttribute("href", expect.stringContaining("unpaid=true"));
  expect(screen.getByRole("link", { name: /مصروفات تشغيلية غير مسددة/ })).toHaveTextContent("450 جنيه");
  expect(screen.getByRole("link", { name: /رصيد قائم حتى الآن/ })).toHaveTextContent("1,740 جنيه");
  expect(screen.getByRole("link", { name: /رصيد قائم حتى الآن/ })).toHaveAttribute("href", expect.stringContaining("state=open"));
  expect(screen.getByRole("link", { name: /منه متأخر/ })).toHaveTextContent("1,240 جنيه");
  expect(screen.getByRole("link", { name: /منه متأخر/ })).toHaveAttribute("href", expect.stringContaining("state=overdue"));
  fireEvent.click(screen.getByRole("button", { name: "مسح" }));
  await waitFor(() => expect(screen.getByRole("link", { name: /مدرب تجريبي/ })).toHaveAttribute("href", expect.stringContaining("allDates=true")));
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
