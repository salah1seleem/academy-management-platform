import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { CashReceiptVoid } from "./cash-receipt-void";
import { ReceiptPrint } from "./receipt-print";
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const review = { id: "receipt", receiptNumber: "R-1", playerId: "player", player: "لاعب اصطناعي", plan: "اشتراك شهري", amount: "800.00", currency: "EGP", startDate: "2026-09-28", endDate: "2026-10-28", cashEnabled: true, sessions: [{ id: "shift", name: "ورديتي" }] };
beforeEach(() => vi.stubGlobal("crypto", webcrypto));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it("requires an explicit review, invalidates it when reason changes, and posts server-bound amount once", async () => {
  const writes: { path: string; body: unknown }[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "token" });
    if (init?.method === "POST") { writes.push({ path: String(url), body: JSON.parse(init.body as string) }); return response(String(url).endsWith("void-quote") ? { ...review, balance: "900.00", quoteHash: "quote" } : { id: "void" }); }
    return response(review);
  });
  render(<CashReceiptVoid receiptId="receipt" />); await screen.findByText(review.player);
  expect(screen.getByRole("button", { name: "مراجعة عكس الإيصال" })).toBeDisabled();
  fireEvent.change(screen.getByLabelText("وردية صرف العكس"), { target: { value: "shift" } });
  const form = screen.getByLabelText("وردية صرف العكس").closest("form")!;
  fireEvent.submit(form); await screen.findByRole("button", { name: "تأكيد الصرف وإلغاء الفترة" });
  fireEvent.change(screen.getByLabelText("السبب (اختياري)"), { target: { value: "تعديل" } });
  expect(screen.queryByRole("button", { name: "تأكيد الصرف وإلغاء الفترة" })).not.toBeInTheDocument();
  fireEvent.submit(form); await screen.findByRole("button", { name: "تأكيد الصرف وإلغاء الفترة" });
  fireEvent.submit(form); await screen.findByText("تم عكس الإيصال وإلغاء الفترة المرتبطة بنجاح.");
  expect(writes.at(-1)).toEqual({ path: "/api/v1/finance/cash-registers/receipts/receipt/void", body: { cashSessionId: "shift", reason: "تعديل", quoteHash: "quote" } });
  expect(screen.queryByRole("button", { name: "تأكيد الصرف وإلغاء الفترة" })).not.toBeInTheDocument();
});
it("locks uncertain details and reuses the exact request key instead of risking another cash payout", async () => {
  const keys: string[] = [], bodies: string[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "token" });
    if (String(url).endsWith("void-quote")) return response({ ...review, balance: "900.00", quoteHash: "quote" });
    if (init?.method === "POST") { keys.push((init.headers as Record<string, string>)["Idempotency-Key"]); bodies.push(init.body as string); if (keys.length === 1) throw new TypeError("Failed to fetch"); return response({ id: "void", replay: true }); }
    return response(review);
  });
  render(<CashReceiptVoid receiptId="receipt" />); await screen.findByLabelText("وردية صرف العكس");
  fireEvent.change(screen.getByLabelText("وردية صرف العكس"), { target: { value: "shift" } });const form = screen.getByLabelText("وردية صرف العكس").closest("form")!;
  fireEvent.submit(form); await screen.findByRole("button", { name: "تأكيد الصرف وإلغاء الفترة" });fireEvent.submit(form);
  await screen.findByText(/النتيجة غير مؤكدة/);expect(screen.getByLabelText("السبب (اختياري)")).toBeDisabled();expect(screen.getByLabelText("وردية صرف العكس")).toBeDisabled();
  fireEvent.submit(form);await screen.findByText("تم عكس الإيصال وإلغاء الفترة المرتبطة بنجاح.");expect(keys).toHaveLength(2);expect(keys[0]).toBe(keys[1]);expect(bodies[0]).toBe(bodies[1]);
});
it("does not expose a write form for a forbidden or already used receipt", async () => {
  global.fetch = vi.fn(async () => response({ title: "العكس للمالك فقط." }, 403));render(<CashReceiptVoid receiptId="receipt" />);
  await screen.findByText("العكس للمالك فقط.");expect(screen.queryByLabelText("وردية صرف العكس")).not.toBeInTheDocument();
});
it("retains original printed amounts with a visible void status and hides the owner action after reversal", async () => {
  global.fetch = vi.fn(async () => response({ id: "receipt", academyName: "الأكاديمية", receiptNumber: "R1", amount: 800, currency: "EGP", paidAtUtc: "2026-09-28T06:00:00Z", isVoided: true, voidedAtUtc: "2026-10-13T06:00:00Z", canReviewVoid: false }));
  render(<ReceiptPrint endpoint="/receipt" />);await screen.findByText("إيصال معكوس — الفترة المرتبطة ملغاة");
  expect(screen.getByText("800 جنيه")).toBeInTheDocument();expect(screen.queryByRole("link", { name: /مراجعة عكس/ })).not.toBeInTheDocument();
  await waitFor(() => expect(screen.getByRole("button", { name: "طباعة الإيصال" })).toBeEnabled());
});
