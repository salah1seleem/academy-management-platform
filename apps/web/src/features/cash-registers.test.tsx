import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { CashRegisters, CashSessionDetail } from "./cash-registers";
vi.mock("./use-staff-action", () => ({ useStaffAction: () => canAdd }));
let canAdd = true;
const response = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
const register = { id: "register", name: "صندوق اصطناعي", branch: "الفرع التجريبي", currency: "EGP", canOpen: true, cashiers: [{ membershipId: "member", name: "أمين تجريبي", isActive: true }] };
const data = { cashEnabled: true, items: [register], history: [] };
const options = { isOwner: true, branches: [{ id: "branch", name: "الفرع التجريبي" }], members: [{ id: "member", name: "أمين تجريبي" }] };
beforeEach(() => { canAdd = true; sessionStorage.clear(); vi.stubGlobal("crypto", webcrypto); global.fetch = vi.fn(async url => response(String(url).endsWith("options") ? options : data)); });
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });
it("shows scoped cash registers and owner setup without inventing income", async () => {
  render(<CashRegisters />); await screen.findByText("صندوق اصطناعي");
  expect(screen.getByText("فتح وردية")).toBeInTheDocument(); expect(screen.getByText("إضافة صندوق فرع")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "إعدادات الأكاديمية" })).toHaveAttribute("href", "/dashboard/settings?tab=advanced");
  expect(screen.queryByText("تأكيد التحويل")).not.toBeInTheDocument();
});
it("pages and searches the bounded server list, and clear restores all authorized records", async () => {
  const paths: string[] = [];
  global.fetch = vi.fn(async url => {
    const path = String(url); paths.push(path);
    if (path.endsWith("options")) return response(options);
    const second = path.includes("offset=100");
    return response({ ...data, total: 101, pageSize: 100, items: [{ ...register, name: second ? "الصندوق القديم" : "صندوق اصطناعي" }] });
  });
  render(<CashRegisters />); await screen.findByText("صندوق اصطناعي");
  fireEvent.click(screen.getByRole("button", { name: /^التالي$/ })); await screen.findByText("الصندوق القديم");
  expect(paths.some(p => p.includes("offset=100"))).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: /^السابق$/ })); await screen.findByText("صندوق اصطناعي");
  fireEvent.click(screen.getByRole("button", { name: /^التالي$/ })); await screen.findByText("الصندوق القديم");
  fireEvent.change(screen.getByLabelText("البحث باسم الصندوق"), { target: { value: "القديم" } });
  await waitFor(() => expect(paths.some(p => p.includes("offset=0") && new URL(p, "http://local.test").searchParams.get("search") === "القديم")).toBe(true));
  fireEvent.click(screen.getByRole("button", { name: "مسح الفلاتر" }));
  await waitFor(() => expect(screen.getByLabelText("البحث باسم الصندوق")).toHaveValue(""));
  await waitFor(() => expect(paths.at(-2)).toBe("/api/v1/finance/cash-registers"));
});
it("disabled cash hides open even for an assigned cashier; read-only admin cannot configure", async () => {
  canAdd = false; global.fetch = vi.fn(async url => response(String(url).endsWith("options") ? { ...options, isOwner: false, members: [] } : { ...data, cashEnabled: false }));
  render(<CashRegisters />); await screen.findByText("النقدي متوقف");
  expect(screen.queryByText("فتح وردية")).not.toBeInTheDocument(); expect(screen.queryByText("إضافة صندوق فرع")).not.toBeInTheDocument(); expect(screen.queryByText("صلاحيات أمناء الصندوق")).not.toBeInTheDocument();
});
it("sends both reviewed balances and preserves the transfer amount on conflict", async () => {
  let sourceBalance = "100.00";
  const payloads: unknown[] = [];
  global.fetch = vi.fn(async (url, init) => {
    const path = String(url);
    if (path.endsWith("/csrf")) return response({ token: "csrf" });
    if (path.endsWith("/options")) return response(options);
    if (path.endsWith("/transfers") && init?.method === "POST") {
      payloads.push(JSON.parse(String(init.body)));
      sourceBalance = "95.00";
      return response({ title: "تغيّر الرصيد الدفتري. راجع أرصدة الصندوقين قبل تأكيد التحويل." }, 409);
    }
    const session = { opening: "100.00", openedAtUtc: "2026-09-28T06:00:00Z", canClose: true };
    return response({
      ...data,
      items: [
        { ...register, session: { ...session, id: "from", balance: sourceBalance } },
        { ...register, id: "target", name: "صندوق الاستلام", session: { ...session, id: "to", balance: "40.00" } },
      ],
    });
  });
  render(<CashRegisters />);
  fireEvent.click(await screen.findByText("تحويل عهدة بين صندوقين"));
  fireEvent.change(screen.getByLabelText("الصندوق المصدر"), { target: { value: "from" } });
  fireEvent.change(screen.getByLabelText("الصندوق المستلم"), { target: { value: "to" } });
  const amount = screen.getByLabelText("المبلغ المحول");
  fireEvent.change(amount, { target: { value: "10" } });
  const review = screen.getByRole("button", { name: "مراجعة التحويل" });
  fireEvent.click(review);
  fireEvent.click(await screen.findByRole("button", { name: "تأكيد التحويل" }));
  await screen.findByText(/تغيّر الرصيد الدفتري/);
  await waitFor(() => expect(review).toBeEnabled());
  expect(payloads).toEqual([{
    fromSessionId: "from", toSessionId: "to", amount: "10.00", reason: null,
    expectedSourceBalance: "100.00", expectedTargetBalance: "40.00",
  }]);
  expect(amount).toHaveValue("10");
  fireEvent.click(review);
  expect(await screen.findByRole("dialog")).toHaveTextContent("85 جنيه");
});
it("retains precise opening amount and idempotency key after uncertain failure", async () => {
  const keys: string[] = [], bodies: unknown[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") { keys.push((init.headers as Record<string, string>)["Idempotency-Key"]); bodies.push(JSON.parse(init.body as string)); if (keys.length === 1) throw new Error("انقطع الاتصال"); return response({ id: "session" }); }
    return response(String(url).endsWith("options") ? options : data);
  });
  render(<CashRegisters />); await screen.findByText("فتح وردية"); fireEvent.click(screen.getByText("فتح وردية"));
  const amount = screen.getByLabelText("العهدة الافتتاحية"); fireEvent.change(amount, { target: { value: "12.34" } }); fireEvent.submit(amount.closest("form")!);
  await screen.findByText("انقطع الاتصال"); fireEvent.submit(amount.closest("form")!); await waitFor(() => expect(keys).toHaveLength(2));
  expect(keys[0]).toBe(keys[1]); expect(bodies).toEqual([{ openingAmount: "12.34" }, { openingAmount: "12.34" }]);
});
it.each([["90.00", "-10.00", "عجز 10 جنيه"], ["100.00", "0.00", "متطابق"], ["110.00", "10.00", "زيادة 10 جنيه"]])("closed count %s displays %s without offering a second close", async (countedAmount, variance, label) => {
  global.fetch = vi.fn(async () => response({ ...closedSession, variance, session: { ...closedSession.session, countedAmount } }));
  render(<CashSessionDetail id="session" />); await screen.findByText(label);
  expect(screen.queryByRole("button", { name: "مراجعة المطابقة" })).not.toBeInTheDocument();
  expect(screen.getByText(/لا توجد حركات للوردية/)).toBeInTheDocument();
});
it("failed authorization does not show stale money or controls", async () => {
  global.fetch = vi.fn(async () => response({}, 404)); render(<CashSessionDetail id="foreign" />); await screen.findByText("تعذر تحميل الصندوق أو أنه خارج صلاحياتك.");
  expect(screen.queryByText("الرصيد الدفتري")).not.toBeInTheDocument(); expect(screen.queryByLabelText("المبلغ الموجود فعليًا")).not.toBeInTheDocument();
});
it("links typed product cash postings without pretending they are subscription receipts", async () => {
  global.fetch = vi.fn(async () => response({ name: "صندوق متجر", currency: "EGP", balance: "280.00", variance: null, canClose: false, session: { id: "session", openingAmount: "100.00", openedAtUtc: "2026-09-28T06:00:00Z" }, movements: [], productSales: [{ id: "posting", productSaleId: "sale", amount: "180.00", currency: "EGP", quantity: 2, productName: "طقم اصطناعي", createdAtUtc: "2026-09-28T06:00:00Z" }] }));
  render(<CashSessionDetail id="session" />); await screen.findByText("مبيعات المنتجات النقدية"); expect(screen.getByText("طقم اصطناعي")).toBeVisible();
  expect(screen.getByRole("link", { name: "تفاصيل البيع" })).toHaveAttribute("href", "/dashboard/product-sales/sale"); expect(screen.queryByText("إيصالات القبض في الوردية")).not.toBeInTheDocument();
});
const openSession = {
  name: "صندوق مفتوح", currency: "EGP", balance: "100.00", variance: null, canClose: true,
  session: { id: "session", openingAmount: "100", openedAtUtc: "2026-09-28T06:00:00Z" }, movements: [],
};
const closedSession = {
  ...openSession, variance: "-10.00",
  session: { ...openSession.session, closedAtUtc: "2026-09-28T14:00:00Z", expectedAmount: "100.00", countedAmount: "90.00" },
};
async function previewCount(value = "90.00") {
  const amount = await screen.findByLabelText("المبلغ الموجود فعليًا");
  fireEvent.change(amount, { target: { value } });
  fireEvent.submit(amount.closest("form")!);
  return screen.findByRole("dialog");
}
const confirmClose = () => fireEvent.click(screen.getByRole("button", { name: "تأكيد إقفال الوردية" }));

it("reviews the counted amount and shortage, supports cancel, then reads the same saved shift", async () => {
  const writes: unknown[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") { writes.push(JSON.parse(init.body as string)); return response({ id: "session" }); }
    return response(writes.length ? closedSession : openSession);
  });
  render(<CashSessionDetail id="session" />);
  expect(await previewCount("٩٠٫٠٠")).toHaveTextContent("عجز 10 جنيه");
  fireEvent.click(screen.getByRole("button", { name: /^إلغاء$/ }));
  expect(writes).toHaveLength(0);
  expect(screen.getByLabelText("المبلغ الموجود فعليًا")).toHaveValue("٩٠٫٠٠");
  await previewCount("٩٠٫٠٠");
  confirmClose();
  await screen.findByText("الرصيد المتوقع عند الإقفال");
  expect(writes).toEqual([{ countedAmount: "90.00", expectedAmount: "100.00", reason: null }]);
  expect(screen.getByText("عجز 10 جنيه")).toBeVisible();
  expect(screen.queryByRole("button", { name: "مراجعة المطابقة" })).not.toBeInTheDocument();
});

it("refreshes a stale reference after a 409 and retains the counted amount for review", async () => {
  let writes = 0;
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") { writes++; return response({ title: "تغيّر الرصيد الدفتري." }, 409); }
    return response({ ...openSession, balance: writes ? "110.00" : "100.00" });
  });
  render(<CashSessionDetail id="session" />);
  await previewCount();
  confirmClose();
  await screen.findByText("راجع الرصيد المحدّث والمبلغ المعدود قبل الإقفال.");
  expect(screen.getByLabelText("المبلغ الموجود فعليًا")).toHaveValue("90.00");
  fireEvent.click(screen.getByRole("button", { name: "مراجعة المطابقة" }));
  expect(await screen.findByRole("dialog")).toHaveTextContent("عجز 20 جنيه");
  expect(writes).toBe(1);
});

it("recovers an accepted close with readback only after interrupted, mismatched and incomplete reads", async () => {
  let writes = 0, reads = 0;
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") { writes++; return response({ id: "session" }); }
    reads++;
    if (reads === 2) return response({}, 503);
    if (reads === 3) return response({ ...closedSession, session: { ...closedSession.session, id: "foreign" } });
    if (reads === 4) return response({ ...closedSession, session: { ...closedSession.session, expectedAmount: null } });
    return response(writes ? closedSession : openSession);
  });
  render(<CashSessionDetail id="session" />);
  await previewCount();
  confirmClose();
  const retry = await screen.findByRole("button", { name: "إعادة قراءة الوردية" });
  expect(screen.getByLabelText("المبلغ الموجود فعليًا")).toBeDisabled();
  fireEvent.click(retry);
  await waitFor(() => expect(reads).toBe(3));
  await waitFor(() => expect(retry).toBeEnabled());
  fireEvent.click(retry);
  await waitFor(() => expect(reads).toBe(4));
  await waitFor(() => expect(retry).toBeEnabled());
  expect(screen.queryByText("الرصيد المتوقع عند الإقفال")).not.toBeInTheDocument();
  fireEvent.click(retry);
  await screen.findByText("فرق المطابقة");
  expect(writes).toBe(1);
});

it("replays only the same command and key when the shift is still open after an unknown response", async () => {
  const attempts: { key: string; body: string }[] = [];
  global.fetch = vi.fn(async (url, init) => {
    if (String(url).endsWith("csrf")) return response({ token: "csrf" });
    if (init?.method === "POST") {
      attempts.push({ key: (init.headers as Record<string, string>)["Idempotency-Key"], body: String(init.body) });
      return attempts.length === 1 ? response({}, 503) : response({ id: "session" });
    }
    return response(attempts.length > 1 ? closedSession : openSession);
  });
  render(<CashSessionDetail id="session" />);
  await previewCount();
  confirmClose();
  fireEvent.click(await screen.findByRole("button", { name: "إعادة محاولة الإقفال" }));
  await screen.findByText("فرق المطابقة");
  expect(attempts).toHaveLength(2);
  expect(attempts[0].key).toBeTruthy();
  expect(attempts[0]).toEqual(attempts[1]);
});

it("rejects empty, negative, excessive precision and over-limit amounts, and allows zero", async () => {
  global.fetch = vi.fn(async () => response(openSession));
  render(<CashSessionDetail id="session" />);
  const amount = await screen.findByLabelText("المبلغ الموجود فعليًا");
  for (const value of ["", "-10", "4.789", "1000000000"]) {
    fireEvent.change(amount, { target: { value } });
    fireEvent.submit(amount.closest("form")!);
    expect(await screen.findByRole("alert")).toHaveTextContent("بخانتين عشريتين");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  }
  expect(await previewCount("0")).toHaveTextContent("المبلغ المعدود 0 جنيه");
});

it("shows cash refunds as separate payouts linked to the player, never negative replacement receipts", async () => {
  global.fetch = vi.fn(async () => response({ name: "صندوق الاسترداد", currency: "EGP", balance: "59.75", variance: null, canClose: false, session: { id: "session", openingAmount: "100.00", openedAtUtc: "2026-09-28T06:00:00Z" }, movements: [], refunds: [{ id: "posting", refundId: "refund", playerId: "player", amount: "40.25", currency: "EGP", createdAtUtc: "2026-09-28T07:00:00Z" }] }));
  render(<CashSessionDetail id="session" />); await screen.findByRole("heading", { name: "الاستردادات النقدية المصروفة" });
  expect(screen.getByRole("link", { name: "ملف اللاعب والاسترداد" })).toHaveAttribute("href", "/dashboard/players/player?tab=subscriptions"); expect(screen.queryByText("إيصالات القبض في الوردية")).not.toBeInTheDocument();
});
