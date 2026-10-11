import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AdminDataTable, DashboardShell, FilterToolbar, PageHeader, RowActions, SearchInput, StatusBadge } from "./dashboard-shell";

const replace = vi.fn();
let currentPath = "/dashboard/players";
vi.mock("next/navigation", () => ({ usePathname: () => currentPath, useRouter: () => ({ replace }) }));

beforeEach(() => { currentPath = "/dashboard/players"; replace.mockReset(); global.fetch = vi.fn(input => Promise.resolve(new Response(JSON.stringify(String(input).includes("my-permissions") ? ["people.manage", "structure.manage", "payment.read", "subscription.plan.manage", "evaluation.read", "evaluation.criteria.manage"] : { displayName: "منى السيد", role: "AcademyAdmin", academyName: "أكاديمية النجوم" }), { status: 200 }))) as typeof fetch; });
afterEach(cleanup);

describe("dashboard navigation governance", () => {
  it("auto-expands the current module, highlights its list route, and supports accordion toggling", async () => {
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    const players = await screen.findByRole("button", { name: "خيارات اللاعبين" });
    expect(players).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: "الأعضاء والاشتراكات" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("button", { name: "التشغيل اليومي" })).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(players);
    expect(players).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(screen.getByRole("button", { name: "التشغيل اليومي" }));
    expect(screen.getByRole("button", { name: "التشغيل اليومي" })).toHaveAttribute("aria-expanded", "true");
  });

  it("keeps the create action visible and row actions execute real links or callbacks", () => {
    const callback = vi.fn();
    render(<><PageHeader title="قائمة اللاعبين" context="اللاعبون / قائمة اللاعبين" action={{ label: "تسجيل لاعب جديد", href: "/dashboard/players/new" }} /><RowActions actions={[{ label: "عرض", href: "/dashboard/players/1" }, { label: "إيقاف", onClick: callback }]} /></>);
    expect(screen.getByRole("link", { name: "تسجيل لاعب جديد" })).toHaveAttribute("href", "/dashboard/players/new");
    expect(screen.getByRole("link", { name: "عرض" })).toHaveAttribute("href", "/dashboard/players/1");
    fireEvent.click(screen.getByRole("button", { name: "إيقاف" }));
    expect(callback).toHaveBeenCalledOnce();
  });

  it("opens and closes the mobile navigation drawer", () => {
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    fireEvent.click(screen.getByRole("button", { name: "فتح القائمة" }));
    expect(screen.getAllByRole("button", { name: "إغلاق القائمة" })[0]).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("complementary", { name: "التنقل الرئيسي" })).toHaveClass("open");
  });

  it("does not repurpose a read link when write permissions finish loading", () => {
    const due = { label: "المستحقات", href: "/dashboard/payroll?employeeId=1" };
    const { rerender } = render(<RowActions actions={[due]} />);
    const link = screen.getByRole("link", { name: "المستحقات" });
    rerender(<RowActions actions={[{ label: "تعديل", href: "/dashboard/staff/employees/1" }, due]} />);
    expect(screen.getByRole("link", { name: "المستحقات" })).toBe(link);
    expect(link).toHaveAttribute("href", due.href);
    expect(screen.getByRole("link", { name: "تعديل" })).not.toBe(link);
  });

  it("keeps plans and payments in the same members workspace without losing routes", async () => {
    currentPath = "/dashboard/subscriptions/plans";
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    expect(await screen.findByRole("link", { name: "الباقات" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "طلبات الدفع" })).toBeVisible();
    expect(screen.queryByRole("button", { name: "الاشتراكات" })).not.toBeInTheDocument();
    expect(screen.queryByText("نظرة عامة")).not.toBeInTheDocument();
    expect(screen.queryByText("الإدارة")).not.toBeInTheDocument();
  });

  it("shows the evaluation module with criteria and players, without the published reports sub-tab", async () => {
    currentPath = "/dashboard/evaluations";
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    const evaluationModule = await screen.findByRole("button", { name: "الفريق والتطوير" });
    expect(evaluationModule).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: "معايير التقييم" })).toBeVisible();
    expect(screen.getByRole("link", { name: "تقييمات اللاعبين" })).toHaveAttribute("aria-current", "page");
    expect(screen.queryByRole("link", { name: "التقارير المنشورة" })).not.toBeInTheDocument();
  });

  it("hides financial reports from administrators without an owner grant", async () => {
    currentPath = "/dashboard/reports/financial";
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    await screen.findAllByText("منى السيد");
    expect(screen.queryByRole("link", { name: "التقارير المالية" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "الإيصالات" })).not.toBeInTheDocument();
  });

  it("redirects coaches away from the web dashboard", async () => {
    currentPath = "/dashboard/reports/attendance";
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ displayName: "المدرب", role: "Coach" }) });
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/"));
  });

  it("keeps secondary row actions in an accessible overflow menu", () => {
    const callback = vi.fn();
    render(<RowActions actions={[{ label: "عرض", href: "/record/1" }, { label: "تعديل", href: "/record/1/edit" }, { label: "إيقاف", onClick: callback, tone: "danger" }]} />);
    expect(screen.getByRole("link", { name: "عرض" })).toBeVisible();
    fireEvent.click(screen.getByLabelText("المزيد من الإجراءات"));
    expect(screen.getByRole("menuitem", { name: "تعديل" })).toHaveAttribute("href", "/record/1/edit");
    fireEvent.click(screen.getByRole("menuitem", { name: "إيقاف" }));
    expect(callback).toHaveBeenCalledOnce();
  });

  it("renders Arabic status labels and a structured responsive table", () => {
    const rows = [{ id: "1", name: "عمر", status: "Active" }];
    render(<><StatusBadge status="Active" /><AdminDataTable label="قائمة تجريبية" rows={rows} rowKey={row => row.id} columns={[{ key: "name", header: "الاسم", primary: true, render: row => row.name }, { key: "status", header: "الحالة", render: row => <StatusBadge status={row.status} /> }]} /></>);
    expect(screen.getByRole("table", { name: "قائمة تجريبية" })).toBeVisible();
    expect(screen.getAllByText("فعال")).toHaveLength(2);
    expect(screen.queryByText("Active")).not.toBeInTheDocument();
  });

  it("exposes a labelled filter bar with result count and reset", () => {
    const reset = vi.fn();
    render(<FilterToolbar resultCount={7} onReset={reset}><SearchInput value="" onChange={() => {}} label="بحث السجلات" /></FilterToolbar>);
    expect(screen.getByRole("textbox", { name: "بحث السجلات" })).toBeVisible();
    expect(screen.getByText("7 نتيجة")).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "إعادة الضبط" }));
    expect(reset).toHaveBeenCalledOnce();
  });
});
