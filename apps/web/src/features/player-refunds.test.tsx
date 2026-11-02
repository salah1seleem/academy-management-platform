import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { PlayerRefunds } from "./player-refunds";
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const available = { collections: [{ id: "pool", amount: 100, refunded: 10, allocated: 70, available: "20.00", currency: "EGP", confirmedAtUtc: "2026-09-28T06:00:00Z" }], refunds: [] };
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
beforeEach(() => { vi.stubGlobal("crypto", webcrypto); global.fetch = vi.fn(async () => response(available)); });
it("uses the server pool, not collection minus refunds alone, and bounds the amount", async () => {
  render(<PlayerRefunds playerId="player" />);
  await screen.findByText(/المبالغ المخصصة لمطالبات/);
  expect(screen.getByRole("option", { name: /المتاح: 20 جنيه/ })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "تأكيد الاسترداد" })).toBeDisabled();
  fireEvent.change(screen.getByLabelText("التحصيل"), { target: { value: "pool" } });
  expect(screen.getByLabelText("قيمة الاسترداد")).toHaveAttribute("max", "20.00");
  expect(screen.getByRole("button", { name: "تأكيد الاسترداد" })).toBeEnabled();
});
it("does not offer fully allocated or unverified pools", async () => {
  global.fetch = vi.fn(async () => response({ collections: [{ ...available.collections[0], available: "0.00" }, { ...available.collections[0], id: "missing", available: undefined }], refunds: [] }));
  render(<PlayerRefunds playerId="player" />); await screen.findByText("لا يوجد مبلغ متاح للاسترداد المباشر.");
  expect(screen.queryByRole("button", { name: "تأكيد الاسترداد" })).not.toBeInTheDocument();
});
it("sends a precise decimal and retains the retry key after uncertain connection failure", async () => {
  const keys: string[] = []; const bodies: unknown[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") {
      keys.push((init.headers as Record<string, string>)["Idempotency-Key"]); bodies.push(JSON.parse(init.body as string));
      if (keys.length === 1) throw new Error("انقطع الاتصال"); return response({ id: "refund" });
    }
    return response(available);
  });
  render(<PlayerRefunds playerId="player" />); await screen.findByLabelText("التحصيل");
  fireEvent.change(screen.getByLabelText("التحصيل"), { target: { value: "pool" } });
  fireEvent.change(screen.getByLabelText("قيمة الاسترداد"), { target: { value: "12.34" } });
  fireEvent.submit(screen.getByLabelText("التحصيل").closest("form")!);
  await screen.findByText("انقطع الاتصال"); fireEvent.submit(screen.getByLabelText("التحصيل").closest("form")!);
  await waitFor(() => expect(keys).toHaveLength(2)); expect(keys[0]).toBe(keys[1]); expect(bodies[0]).toEqual(bodies[1]);
  expect(bodies[0]).toEqual({ collectionId: "pool", amount: "12.34", reason: null });
});

it("requires an owned shift and a precise review before a cash refund; changing the amount clears approval", async () => {
  const writes: { url: string; body: Record<string, unknown> }[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") {
      const body = JSON.parse(init.body as string); writes.push({ url: String(url), body });
      if (String(url).endsWith("/quote")) return response({ amount: body.amount, available: "20.00", currency: "EGP", balance: "100.00", cashEnabled: true, sessions: [{ id: "shift", name: "صندوق الاختبار" }], quoteHash: body.cashSessionId ? "review" : null });
      return response({ id: "refund" });
    }
    return response({ ...available, collections: [{ ...available.collections[0], paymentMethod: "Cash" }] });
  });
  render(<PlayerRefunds playerId="player" />); await screen.findByLabelText("التحصيل");
  fireEvent.change(screen.getByLabelText("التحصيل"), { target: { value: "pool" } });
  expect(screen.getByLabelText("طريقة رد المبلغ")).toBeDisabled();
  fireEvent.change(screen.getByLabelText("قيمة الاسترداد"), { target: { value: "12.34" } });
  fireEvent.submit(screen.getByLabelText("التحصيل").closest("form")!); await screen.findByLabelText("وردية الصرف");
  expect(screen.queryByRole("button", { name: "تأكيد صرف الاسترداد" })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("وردية الصرف"), { target: { value: "shift" } });
  fireEvent.submit(screen.getByLabelText("التحصيل").closest("form")!); await screen.findByRole("button", { name: "تأكيد صرف الاسترداد" });
  fireEvent.change(screen.getByLabelText("قيمة الاسترداد"), { target: { value: "13.34" } });
  expect(screen.queryByRole("button", { name: "تأكيد صرف الاسترداد" })).not.toBeInTheDocument();
  fireEvent.submit(screen.getByLabelText("التحصيل").closest("form")!); await screen.findByRole("button", { name: "تأكيد صرف الاسترداد" });
  fireEvent.submit(screen.getByLabelText("التحصيل").closest("form")!); await screen.findByText(/تم تسجيل الاسترداد بنجاح/);
  expect(writes.filter(w => !w.url.endsWith("/quote"))).toEqual([{ url: "/api/v1/people/players/player/refunds", body: { collectionId: "pool", amount: "13.34", reason: null, cashSessionId: "shift", quoteHash: "review" } }]);
});

it("keeps the cash confirmation and retry key when its response is lost", async () => {
  const keys: string[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (String(url).endsWith("/quote")) return response({ amount: "10.00", available: "20.00", currency: "EGP", balance: "100.00", cashEnabled: true, sessions: [{ id: "shift", name: "صندوق" }], quoteHash: JSON.parse(init!.body as string).cashSessionId ? "review" : null });
    if (init?.method === "POST") { keys.push((init.headers as Record<string,string>)["Idempotency-Key"]); throw new TypeError("Failed to fetch"); }
    return response({ ...available, collections: [{ ...available.collections[0], paymentMethod: "Cash" }] });
  });
  render(<PlayerRefunds playerId="player" />); await screen.findByLabelText("التحصيل"); fireEvent.change(screen.getByLabelText("التحصيل"), { target: { value: "pool" } }); fireEvent.change(screen.getByLabelText("قيمة الاسترداد"), { target: { value: "10.00" } });
  const form = screen.getByLabelText("التحصيل").closest("form")!; fireEvent.submit(form); await screen.findByLabelText("وردية الصرف"); fireEvent.change(screen.getByLabelText("وردية الصرف"), { target: { value: "shift" } }); fireEvent.submit(form); await screen.findByRole("button", { name: "تأكيد صرف الاسترداد" });
  fireEvent.submit(form); await screen.findByText(/أعد المحاولة بنفس التفاصيل دون صرف المبلغ مرة أخرى/); fireEvent.submit(form); await waitFor(() => expect(keys).toHaveLength(2)); expect(keys[0]).toBe(keys[1]);
});
