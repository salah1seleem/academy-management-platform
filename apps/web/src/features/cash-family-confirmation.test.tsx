import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CashFamilyConfirmation } from "./cash-family-confirmation";
afterEach(() => { cleanup(); sessionStorage.clear(); vi.restoreAllMocks(); });
const body = { guardianId: "guardian", quoteHash: "frozen-hash", lines: [{ sportEnrollmentId: "e1", subscriptionPlanId: "plan" }, { sportEnrollmentId: "e2", subscriptionPlanId: "plan" }] };
const response = (data: unknown) => new Response(JSON.stringify(data));
const crossBody = { reference: "CPF1.protected", quoteHash: "a".repeat(64), lines: [{ sportEnrollmentId: "e1", subscriptionPlanId: "plan" }, { sportEnrollmentId: "e2", subscriptionPlanId: "plan" }] };
describe("family cash confirmation", () => {
  it("requires explicit cash consent and links every receipt only after server success", async () => {
    const writes: { body: unknown; key: string | undefined }[] = []; const locked = vi.fn();
    global.fetch = vi.fn(async (path, init) => { if (String(path).endsWith("/csrf")) return response({ token: "csrf" }); writes.push({ body: JSON.parse(String(init?.body)), key: (init?.headers as Record<string, string>)["Idempotency-Key"] }); return response({ amount: "1234.56", currency: "EGP", items: [{ receiptId: "r1", amount: "600.00", currency: "EGP" }, { receiptId: "r2", amount: "634.56", currency: "EGP" }] }); });
    render(<CashFamilyConfirmation sessionId="shift" body={body} amount="1234.56" currency="EGP" onLocked={locked} />); expect(screen.getByRole("button")).toBeDisabled(); fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button"));
    await screen.findByRole("heading", { name: "تم تحصيل وتجديد الأسرة" }); expect(screen.getByRole("link", { name: /إيصال التسجيل 1/ })).toHaveAttribute("href", "/dashboard/subscriptions/receipts/r1"); expect(screen.getByRole("link", { name: /إيصال التسجيل 2/ })).toHaveAttribute("href", "/dashboard/subscriptions/receipts/r2"); expect(writes).toHaveLength(1); expect(writes[0].body).toEqual(body); expect(writes[0].key?.length).toBeGreaterThan(8); expect(locked).toHaveBeenCalledTimes(1); expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
  it("locks the selection and retries an uncertain response with the exact original body and key", async () => {
    const writes: unknown[] = []; const locked = vi.fn(); global.fetch = vi.fn(async (path, init) => { if (String(path).endsWith("/csrf")) return response({ token: "csrf" }); if (String(path).endsWith("/family-collection-result")) return response({ status: "NotRecorded", canDiscard: false }); writes.push([JSON.parse(String(init?.body)), (init?.headers as Record<string, string>)["Idempotency-Key"]]); throw new Error("technical SQL network"); });
    const view = render(<CashFamilyConfirmation sessionId="shift" body={body} amount="1234.56" currency="EGP" onLocked={locked} />); fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button")); expect(await screen.findByRole("alert")).not.toHaveTextContent("SQL"); expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    view.rerender(<CashFamilyConfirmation sessionId="shift" body={{ ...body, quoteHash: "changed" }} amount="9999.99" currency="EGP" onLocked={locked} />); fireEvent.click(screen.getByRole("button", { name: "إعادة إرسال نفس المحاولة" })); await waitFor(() => expect(writes).toHaveLength(2)); expect(writes[0]).toEqual(writes[1]);
  });
  it("recovers a protected cross-branch family receipt without another collection after reload", async () => {
    const intent = { key: "original-key-123", body: crossBody };
    sessionStorage.setItem("academy-cash-family-pending:shift", JSON.stringify(intent));
    const calls: string[] = [];
    global.fetch = vi.fn(async path => {
      calls.push(String(path));
      if (String(path).endsWith("/csrf")) return response({ token: "csrf" });
      if (String(path).endsWith("/family-collection-result")) return response({ status: "Confirmed", canDiscard: false, amount: "2400.00", currency: "EGP", items: [{ receiptId: "r1", amount: "1200.00", currency: "EGP" }, { receiptId: "r2", amount: "1200.00", currency: "EGP" }] });
      throw new Error("Unexpected collection POST");
    });
    render(<CashFamilyConfirmation sessionId="shift" body={crossBody} amount="" currency="EGP" onLocked={vi.fn()} />);
    await screen.findByRole("heading", { name: "تم تحصيل وتجديد الأسرة" });
    expect(screen.getAllByRole("link", { name: /إيصال التسجيل/ })).toHaveLength(2);
    expect(calls).not.toContain(expect.stringContaining("/cross-branch/families/collect"));
    expect(sessionStorage.getItem("academy-cash-family-pending:shift")).toBeNull();
  });
  it("does not send money when recovery storage is unavailable", async () => {
    const original = Storage.prototype.setItem;
    const spy = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("quota"); });
    const fetchSpy = vi.fn(async path => response(String(path).endsWith("/csrf") ? { token: "csrf" } : {}));
    global.fetch = fetchSpy;
    render(<CashFamilyConfirmation sessionId="shift" body={crossBody} amount="2400.00" currency="EGP" onLocked={vi.fn()} />);
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "تحصيل وتجديد كل التسجيلات" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر حفظ استعادة التحصيل");
    expect(fetchSpy).not.toHaveBeenCalled();
    spy.mockRestore();
    Storage.prototype.setItem = original;
  });
});
