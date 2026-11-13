import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { PlayerRefunds } from "./player-refunds";

const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const pool = { id: "pool", allocated: 70, available: "20.00", currency: "EGP", receiptNumber: "RC-20261112-D98C64EB", confirmedAtUtc: "2026-09-28T06:00:00Z" };
const available = { collections: [pool], refunds: [], canRecord: true };
const saved = { id: "refund", collectionId: "pool", amount: 12.34, payoutMethod: "Online", currency: "EGP", reason: null };
const scope = "academy-finance:player-refund:player";
const writeBody = { collectionId: "pool", amount: "12.34", reason: null };
const keyOf = (init?: RequestInit) => new Headers(init?.headers).get("Idempotency-Key");
const submit = () => fireEvent.submit(screen.getByLabelText("قيمة الاسترداد").closest("form")!);
const check = () => fireEvent.click(screen.getByRole("button", { name: "التحقق من السجل" }));

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
beforeEach(() => {
  sessionStorage.clear();
  vi.stubGlobal("crypto", webcrypto);
  global.fetch = vi.fn(async () => response(available));
});

async function openForm(amount = "12.34") {
  render(<PlayerRefunds playerId="player" />);
  fireEvent.click(await screen.findByText("تسجيل استرداد"));
  fireEvent.change(screen.getByLabelText("التحصيل"), { target: { value: "pool" } });
  fireEvent.change(screen.getByLabelText("قيمة الاسترداد"), { target: { value: amount } });
}

async function confirmOnline() {
  submit();
  fireEvent.click(await screen.findByRole("button", { name: "تسجيل التحويل المنفّذ" }));
}

it("distinguishes same-day receipts and uses the server pool after allocations", async () => {
  global.fetch = vi.fn(async () => response({
    ...available, collections: [pool, { ...pool, id: "other", receiptNumber: "RC-20261112-B954FE55" }],
  }));
  await openForm();
  expect(screen.getByRole("option", { name: /D98C64EB.*المتاح: 20 جنيه/ })).toBeInTheDocument();
  expect(screen.getByRole("option", { name: /B954FE55.*المتاح: 20 جنيه/ })).toBeInTheDocument();
  expect(screen.getByRole("combobox", { name: "التحصيل" })).toHaveAccessibleDescription(/تحصيل.*المتاح 20 جنيه/);
  expect(screen.getByText(/المبلغ المخصص لمطالبة/)).toBeInTheDocument();
  expect(screen.getByLabelText("قيمة الاسترداد")).toHaveAttribute("max", "20");
  fireEvent.change(screen.getByLabelText("التحصيل"), { target: { value: "other" } });
  expect(screen.getByLabelText("قيمة الاسترداد")).toHaveValue(null);
  expect(screen.getByRole("button", { name: "مراجعة الاسترداد" })).toBeDisabled();
});

it("offers a safe retry for a failed read and then displays a fully allocated pool as unavailable", async () => {
  global.fetch = vi.fn().mockResolvedValueOnce(response({}, 503)).mockImplementation(async () => response({
    ...available, collections: [{ ...pool, available: "0.00" }, { ...pool, id: "missing", available: undefined }],
  }));
  render(<PlayerRefunds playerId="player" />);
  await screen.findByRole("alert");
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل السجل" }));
  await screen.findByText("لا يوجد مبلغ متاح للاسترداد المباشر.");
  expect(screen.queryByRole("button", { name: "مراجعة الاسترداد" })).not.toBeInTheDocument();
});

it("shows the refund history without any write action for a reader", async () => {
  global.fetch = vi.fn(async () => response({
    ...available, canRecord: false, refunds: [{ ...saved, payoutMethod: null }],
  }));
  render(<PlayerRefunds playerId="player" />);
  await screen.findByText(/طريقة الصرف غير مسجلة/);
  expect(screen.queryByText("تسجيل استرداد")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /مراجعة الاسترداد/ })).not.toBeInTheDocument();
});

it.each(["", "0", "-1", "20.01", "12.345", "1e1"])("rejects %j before review or write", async amount => {
  await openForm(amount);
  expect(screen.getByRole("button", { name: "مراجعة الاسترداد" })).toBeDisabled();
  submit();
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(vi.mocked(fetch).mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
});

it("shows the exact amount, receipt and external transfer, and Escape cancels before any write", async () => {
  await openForm();
  const review = screen.getByRole("button", { name: "مراجعة الاسترداد" });
  review.focus();
  fireEvent.click(review);
  const dialog = screen.getByRole("dialog");
  expect(dialog).toHaveTextContent(/12\.34 جنيه من إيصال D98C64EB.*إتمام التحويل خارج النظام/);
  fireEvent.keyDown(dialog, { key: "Escape" });
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(review).toHaveFocus();
  expect(vi.mocked(fetch).mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
});

it.each([
  { amount: 12.35 }, { currency: "USD" }, { collectionId: "other" },
  { payoutMethod: "Cash" }, { cashSessionId: "other" }, { reason: "سبب مختلف" }, null,
])("rechecks with a read only when the acknowledged entry fails verification: %j", async fault => {
  const writes: unknown[] = [];
  let verify = false;
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") {
      writes.push(JSON.parse(String(init.body)));
      return response({ id: saved.id });
    }
    if (!writes.length) return response(available);
    if (verify) return response({ ...available, refunds: [saved] });
    return fault ? response({ ...available, refunds: [{ ...saved, ...fault }] }) : response({}, 503);
  });
  await openForm();
  await confirmOnline();
  await screen.findByRole("region", { name: "التحقق من الاسترداد" });
  expect(screen.queryByText(/تم تسجيل الاسترداد بنجاح/)).not.toBeInTheDocument();
  expect(screen.queryByLabelText("قيمة الاسترداد")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "إعادة إرسال التسجيل نفسه" })).not.toBeInTheDocument();
  verify = true;
  check();
  await screen.findByText(/تم تسجيل الاسترداد بنجاح بقيمة 12\.34 جنيه/);
  expect(writes).toEqual([writeBody]);
  expect(sessionStorage.getItem(scope)).toBeNull();
});

it("checks a missing response before resending the same payload and idempotency key", async () => {
  const keys: (string | null)[] = [], bodies: unknown[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") {
      keys.push(keyOf(init)); bodies.push(JSON.parse(String(init.body)));
      if (keys.length === 1) throw new TypeError("Failed to fetch");
      return response({ id: saved.id });
    }
    return keys.length === 2 ? response({ ...available, refunds: [saved] }) : response(available);
  });
  await openForm();
  await confirmOnline();
  await screen.findByRole("region", { name: "التحقق من الاسترداد" });
  check();
  fireEvent.click(await screen.findByRole("button", { name: "إعادة إرسال التسجيل نفسه" }));
  await screen.findByText(/تم تسجيل الاسترداد بنجاح/);
  expect(keys[0]).toBeTruthy();
  expect(keys[0]).toBe(keys[1]);
  expect(bodies).toEqual([writeBody, writeBody]);
});

it("recovers an already recorded refund on remount without storing its amount or reason", async () => {
  let key: string | null = null;
  const writes = vi.fn();
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") {
      key = keyOf(init);
      writes();
      throw new TypeError("Failed to fetch");
    }
    return key && keyOf(init) === key ? response({ ...available, refunds: [{ ...saved, reason: "سبب خاص", matchesAttempt: true }] }) : response(available);
  });
  await openForm();
  fireEvent.change(screen.getByLabelText("السبب (اختياري)"), { target: { value: "سبب خاص" } });
  await confirmOnline();
  await screen.findByRole("region", { name: "التحقق من الاسترداد" });
  expect(JSON.parse(sessionStorage.getItem(scope)!)).toEqual({ key, hash: expect.stringMatching(/^[a-f\d]{64}$/) });
  cleanup();
  render(<PlayerRefunds playerId="player" />);
  await screen.findByText(/تم تسجيل الاسترداد بنجاح/);
  expect(writes).toHaveBeenCalledTimes(1);
  expect(sessionStorage.getItem(scope)).toBeNull();
});

it("does not resubmit on remount when the original request never reached the server", async () => {
  sessionStorage.setItem(scope, JSON.stringify({ key: webcrypto.randomUUID(), hash: "fingerprint" }));
  render(<PlayerRefunds playerId="player" />);
  fireEvent.click(await screen.findByRole("button", { name: "بدء مراجعة أخرى" }));
  fireEvent.click(screen.getByRole("button", { name: "العودة للتحقق" }));
  fireEvent.click(screen.getByRole("button", { name: "بدء مراجعة أخرى" }));
  fireEvent.click(screen.getByRole("button", { name: "راجعت السجل، متابعة" }));
  await screen.findByText("تسجيل استرداد");
  expect(vi.mocked(fetch).mock.calls.every(([, init]) => init?.method !== "POST")).toBe(true);
});

it("requires a cash shift and invalidates the confirmation on edits and a stale quote", async () => {
  const writes: unknown[] = [];
  global.fetch = vi.fn(async (url, init) => {
    const path = String(url);
    if (path.endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") {
      const body = JSON.parse(String(init.body));
      if (path.endsWith("/quote")) return response({
        amount: body.amount, available: "20.00", currency: "EGP", balance: "100.00", cashEnabled: true,
        sessions: [{ id: "shift", name: "صندوق الاختبار" }], quoteHash: body.cashSessionId ? "review" : null,
      });
      writes.push(body);
      return writes.length === 1 ? response({ title: "تغير رصيد الوردية." }, 409) : response({ id: saved.id });
    }
    return response({
      ...available,
      collections: [{ ...pool, paymentMethod: "Cash" }],
      refunds: writes.length === 2 ? [{ ...saved, payoutMethod: "Cash", cashSessionId: "shift", reason: "السبب المعدل" }] : [],
    });
  });
  await openForm();
  expect(screen.getByLabelText("طريقة رد المبلغ")).toBeDisabled();
  submit();
  const shift = await screen.findByLabelText("وردية الصرف");
  expect(screen.queryByRole("button", { name: "تأكيد صرف الاسترداد" })).not.toBeInTheDocument();
  fireEvent.change(shift, { target: { value: "shift" } });
  submit();
  await screen.findByRole("button", { name: "تأكيد صرف الاسترداد" });
  fireEvent.click(screen.getByRole("button", { name: "تحديث المراجعة" }));
  expect(screen.queryByRole("button", { name: "تأكيد صرف الاسترداد" })).not.toBeInTheDocument();
  submit();
  await screen.findByRole("button", { name: "تأكيد صرف الاسترداد" });
  fireEvent.change(screen.getByLabelText("السبب (اختياري)"), { target: { value: "السبب المعدل" } });
  expect(screen.queryByRole("button", { name: "تأكيد صرف الاسترداد" })).not.toBeInTheDocument();
  submit();
  await screen.findByRole("button", { name: "تأكيد صرف الاسترداد" });
  submit();
  await screen.findByText("تغير رصيد الوردية.");
  await waitFor(() => expect(screen.getByRole("button", { name: "مراجعة الاسترداد النقدي" })).toBeEnabled());
  submit();
  await screen.findByRole("button", { name: "تأكيد صرف الاسترداد" });
  submit();
  await screen.findByText(/تم تسجيل الاسترداد بنجاح/);
  const cashBody = { ...writeBody, reason: "السبب المعدل", cashSessionId: "shift", quoteHash: "review" };
  expect(writes).toEqual([cashBody, cashBody]);
});
