import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ProviderQuarantine, ProviderQuarantineDetail, type ProviderIssue } from "./provider-quarantine";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const issue: ProviderIssue = { id: "issue", paymentRequestId: "payment", provider: "InternalTest", providerEventId: "event", reasonCode: "amount-mismatch", receivedStatus: "succeeded", receivedReference: "ref", expectedReference: "ref", receivedAmount: "925.00", expectedAmount: "900.00", receivedCurrency: "EGP", expectedCurrency: "EGP", createdAtUtc: "2026-10-13T08:00:00Z", paymentStatus: "Pending", player: "لاعب اصطناعي", branch: "فرع تجريبي" };
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
describe("Owner provider discrepancy follow-up", () => {
  it("clears even default dates and paginates stored records without financial approval", async () => {
    const paths: string[] = []; global.fetch = vi.fn(async path => { paths.push(String(path)); return response({ total: 26, page: new URL(String(path), "http://test").searchParams.get("page") === "2" ? 2 : 1, pageSize: 25, items: [issue] }); });
    render(<ProviderQuarantine />); await screen.findByText(issue.player); expect((screen.getByLabelText("من تاريخ") as HTMLInputElement).value).not.toBe("");
    fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" })); await waitFor(() => expect(paths.at(-1)).toBe("/api/v1/finance/provider-quarantine?page=1"));
    expect((screen.getByLabelText("من تاريخ") as HTMLInputElement).value).toBe(""); await screen.findByText(issue.player);
    fireEvent.click(screen.getByRole("button", { name: /^التالي$/ })); await waitFor(() => expect(paths.at(-1)).toContain("page=2"));
    expect(screen.queryByRole("button", { name: /قبول|اعتماد|تفعيل/ })).not.toBeInTheDocument();
  });
  it("shows expected and received values, current status and only valid source drilldowns", async () => {
    global.fetch = vi.fn(async () => response({ ...issue, paymentStatus: "Confirmed", receiptId: "receipt" })); render(<ProviderQuarantineDetail id="issue" />);
    await screen.findByText(/المبلغ مختلف/); expect(screen.getByText("925 جنيه")).toBeInTheDocument(); expect(screen.getByText("900 جنيه")).toBeInTheDocument();
    expect(screen.getByText("مؤكد بإشارة أخرى صحيحة")).toBeInTheDocument(); expect(screen.getByRole("link", { name: "تفاصيل طلب الدفع" })).toHaveAttribute("href", "/dashboard/subscriptions/payments/payment");
    expect(screen.getByRole("link", { name: "إيصال التأكيد الصحيح" })).toHaveAttribute("href", "/dashboard/subscriptions/receipts/receipt");
  });
  it("fails closed for a forbidden role without rendering player details", async () => {
    global.fetch = vi.fn(async () => response({}, 403)); render(<ProviderQuarantineDetail id="issue" />); await screen.findByText("هذه المتابعة متاحة للمالك فقط."); expect(screen.queryByText(issue.player)).not.toBeInTheDocument();
  });
});
