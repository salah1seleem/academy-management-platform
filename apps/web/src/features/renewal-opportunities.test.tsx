import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { RenewalOpportunities, RenewalOpportunityCard } from "./renewal-opportunities";
import Page from "../app/dashboard/finance/renewals/page";
const report = { quoteHash: "hash", quotedAt: "2026-09-28T09:00:00Z", quoteDay: "2026-09-28", currency: "EGP", potential: "12700.00", count: 20, unpricedCount: 1,
  items: [{ enrollmentId: "e", playerId: "p", player: "لاعب اختبار", playerCode: "DEMO-1", branch: "التجمع", sport: "السباحة", group: "أ", plan: "باقة سابقة", requiresPlanSelection: true, quote: null, discount: null, currency: "EGP" }] };
const ok = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
afterEach(() => { cleanup(); vi.restoreAllMocks(); window.history.replaceState({}, "", "/"); });
function mock() { global.fetch = vi.fn(async url => String(url).includes("my-permissions") ? ok(["reports.export"]) : String(url).includes("/options") ? ok({ branches: [{ id: "b", arabicName: "التجمع" }], sports: [{ id: "s", arabicName: "السباحة" }], groups: [] }) : ok(report)) as typeof fetch; }
it("server rendering preserves the drill-down branch instead of hydrating all branches", async () => {
  mock(); render(await Page({ searchParams: Promise.resolve({ branchId: "b" }) }));
  await screen.findByRole("link", { name: "ملف اللاعب والتجديد" });
  expect(screen.getByLabelText("الفرع")).toHaveValue("b");
  expect(fetch).toHaveBeenCalledWith("/api/v1/finance/renewal-opportunities?branchId=b", expect.anything());
});
it("renders missing quotes explicitly, exports the same filters/hash and never creates a payment", async () => {
  mock(); render(<RenewalOpportunities initialBranchId="b" />);
  await screen.findByRole("link", { name: "ملف اللاعب والتجديد" }); expect(screen.getByText("تحتاج اختيار باقة", { exact: true })).toBeVisible();
  expect(screen.getByRole("link", { name: /تصدير CSV/ })).toHaveAttribute("href", "/api/v1/finance/renewal-opportunities/export?branchId=b&quoteHash=hash");
  expect(screen.getByRole("link", { name: "ملف اللاعب والتجديد" })).toHaveAttribute("href", "/dashboard/players/p");
  expect(screen.getByText(/لا تنشئ مطالبة أو دفعًا/)).toBeVisible(); expect(vi.mocked(fetch).mock.calls.every(c => !c[1]?.method || c[1]?.method === "GET")).toBe(true);
});
it("filter application and clear retain all authorized data, not hidden default dates", async () => {
  mock(); render(<RenewalOpportunities />); await screen.findByRole("option", { name: "التجمع" }); fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "b" } }); fireEvent.change(screen.getByLabelText("البحث باللاعب"), { target: { value: "لاعب" } }); fireEvent.click(screen.getByRole("button", { name: "تحديث الفرص" }));
  await waitFor(() => expect(fetch).toHaveBeenCalledWith("/api/v1/finance/renewal-opportunities?branchId=b&search=%D9%84%D8%A7%D8%B9%D8%A8", expect.anything()));
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" })); await waitFor(() => expect(screen.getByLabelText("الفرع")).toHaveValue("")); expect(screen.getByLabelText("البحث باللاعب")).toHaveValue("");
  await waitFor(() => expect(screen.getByRole("link", { name: /تصدير CSV/ })).toHaveAttribute("href", "/api/v1/finance/renewal-opportunities/export?&quoteHash=hash"));
});
it("current forecast KPI is a distinct drill-down and uses the selected branch", async () => {
  mock(); render(<RenewalOpportunityCard branchId="b" />); expect(await screen.findByRole("link", { name: "12,700 جنيه" })).toHaveAttribute("href", "/dashboard/finance/renewals?branchId=b");
  expect(screen.getByText(/ليست دينًا أو تحصيلًا/)).toBeVisible(); expect(screen.getByText(/تحتاج اختيار باقة ولا تدخل الإجمالي/)).toBeVisible();
});
it("permission or network failure does not show a fake zero forecast", async () => {
  global.fetch = vi.fn(async () => ok({}, 403)); render(<RenewalOpportunities />); await screen.findByText("تعذر تحميل فرص التجديد أو أنها خارج صلاحياتك."); expect(screen.queryByText("0 جنيه")).not.toBeInTheDocument(); expect(screen.queryByRole("link", { name: /تصدير CSV/ })).not.toBeInTheDocument();
});
it("finance read alone does not expose an unauthorized export action", async () => {
  mock(); const original = fetch; global.fetch = vi.fn((input, init) => String(input).endsWith("my-permissions") ? Promise.resolve(ok(["reports.financial.read"])) : original(input, init));
  render(<RenewalOpportunities />); await screen.findByRole("link", { name: "ملف اللاعب والتجديد" }); expect(screen.queryByRole("link", { name: /تصدير CSV/ })).not.toBeInTheDocument();
});
