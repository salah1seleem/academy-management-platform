import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StructureList } from "./structure-list";

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(), usePathname: () => "/dashboard/academy/branches", useRouter: () => ({ replace: vi.fn() }) }));
const branches = [{ id: "1", arabicName: "فرع مدينة نصر", isActive: true }, { id: "2", arabicName: "فرع التجمع الخامس", isActive: true }];

beforeEach(() => { global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => branches }); });
afterEach(cleanup);

describe("structure list", () => {
  it("filters the rendered tenant-scoped items and exposes only functional actions", async () => {
    render(<StructureList kind="branches" />);
    expect(await screen.findByText("فرع مدينة نصر")).toBeVisible();
    fireEvent.change(screen.getByRole("textbox", { name: "بحث الفروع" }), { target: { value: "التجمع" } });
    await waitFor(() => expect(screen.queryByText("فرع مدينة نصر")).not.toBeInTheDocument());
    expect(screen.getByText("فرع التجمع الخامس")).toBeVisible();
    expect(screen.getByRole("link", { name: "عرض" })).toHaveAttribute("href", "/dashboard/academy/branches/2");
    fireEvent.click(screen.getByLabelText("المزيد من الإجراءات"));
    expect(screen.getByRole("menuitem", { name: "تعديل" })).toHaveAttribute("href", "/dashboard/academy/branches/2/edit");
    fireEvent.click(screen.getByRole("menuitem", { name: "إيقاف" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("فرع التجمع الخامس");
  });
  it("offers status control and keeps a selected coach in the assignment link", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [{ id: "coach-1", arabicName: "كريم حسن", isActive: true, assignedGroups: 1 }] });
    render(<StructureList kind="coaches" />);
    expect(await screen.findByText("كريم حسن")).toBeVisible();
    fireEvent.click(screen.getByLabelText("المزيد من الإجراءات"));
    expect(screen.getByRole("menuitem", { name: "إسناد مجموعة" })).toHaveAttribute("href", "/dashboard/academy/coaches/new?coachId=coach-1");
    fireEvent.click(screen.getByRole("menuitem", { name: "إيقاف" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("كريم حسن");
  });
});
