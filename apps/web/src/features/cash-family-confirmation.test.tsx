import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CashFamilyConfirmation } from "./cash-family-confirmation";
vi.mock("../lib/pending-financial-intent", () => ({ pendingFinancialIntent: vi.fn(async () => "stable-family-request") }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const body = { guardianId: "guardian", quoteHash: "frozen-hash", lines: [{ sportEnrollmentId: "e1", subscriptionPlanId: "plan" }, { sportEnrollmentId: "e2", subscriptionPlanId: "plan" }] };
const response = (data: unknown) => new Response(JSON.stringify(data));
describe("family cash confirmation", () => {
  it("requires explicit cash consent and links every receipt only after server success", async () => {
    const writes: { body: unknown; key: string | undefined }[] = []; const locked = vi.fn();
    global.fetch = vi.fn(async (path, init) => { if (String(path).endsWith("/csrf")) return response({ token: "csrf" }); writes.push({ body: JSON.parse(String(init?.body)), key: (init?.headers as Record<string, string>)["Idempotency-Key"] }); return response({ amount: "1234.56", currency: "EGP", items: [{ receiptId: "r1", amount: "600.00", currency: "EGP" }, { receiptId: "r2", amount: "634.56", currency: "EGP" }] }); });
    render(<CashFamilyConfirmation sessionId="shift" body={body} amount="1234.56" currency="EGP" onLocked={locked} />); expect(screen.getByRole("button")).toBeDisabled(); fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button"));
    await screen.findByRole("heading", { name: "تم تحصيل وتجديد الأسرة" }); expect(screen.getByRole("link", { name: /إيصال التسجيل 1/ })).toHaveAttribute("href", "/dashboard/subscriptions/receipts/r1"); expect(screen.getByRole("link", { name: /إيصال التسجيل 2/ })).toHaveAttribute("href", "/dashboard/subscriptions/receipts/r2"); expect(writes).toEqual([{ body, key: "stable-family-request" }]); expect(locked).toHaveBeenCalledTimes(1); expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
  it("locks the selection and retries an uncertain response with the exact original body and key", async () => {
    const writes: unknown[] = []; const locked = vi.fn(); global.fetch = vi.fn(async (path, init) => { if (String(path).endsWith("/csrf")) return response({ token: "csrf" }); writes.push([JSON.parse(String(init?.body)), (init?.headers as Record<string, string>)["Idempotency-Key"]]); throw new Error("technical SQL network"); });
    const view = render(<CashFamilyConfirmation sessionId="shift" body={body} amount="1234.56" currency="EGP" onLocked={locked} />); fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button")); expect(await screen.findByRole("alert")).not.toHaveTextContent("SQL"); expect(screen.getByRole("checkbox")).toBeDisabled();
    view.rerender(<CashFamilyConfirmation sessionId="shift" body={{ ...body, quoteHash: "changed" }} amount="9999.99" currency="EGP" onLocked={locked} />); fireEvent.click(screen.getByRole("button", { name: "إعادة تأكيد نفس التحصيل" })); await waitFor(() => expect(writes).toHaveLength(2)); expect(writes[0]).toEqual(writes[1]);
  });
});
