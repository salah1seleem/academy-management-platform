import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AdminDataTable } from "./dashboard-shell";
import { RecordTabs } from "./record-tabs";

const replace = vi.fn();
let query = new URLSearchParams("branch=b1&tab=training");
vi.mock("next/navigation", () => ({ usePathname: () => "/dashboard/players/p1", useSearchParams: () => query, useRouter: () => ({ replace }) }));
afterEach(() => { cleanup(); replace.mockReset(); query = new URLSearchParams("branch=b1&tab=training"); });
const rows = Array.from({ length: 61 }, (_, i) => ({ id: String(i), name: `لاعب تجريبي ${i + 1}`, group: "مجموعة تجريبية" }));
const columns = [{ key: "name", header: "الاسم", primary: true, render: (r: typeof rows[number]) => r.name }, { key: "group", header: "المجموعة", render: (r: typeof rows[number]) => r.group }];

describe("V2 dense workspace controls", () => {
  it("paginates actual loaded records, resets after filters, and preserves mandatory identity", () => {
    const { rerender } = render(<AdminDataTable label="اللاعبون" rows={rows} columns={columns} rowKey={r => r.id} />);
    expect(screen.getByRole("table")).toHaveAttribute("aria-rowcount", "62");
    expect(screen.getAllByRole("row")).toHaveLength(26);
    expect(screen.queryByText("لاعب تجريبي 26")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "الصفحة التالية" }));
    expect(screen.getByText("لاعب تجريبي 26")).toBeVisible();
    fireEvent.change(screen.getByRole("combobox", { name: "صفوف الصفحة" }), { target: { value: "10" } });
    expect(screen.getAllByRole("row")).toHaveLength(11);
    fireEvent.click(screen.getByRole("button", { name: "الصفحة التالية" }));
    rerender(<AdminDataTable label="اللاعبون" rows={rows.slice(0, 3)} columns={columns} rowKey={r => r.id} />);
    expect(screen.getByText("لاعب تجريبي 1")).toBeVisible();
    fireEvent.click(screen.getByText("الأعمدة"));
    expect(screen.getByRole("checkbox", { name: "إظهار عمود الاسم" })).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: "إظهار عمود المجموعة" }));
    expect(screen.queryByRole("columnheader", { name: "المجموعة" })).not.toBeInTheDocument();
    expect(screen.getAllByRole("row")).toHaveLength(4);
  });
  it("uses deep-link tabs, preserves unrelated filters, and supports RTL keyboard navigation", () => {
    const tabs = [{ id: "overview", label: "نظرة عامة", content: "هوية اللاعب" }, { id: "training", label: "التدريب", content: "مواعيد اللاعب" }];
    const { rerender } = render(<RecordTabs label="ملف اللاعب" tabs={tabs} />);
    expect(screen.getByRole("tab", { name: "التدريب" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tabpanel")).toHaveTextContent("مواعيد اللاعب");
    fireEvent.keyDown(screen.getByRole("tab", { name: "التدريب" }), { key: "ArrowRight" });
    expect(replace).toHaveBeenLastCalledWith("/dashboard/players/p1?branch=b1&tab=overview", { scroll: false });
    query = new URLSearchParams("tab=unknown"); rerender(<RecordTabs label="ملف اللاعب" tabs={tabs} />);
    expect(screen.getByRole("tabpanel")).toHaveTextContent("هوية اللاعب");
  });
});
