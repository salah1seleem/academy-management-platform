import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { CashCollectionForm } from "./cash-collection-form";

const response = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
const items = [{ enrollmentId: "enrollment", player: "لاعب اصطناعي", code: "DEMO-1", sport: "كرة القدم", plans: [{ id: "plan", name: "باقة الشهر" }] }];
const quote = { quoteHash: "A".repeat(64), planName: "باقة الشهر", originalAmount: "1200.00", discountAmount: "240.00", amount: "960.00", currency: "EGP", discountReason: "عرض الإخوة" };
beforeEach(() => {
  sessionStorage.clear(); vi.stubGlobal("crypto", webcrypto); vi.spyOn(window, "confirm").mockReturnValue(true);
  global.fetch = vi.fn(async url => response(String(url).endsWith("csrf") ? { token: "csrf" } : String(url).endsWith("collection-quote") ? quote : { items }));
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });
async function choose() {
  fireEvent.change(screen.getByLabelText("البحث باللاعب أو كوده"), { target: { value: "لاعب" } }); fireEvent.click(screen.getByRole("button", { name: "بحث في الفرع" }));
  await screen.findByLabelText("تسجيل اللاعب"); fireEvent.change(screen.getByLabelText("تسجيل اللاعب"), { target: { value: "enrollment" } }); fireEvent.change(screen.getByLabelText("باقة التدريب"), { target: { value: "plan" } });
}
it("requires a branch-scoped selection and server price review before accepting money", async () => {
  render(<CashCollectionForm sessionId="shift" onCollected={vi.fn()} />); expect(screen.queryByText("تأكيد استلام النقدية وإصدار الإيصال")).not.toBeInTheDocument(); await choose();
  fireEvent.click(screen.getByText("مراجعة مبلغ القبض")); await screen.findByLabelText("مراجعة القبض"); expect(screen.getByText("عرض الإخوة")).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("باقة التدريب"), { target: { value: "" } }); expect(screen.queryByLabelText("مراجعة القبض")).not.toBeInTheDocument();
});
it("retry after an uncertain connection uses the same reviewed request and idempotency key", async () => {
  const keys: string[] = [], bodies: unknown[] = [], collected = vi.fn();
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (String(url).endsWith("collection-quote")) return response(quote);
    if (String(url).endsWith("/collections")) { keys.push((init!.headers as Record<string, string>)["Idempotency-Key"]); bodies.push(JSON.parse(init!.body as string)); if (keys.length === 1) throw new Error("انقطع الاتصال"); return response({ receiptId: "receipt" }); }
    return response({ items });
  });
  render(<CashCollectionForm sessionId="shift" onCollected={collected} />); await choose(); fireEvent.click(screen.getByText("مراجعة مبلغ القبض"));
  const confirm = await screen.findByText("تأكيد استلام النقدية وإصدار الإيصال"); fireEvent.click(confirm); await screen.findByText("انقطع الاتصال"); fireEvent.click(confirm);
  await screen.findByRole("link", { name: "عرض الإيصال" }); expect(keys).toHaveLength(2); expect(keys[0]).toBe(keys[1]); expect(bodies[0]).toEqual({ enrollmentId: "enrollment", planId: "plan", quoteHash: quote.quoteHash }); expect(bodies[0]).toEqual(bodies[1]); expect(collected).toHaveBeenCalledTimes(1);
});
it("cancelled confirmation never records a collection", async () => {
  vi.mocked(window.confirm).mockReturnValue(false); render(<CashCollectionForm sessionId="shift" onCollected={vi.fn()} />); await choose(); fireEvent.click(screen.getByText("مراجعة مبلغ القبض")); fireEvent.click(await screen.findByText("تأكيد استلام النقدية وإصدار الإيصال"));
  expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url).endsWith("/collections"))).toBe(false);
});
it("shows unavailable plans and does not invent a price or automatic payment", async () => {
  global.fetch = vi.fn(async () => response({ items: [{ ...items[0], plans: [] }] })); render(<CashCollectionForm sessionId="shift" onCollected={vi.fn()} />); await choose();
  expect(screen.getByText("لا توجد باقة منشورة تطابق تسجيل اللاعب وعملة الصندوق.")).toBeInTheDocument(); expect(screen.getByText("مراجعة مبلغ القبض")).toBeDisabled();
});
it("a denied search reveals no previous player or receipt", async () => {
  global.fetch = vi.fn(async () => response({}, 403)); render(<CashCollectionForm sessionId="shift" onCollected={vi.fn()} />);
  const search = screen.getByLabelText("البحث باللاعب أو كوده"); fireEvent.change(search, { target: { value: "لاعب" } }); fireEvent.submit(search.closest("form")!);
  await screen.findByText("تعذر البحث في لاعبي فرع الصندوق."); expect(screen.queryByLabelText("تسجيل اللاعب")).not.toBeInTheDocument(); expect(screen.queryByRole("link", { name: "عرض الإيصال" })).not.toBeInTheDocument();
});
it("price conflict retains explicit re-review and never shows a false success", async () => {
  const collected = vi.fn(); global.fetch = vi.fn(async url => response(String(url).endsWith("csrf") ? { token: "csrf" } : String(url).endsWith("collection-quote") ? quote : String(url).endsWith("/collections") ? { title: "تغير السعر. راجع المبلغ." } : { items }, String(url).endsWith("/collections") ? 409 : 200));
  render(<CashCollectionForm sessionId="shift" onCollected={collected} />); await choose(); fireEvent.click(screen.getByText("مراجعة مبلغ القبض")); fireEvent.click(await screen.findByText("تأكيد استلام النقدية وإصدار الإيصال"));
  await screen.findByText("تغير السعر. راجع المبلغ."); await waitFor(() => expect(screen.getByText("مراجعة مبلغ القبض")).not.toBeDisabled()); expect(collected).not.toHaveBeenCalled();
});
