import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { StructureList } from "./structure-list";

vi.mock("next/navigation", () => ({ usePathname: () => "/dashboard/academy/branches", useRouter: () => ({ replace: vi.fn() }) }));
const branches = [{ id: "1", arabicName: "فرع مدينة نصر", isActive: true }, { id: "2", arabicName: "فرع التجمع الخامس", isActive: true }];

beforeEach(() => { global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => branches }); });

describe("structure list", () => {
  it("filters the rendered tenant-scoped items and exposes only functional actions", async () => {
    render(<StructureList kind="branches" />);
    expect(await screen.findByText("فرع مدينة نصر")).toBeVisible();
    fireEvent.change(screen.getByRole("textbox", { name: "بحث الفروع" }), { target: { value: "التجمع" } });
    await waitFor(() => expect(screen.queryByText("فرع مدينة نصر")).not.toBeInTheDocument());
    expect(screen.getByText("فرع التجمع الخامس")).toBeVisible();
    expect(screen.getByRole("link", { name: "عرض" })).toHaveAttribute("href", "/dashboard/academy/branches/2");
    expect(screen.getByRole("link", { name: "تعديل" })).toHaveAttribute("href", "/dashboard/academy/branches/2/edit");
    fireEvent.click(screen.getByRole("button", { name: "إيقاف" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("فرع التجمع الخامس");
  });
});
