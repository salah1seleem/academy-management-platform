import { webcrypto } from "node:crypto";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { ProductPaymentReview } from "./manual-payments";
import { clearFinancialIntent } from "../lib/pending-financial-intent";

const product = {
  id: "order", productName: "طقم مراجعة اصطناعي", branchName: "فرع اختباري",
  quantity: 2, unitPrice: 90, amount: 180, currency: "EGP", status: "Pending",
  canReview: true, canAccept: true,
};

beforeEach(() => { vi.stubGlobal("crypto", webcrypto); });
afterEach(() => { cleanup(); clearFinancialIntent("product-review:order"); vi.unstubAllGlobals(); vi.restoreAllMocks(); });

it("shows a real waiting state before either review decision is available", async () => {
  global.fetch = vi.fn(async () => Response.json(product));
  render(<ProductPaymentReview id="order" />);
  await screen.findByRole("heading", { name: product.productName });
  expect(screen.queryByRole("button", { name: /رفض/ })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /قبول/ })).not.toBeInTheDocument();
  expect(screen.getByText("بانتظار إثبات الدفع")).toBeVisible();
});

const asset = "10000000-0000-0000-0000-000000000001";
const pending = { ...product, proofAssetId: asset, proofUrl: `/api/v1/assets/${asset}` };
const approvalLabel = "قبول وخصم المخزون";

function proofImage() {
  return screen.getByAltText("صورة إثبات دفع المنتج");
}

function loadProof() {
  const image = proofImage();
  Object.defineProperties(image, {
    naturalWidth: { configurable: true, value: 24 },
    complete: { configurable: true, value: true },
    decode: { configurable: true, value: async () => undefined },
  });
  fireEvent.load(image);
}

async function approve() {
  const button = await screen.findByRole("button", { name: approvalLabel });
  loadProof();
  await screen.findByRole("img", { name: "صورة إثبات دفع المنتج" });
  fireEvent.click(button);
  expect(screen.getByRole("dialog")).toHaveTextContent("180 جنيه");
  fireEvent.click(screen.getByRole("button", { name: "تأكيد المراجعة" }));
}

it("keeps approval disabled through an image failure and retry while preserving the note and original link", async () => {
  const fetchOrder = vi.fn(async () => Response.json(pending));
  global.fetch = fetchOrder;
  render(<ProductPaymentReview id="order" />);
  const approval = await screen.findByRole("button", { name: approvalLabel });
  const note = screen.getByLabelText(/ملاحظة لولي الأمر/);
  expect(approval).toBeDisabled();
  fireEvent.click(approval);
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(screen.getByRole("link", { name: "فتح إثبات الدفع" })).toHaveAttribute("href", pending.proofUrl);
  fireEvent.change(note, { target: { value: "رقم تحويل غير واضح" } });
  fireEvent.error(proofImage());
  expect(screen.getByRole("alert")).toHaveTextContent("تعذر فتح صورة إثبات الدفع");
  expect(approval).toBeDisabled();
  expect(screen.getByRole("button", { name: "رفض الطلب" })).toBeEnabled();
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل الصورة" }));
  expect(approval).toBeDisabled();
  loadProof();
  await screen.findByRole("img", { name: "صورة إثبات دفع المنتج" });
  expect(approval).toBeEnabled();
  expect(note).toHaveValue("رقم تحويل غير واضح");
  expect(fetchOrder).toHaveBeenCalledTimes(1);
});

it.each([{ amount: null }, { amount: -1 }, { currency: "" }, { unitPrice: null }, { amount: 181 }, { quantity: 0 }])(
  "does not approve unreadable or inconsistent totals with %j even when the image loads",
  async override => {
    global.fetch = vi.fn(async () => Response.json({ ...pending, ...override }));
    render(<ProductPaymentReview id="order" />);
    const approval = await screen.findByRole("button", { name: approvalLabel });
    loadProof();
    await screen.findByRole("img", { name: "صورة إثبات دفع المنتج" });
    expect(approval).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent("تعذر التحقق من الكمية وسعر الطلب");
    expect(screen.getByRole("button", { name: "رفض الطلب" })).toBeEnabled();
    if ("currency" in override || "amount" in override && override.amount !== 181) {
      expect(screen.getByText("المبلغ غير متاح")).toBeVisible();
      expect(screen.queryByText("0 جنيه")).toBeNull();
    }
  },
);

it("retains a genuine zero total and the links of an already confirmed counter sale", async () => {
  global.fetch = vi.fn(async () => Response.json({ ...pending, unitPrice: 0, amount: 0 }));
  const view = render(<ProductPaymentReview id="order" />);
  const approval = await screen.findByRole("button", { name: approvalLabel });
  loadProof();
  await screen.findByRole("img", { name: "صورة إثبات دفع المنتج" });
  expect(approval).toBeEnabled();
  expect(screen.getAllByText("0 جنيه")).toHaveLength(2);
  view.unmount();
  global.fetch = vi.fn(async () => Response.json({ ...product, status: "Confirmed", productId: "catalog-item", cashSessionId: "shift" }));
  render(<ProductPaymentReview id="order" />);
  expect(await screen.findByRole("link", { name: "تفاصيل المنتج" })).toHaveAttribute("href", "/dashboard/content/catalog/catalog-item");
  expect(screen.getByRole("link", { name: "عرض وردية التحصيل" })).toHaveAttribute("href", "/dashboard/finance/cash-registers/sessions/shift");
  expect(screen.queryByRole("button", { name: approvalLabel })).toBeNull();
});

it("keeps a confirmed write locked until a read shows the same proof, amount and decision", async () => {
  let sent = false;
  let nextRead = 0;
  const writes: unknown[] = [];
  global.fetch = vi.fn(async (input, init) => {
    if (String(input).endsWith("/csrf")) return Response.json({ token: "csrf" });
    if (init?.method === "POST") {
      writes.push(JSON.parse(String(init.body)));
      sent = true;
      return Response.json({ status: "Confirmed" });
    }
    if (!sent) return Response.json(pending);
    nextRead++;
    if (nextRead === 1) return Response.json({}, { status: 503 });
    return Response.json({ ...pending, status: "Confirmed", amount: nextRead === 2 ? 181 : 180 });
  });
  render(<ProductPaymentReview id="order" />);
  await approve();
  await screen.findByText(/تعذر مطابقة القرار/);
  expect(screen.queryByRole("button", { name: "قبول وخصم المخزون" })).not.toBeInTheDocument();
  const check = screen.getByRole("button", { name: "التحقق من القرار" });
  fireEvent.click(check);
  await screen.findByText(/تعذر مطابقة القرار/);
  expect(screen.queryByText("ظهر القرار المحفوظ في الطلب.")).not.toBeInTheDocument();
  expect(screen.queryByText("181 جنيه")).not.toBeInTheDocument();
  fireEvent.click(check);
  expect(await screen.findByText("ظهر القرار المحفوظ في الطلب.")).toBeVisible();
  expect(writes).toEqual([{ accept: true, reason: null, expectedProofAssetId: asset }]);
});

it("only offers the same review after a lost request and an unchanged pending read", async () => {
  const writes: unknown[] = [];
  global.fetch = vi.fn(async (input, init) => {
    if (String(input).endsWith("/csrf")) return Response.json({ token: "csrf" });
    if (init?.method === "POST") {
      writes.push(JSON.parse(String(init.body)));
      if (writes.length === 1) throw new TypeError("offline");
      return Response.json({ status: "Confirmed" });
    }
    return Response.json(writes.length > 1 ? { ...pending, status: "Confirmed" } : pending);
  });
  render(<ProductPaymentReview id="order" />);
  await approve();
  await screen.findByText(/تعذر تأكيد حفظ المراجعة/);
  expect(screen.queryByRole("button", { name: "رفض الطلب" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "إعادة إرسال نفس القرار" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "التحقق من القرار" }));
  fireEvent.click(await screen.findByRole("button", { name: "إعادة إرسال نفس القرار" }));
  expect(await screen.findByText("ظهر القرار المحفوظ في الطلب.")).toBeVisible();
  expect(writes).toHaveLength(2);
  expect(writes[0]).toEqual(writes[1]);
});

it("gates approval and rejection independently for readers and non-financial reviewers", async () => {
  global.fetch = vi.fn(async () => Response.json({ ...pending, canReview: false, canAccept: false }));
  const view = render(<ProductPaymentReview id="order" />);
  await screen.findByRole("link", { name: "فتح إثبات الدفع" });
  expect(screen.queryByRole("button", { name: "رفض الطلب" })).not.toBeInTheDocument();
  view.unmount();
  global.fetch = vi.fn(async () => Response.json({ ...pending, canAccept: false }));
  render(<ProductPaymentReview id="order" />);
  expect(await screen.findByRole("button", { name: "رفض الطلب" })).toBeEnabled();
  expect(screen.queryByRole("button", { name: "قبول وخصم المخزون" })).not.toBeInTheDocument();
});

it("verifies a saved review after remount without storing the original decision or resending it", async () => {
  let written = false;
  let writes = 0;
  global.fetch = vi.fn(async (input, init) => {
    if (String(input).endsWith("/csrf")) return Response.json({ token: "csrf" });
    if (init?.method === "POST") {
      writes++;
      written = true;
      throw new TypeError("lost response");
    }
    return Response.json(written ? { ...pending, status: "Confirmed", reviewReason: "ملاحظة خاصة" } : pending);
  });
  const view = render(<ProductPaymentReview id="order" />);
  fireEvent.change(await screen.findByLabelText(/ملاحظة لولي الأمر/), { target: { value: "ملاحظة خاصة" } });
  await approve();
  await screen.findByText(/تعذر تأكيد حفظ المراجعة/);
  const saved = JSON.parse(sessionStorage.getItem("academy-finance:product-review:order")!);
  expect(Object.keys(saved).sort()).toEqual(["hash", "key"]);
  view.unmount();
  render(<ProductPaymentReview id="order" />);
  fireEvent.click(await screen.findByRole("button", { name: "التحقق من القرار" }));
  expect(await screen.findByText("ظهر القرار المحفوظ في الطلب.")).toBeVisible();
  expect(writes).toBe(1);
});
