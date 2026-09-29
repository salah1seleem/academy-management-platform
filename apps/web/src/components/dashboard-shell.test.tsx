import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AdminDataTable, DashboardShell, FilterToolbar, PageHeader, RowActions, SearchInput, StatusBadge } from "./dashboard-shell";

const replace = vi.fn();
let currentPath = "/dashboard/players";
vi.mock("next/navigation", () => ({ usePathname: () => currentPath, useRouter: () => ({ replace }) }));

beforeEach(() => { currentPath = "/dashboard/players"; replace.mockReset(); global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ displayName: "منى السيد", role: "AcademyAdmin", academyName: "أكاديمية النجوم" }) }); });
afterEach(cleanup);

describe("dashboard navigation governance", () => {
  it("auto-expands the current module, highlights its list route, and supports accordion toggling", () => {
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    const players = screen.getByRole("button", { name: /اللاعبون/ });
    expect(players).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: "قائمة اللاعبين" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("button", { name: /الأكاديمية/ })).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(players);
    expect(players).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(screen.getByRole("button", { name: /الأكاديمية/ }));
    expect(screen.getByRole("button", { name: /الأكاديمية/ })).toHaveAttribute("aria-expanded", "true");
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

  it("shows the subscriptions module and its approved sub-modules as an accordion", () => {
    currentPath = "/dashboard/subscriptions/plans";
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    const subscriptionModule = screen.getByRole("button", { name: /الاشتراكات/ });
    expect(subscriptionModule).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: "الباقات" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "طلبات الدفع" })).toBeVisible();
    expect(screen.getByRole("link", { name: "التحصيلات" })).toBeVisible();
    fireEvent.click(subscriptionModule); expect(subscriptionModule).toHaveAttribute("aria-expanded", "false");
  });

  it("shows the evaluation module with criteria, players and published reports", () => {
    currentPath = "/dashboard/evaluations";
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    const evaluationModule = screen.getByRole("button", { name: /التقييمات/ });
    expect(evaluationModule).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: "معايير التقييم" })).toBeVisible();
    expect(screen.getByRole("link", { name: "تقييمات اللاعبين" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "التقارير المنشورة" })).toBeVisible();
  });

  it("shows financial reports to administrators", () => {
    currentPath = "/dashboard/reports/financial";
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    expect(screen.getByRole("link", { name: "التقارير المالية" })).toBeVisible();
    expect(screen.getByRole("link", { name: "الإيصالات" })).toBeVisible();
  });

  it("hides financial reports from coaches while retaining attendance reports", async () => {
    currentPath = "/dashboard/reports/attendance";
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ displayName: "المدرب", role: "Coach" }) });
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    expect(await screen.findByRole("link", { name: "تقارير الحضور" })).toBeVisible();
    expect(screen.queryByRole("link", { name: "التقارير المالية" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "الإيصالات" })).not.toBeInTheDocument();
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
