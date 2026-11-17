import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { RecordTabs } from "./record-tabs";

let query = new URLSearchParams("tab=finance");
const replace = vi.fn();
vi.mock("next/navigation", () => ({
  usePathname: () => "/dashboard/settings",
  useSearchParams: () => query,
  useRouter: () => ({ replace }),
}));
afterEach(() => { cleanup(); vi.restoreAllMocks(); replace.mockReset(); query = new URLSearchParams("tab=finance"); });
const tabs = [
  { id: "academy", label: "بيانات الأكاديمية", content: <label>ملاحظة<input /></label> },
  { id: "finance", label: "المالية والتحصيل", content: "حسابات التحويل" },
];

it("selects the deep link after tabs arrive, then falls back when that section is removed", () => {
  const view = render(<RecordTabs label="أقسام الإعدادات" tabs={[]} />);
  expect(view.container).toBeEmptyDOMElement();
  view.rerender(<RecordTabs label="أقسام الإعدادات" tabs={tabs} />);
  expect(screen.getByRole("tab", { name: "المالية والتحصيل" })).toHaveAttribute("aria-selected", "true");
  expect(screen.getByRole("tabpanel")).toHaveTextContent("حسابات التحويل");
  view.rerender(<RecordTabs label="أقسام الإعدادات" tabs={tabs.slice(0, 1)} />);
  expect(screen.queryByText("حسابات التحويل")).not.toBeInTheDocument();
  expect(screen.getByRole("tab", { name: "بيانات الأكاديمية" })).toHaveAttribute("aria-selected", "true");
});

it("preserves a mounted draft while the selected section changes and hides the inactive panel", () => {
  query = new URLSearchParams("tab=academy");
  const view = render(<RecordTabs label="أقسام الإعدادات" tabs={tabs} keepMounted />);
  fireEvent.change(screen.getByRole("textbox", { name: "ملاحظة" }), { target: { value: "ملاحظة اصطناعية" } });
  query = new URLSearchParams("tab=finance");
  view.rerender(<RecordTabs label="أقسام الإعدادات" tabs={tabs} keepMounted />);
  expect(screen.queryByRole("textbox", { name: "ملاحظة" })).not.toBeInTheDocument();
  expect(screen.getByRole("tabpanel")).toHaveTextContent("حسابات التحويل");
  query = new URLSearchParams("tab=academy");
  view.rerender(<RecordTabs label="أقسام الإعدادات" tabs={tabs} keepMounted />);
  expect(screen.getByRole("textbox", { name: "ملاحظة" })).toHaveValue("ملاحظة اصطناعية");
});

it("keeps filters and the existing RTL wrap behavior for Home, End and arrow keys", () => {
  query = new URLSearchParams("branchId=synthetic-branch&search=synthetic&tab=academy");
  render(<RecordTabs label="أقسام الإعدادات" tabs={tabs} />);
  const first = screen.getByRole("tab", { name: "بيانات الأكاديمية" });
  for (const key of ["End", "ArrowRight"]) {
    fireEvent.keyDown(first, { key });
    expect(replace).toHaveBeenLastCalledWith("/dashboard/settings?branchId=synthetic-branch&search=synthetic&tab=finance", { scroll: false });
  }
  const last = screen.getByRole("tab", { name: "المالية والتحصيل" });
  for (const key of ["Home", "ArrowLeft"]) {
    fireEvent.keyDown(last, { key });
    expect(replace).toHaveBeenLastCalledWith("/dashboard/settings?branchId=synthetic-branch&search=synthetic&tab=academy", { scroll: false });
  }
});
