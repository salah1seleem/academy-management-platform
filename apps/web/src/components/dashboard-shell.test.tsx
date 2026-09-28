import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DashboardShell, PageHeader, RowActions } from "./dashboard-shell";

const replace = vi.fn();
let currentPath = "/dashboard/players";
vi.mock("next/navigation", () => ({ usePathname: () => currentPath, useRouter: () => ({ replace }) }));

beforeEach(() => { currentPath = "/dashboard/players"; replace.mockReset(); global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ displayName: "منى السيد", role: "AcademyAdmin" }) }); });
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
});
