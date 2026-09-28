import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DashboardShell, PageHeader, RowActions } from "./dashboard-shell";
vi.mock("next/navigation", () => ({ usePathname: () => "/dashboard/players", useRouter: () => ({ replace: vi.fn() }) }));
global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ displayName: "منى السيد", role: "AcademyAdmin" }) });
describe("dashboard navigation governance", () => { it("renders Arabic sidebar, active list entry, create and row actions", async () => { render(<DashboardShell><PageHeader title="قائمة اللاعبين" context="اللاعبون / قائمة اللاعبين" action={{ label: "تسجيل لاعب جديد", href: "/dashboard/players/new" }} /><RowActions /></DashboardShell>); expect(screen.getByRole("complementary", { name: "التنقل الرئيسي" })).toBeInTheDocument(); expect(screen.getByRole("link", { name: "قائمة اللاعبين" })).toHaveAttribute("aria-current", "page"); expect(screen.getAllByRole("link", { name: "تسجيل لاعب جديد" })[1]).toBeVisible(); expect(screen.getByRole("button", { name: "تعديل" })).toBeVisible(); expect(screen.getByRole("button", { name: "فتح القائمة" })).toBeVisible(); }); });
