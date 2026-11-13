import { webcrypto } from "node:crypto";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { AdminPaymentDetail } from "./subscriptions";

const push = vi.hoisted(() => vi.fn());
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }), useSearchParams: () => new URLSearchParams() }));

const payment = {
  id: "payment-a", renewalId: "renewal-a", provider: "Manual", canCollect: false, canReviewProof: true,
  branchId: "branch-a", sportId: "sport-a", groupId: "group-a", isCurrent: true,
  player: "لاعب تجريبي", playerCode: "DEMO01", sport: "كرة القدم", plan: "اشتراك شهري",
  amount: 850.5, finalAmount: 850.5, originalAmount: 900, discountAmount: 49.5,
  currency: "EGP", status: "Pending", renewalStatus: "PendingPayment", createdAtUtc: "2026-11-05T12:00:00Z",
};
const assetId = "6d19c27b-e27f-4634-b282-1b22e448f47b";
const proof = { id: "proof-a", assetId, status: "Pending", url: `/api/v1/assets/${assetId}` };
const readPayment = "/api/v1/subscriptions/payments/payment-a";
const proofPath = "/api/v1/manage/payments/payment-a/proof";
const options = { groups: [
  { id: "group-a", arabicName: "المجموعة الحالية", branchId: "branch-a", sportId: "sport-a" },
  { id: "group-b", arabicName: "مجموعة أخرى", branchId: "branch-a", sportId: "sport-a" },
] };

beforeEach(() => {
  push.mockClear();
  sessionStorage.clear();
  vi.stubGlobal("crypto", webcrypto);
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it("distinguishes a failed read from a pending one and retries the read", async () => {
  const fetcher = vi.fn(async input => String(input) === readPayment ? Response.json(payment) : new Response(null, { status: 404 }));
  fetcher.mockRejectedValueOnce(new TypeError("Network failure"));
  vi.stubGlobal("fetch", fetcher);
  render(<AdminPaymentDetail id="payment-a" />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل طلب الدفع");
  expect(screen.queryByRole("status")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل الطلب" }));
  expect(await screen.findByText("المبلغ المطلوب")).toBeVisible();
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

it("confirms the displayed electronic amount and reloads after the server rejects an outdated amount", async () => {
  let amount = payment.amount;
  const fetcher = vi.fn(async (input, init) => {
    const path = String(input);
    if (path === readPayment) return Response.json({ ...payment, amount, canCollect: true });
    if (path.endsWith("/cash-options")) return Response.json({ cashEnabled: false, sessions: [] });
    if (path.endsWith("/csrf")) return Response.json({ token: "csrf" });
    if (path.endsWith("/collect")) {
      expect(JSON.parse(String(init.body))).toEqual({ paymentMethod: "Online", expectedAmount: "850.5" });
      amount = 900;
      return Response.json({ message: "تغير المبلغ. راجع الإجمالي قبل التأكيد." }, { status: 409 });
    }
    return new Response(null, { status: 404 });
  });
  vi.stubGlobal("fetch", fetcher);
  render(<AdminPaymentDetail id="payment-a" />);
  fireEvent.click(await screen.findByRole("button", { name: "تأكيد تحصيل إلكتروني" }));
  const dialog = screen.getByRole("dialog", { name: "تأكيد التحصيل الإلكتروني" });
  expect(dialog).toHaveTextContent("850.5 جنيه");
  expect(fetcher.mock.calls.some(([path]) => String(path).endsWith("/collect"))).toBe(false);
  fireEvent.click(within(dialog).getByRole("button", { name: "تسجيل التحصيل" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("تغير المبلغ");
  expect((await screen.findByText("المبلغ المطلوب")).parentElement).toHaveTextContent("900 جنيه");
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
});

it("shows proof read failures and does not expose review controls for a superseded request", async () => {
  let loadProof = false;
  vi.stubGlobal("fetch", vi.fn(async input => {
    if (input === readPayment) return Response.json({ ...payment, isCurrent: false, currentPaymentId: "replacement" });
    if (input === proofPath) return loadProof ? Response.json(proof) : new Response(null, { status: 503 });
    throw new Error("Unexpected request");
  }));
  render(<AdminPaymentDetail id="payment-a" />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل بيانات إثبات التحويل");
  loadProof = true;
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل الطلب" }));
  expect(await screen.findByRole("link", { name: "فتح إثبات التحويل" })).toHaveAttribute("href", proof.url);
  expect(screen.getByRole("link", { name: "فتح طلب الدفع الحالي" })).toHaveAttribute("href", "/dashboard/subscriptions/payments/replacement");
  expect(screen.queryByRole("button", { name: "قبول الإثبات" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "رفض الإثبات" })).not.toBeInTheDocument();
});

it("reviews proof for the assigned group and waits for the saved status before showing the receipt", async () => {
  let reviewed = false;
  const fetcher = vi.fn(async (input, init) => {
    const path = String(input);
    if (path === readPayment) return Response.json(reviewed ? { ...payment, status: "Confirmed", receiptId: "receipt-a" } : payment);
    if (path === proofPath) return Response.json({ ...proof, status: reviewed ? "Accepted" : "Pending" });
    if (path.endsWith("/options")) return Response.json(options);
    if (path.endsWith("/csrf")) return Response.json({ token: "csrf" });
    if (path.endsWith("/review")) {
      expect(JSON.parse(String(init.body))).toEqual({ accept: true, groupId: "group-a", reason: null, expectedProofAssetId: assetId });
      reviewed = true;
      return Response.json({ receiptId: "receipt-a" });
    }
    throw new Error("Unexpected request");
  });
  vi.stubGlobal("fetch", fetcher);
  const view = render(<AdminPaymentDetail id="payment-a" />);
  await screen.findByText("المجموعة الحالية");
  expect(screen.queryByText("مجموعة أخرى")).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "قبول الإثبات" })).toBeDisabled();
  fireEvent.change(screen.getByLabelText("ملاحظة للأسرة (اختياري)"), { target: { value: "ملاحظة محفوظة" } });
  fireEvent.error(view.container.querySelector("img[alt='صورة إثبات تحويل التجديد']")!);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر فتح صورة إثبات الدفع");
  expect(screen.getByRole("button", { name: "قبول الإثبات" })).toBeDisabled();
  expect(screen.getByRole("button", { name: "رفض الإثبات" })).toBeEnabled();
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل الصورة" }));
  const preview = view.container.querySelector("img[alt='صورة إثبات تحويل التجديد']")!;
  Object.defineProperties(preview, { naturalWidth: { value: 24 }, complete: { value: true }, decode: { value: async () => undefined } });
  fireEvent.load(preview);
  await screen.findByRole("img", { name: "صورة إثبات تحويل التجديد" });
  expect(screen.getByLabelText("ملاحظة للأسرة (اختياري)")).toHaveValue("ملاحظة محفوظة");
  expect(fetcher.mock.calls.filter(([path]) => String(path).endsWith("/options"))).toHaveLength(1);
  fireEvent.change(screen.getByLabelText("ملاحظة للأسرة (اختياري)"), { target: { value: "" } });
  fireEvent.click(screen.getByRole("button", { name: "قبول الإثبات" }));
  const dialog = screen.getByRole("dialog", { name: "قبول إثبات التحويل" });
  expect(dialog).toHaveTextContent("850.5 جنيه");
  expect(screen.queryByRole("link", { name: "عرض الإيصال" })).not.toBeInTheDocument();
  fireEvent.click(within(dialog).getByRole("button", { name: "تسجيل التحصيل" }));
  expect(await screen.findByRole("link", { name: "عرض الإيصال" })).toHaveAttribute("href", "/dashboard/subscriptions/receipts/receipt-a?renewalPayment=payment-a");
  expect(await screen.findByText("تم قبول الإثبات")).toBeVisible();
});

it.each([
  { data: { ...payment, currency: "", canCollect: true, canCollectCash: true, canSimulate: true }, groups: options.groups, message: "تعذر التحقق من مبلغ الطلب وعملته." },
  { data: payment, groups: [options.groups[1]], message: "لا توجد مجموعة متاحة لهذا التسجيل. راجع التسكين قبل قبول الإثبات." },
])("keeps acceptance disabled with invalid money or a mismatched group", async ({ data, groups, message }) => {
  vi.stubGlobal("fetch", vi.fn(async input => {
    if (input === readPayment) return Response.json(data);
    if (input === proofPath) return Response.json(proof);
    return Response.json({ groups });
  }));
  const view = render(<AdminPaymentDetail id="payment-a" />);
  await screen.findByText(message);
  const preview = view.container.querySelector("img[alt='صورة إثبات تحويل التجديد']")!;
  Object.defineProperties(preview, { naturalWidth: { value: 24 }, complete: { value: true }, decode: { value: async () => undefined } });
  fireEvent.load(preview);
  await screen.findByRole("img", { name: "صورة إثبات تحويل التجديد" });
  expect(screen.getByRole("button", { name: "قبول الإثبات" })).toBeDisabled();
  if (!data.currency) {
    expect(screen.getByText("المبلغ المطلوب").parentElement).toHaveTextContent("غير متاح");
    expect(screen.queryByText(/جنيه/)).toBeNull();
    expect(screen.queryByRole("button", { name: "تأكيد تحصيل إلكتروني" })).toBeNull();
    expect(screen.queryByRole("heading", { name: "تحصيل نقدي من ورديتك" })).toBeNull();
    expect(screen.queryByRole("heading", { name: "محاكاة الدفع التجريبي" })).toBeNull();
  }
});

it("shows the proof without decision controls for a staff member with read access", async () => {
  const fetcher = vi.fn(async input => {
    if (input === readPayment) return Response.json({ ...payment, canReviewProof: false });
    if (input === proofPath) return Response.json(proof);
    throw new Error("Unexpected request");
  });
  vi.stubGlobal("fetch", fetcher);
  render(<AdminPaymentDetail id="payment-a" />);
  await screen.findByRole("link", { name: "فتح إثبات التحويل" });
  expect(screen.queryByRole("button", { name: "قبول الإثبات" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "رفض الإثبات" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "تأكيد تحصيل إلكتروني" })).not.toBeInTheDocument();
  expect(screen.queryByRole("heading", { name: "تحصيل نقدي من ورديتك" })).not.toBeInTheDocument();
  expect(screen.queryByRole("link", { name: "إدارة الخصم" })).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "تفاصيل التجديد" })).toHaveAttribute("href", "/dashboard/subscriptions/renewals/renewal-a?fromPayment=true");
  expect(fetcher.mock.calls).toHaveLength(2);
});

it("requires a new preview when opening another payment", async () => {
  const replacement = "3c8271aa-6fc3-499b-8a85-f6765f42daa7";
  vi.stubGlobal("fetch", vi.fn(async input => {
    const path = String(input);
    if (path.endsWith("/options")) return Response.json(options);
    if (path.endsWith("/proof")) return Response.json(path === proofPath ? proof : { ...proof, assetId: replacement, url: `/api/v1/assets/${replacement}` });
    return Response.json(path === readPayment ? payment : { ...payment, id: "payment-b" });
  }));
  const view = render(<AdminPaymentDetail id="payment-a" />);
  await screen.findByRole("link", { name: "فتح إثبات التحويل" });
  const first = view.container.querySelector("img[alt='صورة إثبات تحويل التجديد']")!;
  Object.defineProperties(first, { naturalWidth: { value: 24 }, complete: { value: true }, decode: { value: async () => undefined } });
  fireEvent.load(first);
  await waitFor(() => expect(screen.getByRole("button", { name: "قبول الإثبات" })).toBeEnabled());
  view.rerender(<AdminPaymentDetail id="payment-b" />);
  expect(await screen.findByRole("link", { name: "فتح إثبات التحويل" })).toHaveAttribute("href", `/api/v1/assets/${replacement}`);
  expect(screen.getByRole("button", { name: "قبول الإثبات" })).toBeDisabled();
  expect(screen.queryByRole("img", { name: "صورة إثبات تحويل التجديد" })).toBeNull();
});

it("keeps the historical amount on a superseded request without showing the current discount", async () => {
  vi.stubGlobal("fetch", vi.fn(async input => input === readPayment
    ? Response.json({ ...payment, status: "Cancelled", amount: 900, isCurrent: false, currentPaymentId: "replacement" })
    : new Response(null, { status: 404 })));
  render(<AdminPaymentDetail id="payment-a" />);
  expect((await screen.findByText("مبلغ الطلب")).parentElement).toHaveTextContent("900 جنيه");
  expect(screen.queryByText("قبل الخصم")).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "فتح طلب الدفع الحالي" })).toHaveAttribute("href", "/dashboard/subscriptions/payments/replacement");
});

it("does not infer retry or simulation authority from the payment status and provider", async () => {
  const fetcher = vi.fn(async input => {
    const id = String(input).split("/").at(-1);
    if (id === "failed") return Response.json({ ...payment, status: "Failed", canRetry: false });
    if (id === "pending") return Response.json({ ...payment, provider: "InternalTest", canSimulate: false });
    return new Response(null, { status: 404 });
  });
  vi.stubGlobal("fetch", fetcher);
  const view = render(<AdminPaymentDetail id="failed" />);
  expect(await screen.findByText("مبلغ الطلب")).toBeVisible();
  expect(screen.queryByRole("button", { name: "إعادة محاولة الدفع" })).not.toBeInTheDocument();
  view.rerender(<AdminPaymentDetail id="pending" />);
  expect(await screen.findByText("المبلغ المطلوب")).toBeVisible();
  expect(screen.queryByRole("heading", { name: "محاكاة الدفع التجريبي" })).not.toBeInTheDocument();
});

it("reuses the same key when a replacement payment response is lost", async () => {
  const keys: (string | null)[] = [];
  vi.stubGlobal("fetch", vi.fn(async (input, init) => {
    const path = String(input);
    if (path === readPayment) return Response.json({ ...payment, status: "Failed", canRetry: true });
    if (path.endsWith("/csrf")) return Response.json({ token: "csrf" });
    if (path.endsWith("/retry")) {
      keys.push(new Headers(init.headers).get("Idempotency-Key"));
      return keys.length === 1 ? Response.json({}) : Response.json({ paymentId: "replacement" });
    }
    return new Response(null, { status: 404 });
  }));
  render(<AdminPaymentDetail id="payment-a" />);
  fireEvent.click(await screen.findByRole("button", { name: "إعادة محاولة الدفع" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تأكيد العملية");
  fireEvent.click(screen.getByRole("button", { name: "إعادة محاولة الدفع" }));
  await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/subscriptions/payments/replacement"));
  expect(keys).toHaveLength(2);
  expect(keys[0]).toBeTruthy();
  expect(keys[1]).toBe(keys[0]);
});
