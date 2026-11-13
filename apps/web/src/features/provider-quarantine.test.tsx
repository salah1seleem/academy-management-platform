import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ProviderQuarantine, ProviderQuarantineDetail, type ProviderIssue } from "./provider-quarantine";

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(window.location.search) }));
vi.mock("../components/date-filter-defaults", () => ({ useDateFilterDefaults: () => ({ from: "2026-10-01", to: "2026-10-13" }) }));

afterEach(() => {
  cleanup();
  window.history.replaceState(null, "", "/");
  vi.unstubAllGlobals();
});

const issue: ProviderIssue = {
  id: "issue", paymentRequestId: "payment", provider: "InternalTest", providerEventId: "event",
  reasonCode: "amount-mismatch", receivedStatus: "succeeded", receivedReference: "ref", expectedReference: "ref",
  receivedAmount: "925.50", expectedAmount: "900.00", receivedCurrency: "EGP", expectedCurrency: "EGP",
  createdAtUtc: "2026-10-13T08:00:00Z", paymentStatus: "Pending", player: "لاعب اصطناعي", branch: "فرع تجريبي",
};
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });
const root = "/api/v1/finance/provider-quarantine";
const route = "/dashboard/finance/provider-quarantine";

function listResponse(url: string) {
  const query = new URL(url, "http://test").searchParams;
  return response({ total: 26, page: Number(query.get("page")), pageSize: Number(query.get("pageSize")), items: [issue] });
}

describe("Owner provider discrepancy follow-up", () => {
  it("clears default dates and uses a single server pagination control", async () => {
    const fetch = vi.fn(async (path: string) => listResponse(path));
    vi.stubGlobal("fetch", fetch);
    render(<ProviderQuarantine />);
    await screen.findByText(issue.player);
    expect(screen.getByLabelText("من تاريخ")).toHaveValue("2026-10-01");
    fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" }));
    await waitFor(() => expect(fetch.mock.calls.at(-1)?.[0]).toBe(`${root}?page=1&pageSize=25`));
    expect(screen.getByLabelText("من تاريخ")).toHaveValue("");
    fireEvent.change(await screen.findByRole("combobox", { name: "صفوف الصفحة" }), { target: { value: "10" } });
    await screen.findByText("1–10 من 26");
    fireEvent.click(screen.getByRole("button", { name: "الصفحة التالية" }));
    await screen.findByText("11–20 من 26");
    expect(fetch.mock.calls.at(-1)?.[0]).toBe(`${root}?page=2&pageSize=10`);
    expect(screen.getAllByRole("navigation", { name: /صفحات/ })).toHaveLength(1);
    expect(screen.getByRole("link", { name: /عرض اختلاف دفع/ })).toHaveAttribute("href", `${route}/issue?from=&to=&page=2&pageSize=10`);
    expect(screen.queryByRole("button", { name: /قبول|اعتماد|تفعيل/ })).not.toBeInTheDocument();
  });

  it("blocks a reversed range without showing earlier rows and restores a valid request", async () => {
    const fetch = vi.fn(async (path: string) => listResponse(path));
    vi.stubGlobal("fetch", fetch);
    render(<ProviderQuarantine />);
    await screen.findByText(issue.player);
    fireEvent.change(screen.getByLabelText("من تاريخ"), { target: { value: "2026-10-14" } });
    expect(await screen.findByRole("alert")).toHaveTextContent("تاريخ البداية يجب أن يسبق تاريخ النهاية أو يساويه.");
    expect(screen.queryByText(issue.player)).not.toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(1);
    fireEvent.change(screen.getByLabelText("إلى تاريخ"), { target: { value: "2026-10-14" } });
    await screen.findByText(issue.player);
    expect(fetch.mock.calls.at(-1)?.[0]).toContain("from=2026-10-14&to=2026-10-14");
  });

  it("shows exact amounts, the recorded state, one back link and the receipt source", async () => {
    window.history.replaceState(null, "", `${route}/issue?from=2026-10-13&to=2026-10-13&page=2&pageSize=10`);
    vi.stubGlobal("fetch", vi.fn(async () => response({ ...issue, paymentStatus: "Confirmed", receiptId: "receipt" })));
    render(<ProviderQuarantineDetail id="issue" />);
    await screen.findByRole("heading", { name: "المبلغ مختلف" });
    expect(screen.getByText("925.50 جنيه")).toBeInTheDocument();
    expect(screen.getByText("900 جنيه")).toBeInTheDocument();
    expect(screen.getByText("تم تأكيد الطلب")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "فتح طلب الدفع" })).toHaveAttribute("href", "/dashboard/subscriptions/payments/payment");
    expect(screen.getByRole("link", { name: "عرض إيصال التحصيل" })).toHaveAttribute("href", "/dashboard/subscriptions/receipts/receipt");
    expect(screen.getByRole("link", { name: "العودة إلى اختلافات الدفع" })).toHaveAttribute("href", `${route}?from=2026-10-13&to=2026-10-13&page=2&pageSize=10`);
    expect(screen.getAllByText("ref")).toHaveLength(1);
    expect(screen.getByText("تفاصيل الإشارة والمراجع").closest("details")).not.toHaveAttribute("open");
  });

  it("retries the same read after a temporary failure", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(response({}, 503)).mockResolvedValueOnce(response(issue));
    vi.stubGlobal("fetch", fetch);
    render(<ProviderQuarantineDetail id="issue" />);
    await screen.findByText("تعذر تحميل سجل اختلافات الدفع.");
    expect(screen.queryByText(issue.player)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
    await screen.findByRole("heading", { name: "المبلغ مختلف" });
    expect(fetch.mock.calls.map(call => call[0])).toEqual([`${root}/issue`, `${root}/issue`]);
  });

  it.each([[403, "متابعة اختلافات الدفع متاحة للمالك فقط."], [404, "اختلاف الدفع غير متاح."]])(
    "fails closed for status %i without rendering player details or retry",
    async (status, message) => {
      vi.stubGlobal("fetch", vi.fn(async () => response({}, status)));
      render(<ProviderQuarantineDetail id="issue" />);
      await screen.findByText(message);
      expect(screen.queryByText(issue.player)).not.toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "فتح طلب الدفع" })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "إعادة المحاولة" })).not.toBeInTheDocument();
    },
  );
});
