import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { CollectionList } from "./subscription-collections";

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(window.location.search) }));

const item = {
  id: "collection-1", receiptId: "a76bb096-6c71-49ad-856f-c9d1a7699544", receiptNumber: "RC-QA-720",
  player: "لاعب العرض", sport: "كرة القدم", amount: 720, currency: "EGP", paymentMethod: "Online", provider: "Manual",
};
const totals = [{ currency: "EGP", amount: 720 }, { currency: "USD", amount: 42 }];
const data = () => Response.json({ totals, items: [item, {
  ...item, id: "collection-2", receiptId: "c4127d8f-307c-4e96-a0f6-3659eef9c81e", receiptNumber: "RC-QA-42",
  amount: 42, currency: "USD", paymentMethod: "Cash",
}] });

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  window.history.replaceState(null, "", "/");
});

it("shows the payment method and retains separate totals when filtering to one receipt", async () => {
  window.history.replaceState(null, "", "/dashboard/subscriptions/collections?pageSize=10");
  const fetcher = vi.fn(async () => data());
  vi.stubGlobal("fetch", fetcher);
  render(<CollectionList />);
  const table = await screen.findByRole("table", { name: "تحصيلات الاشتراكات" });
  expect(within(table).getByText("الدفع الإلكتروني")).toBeVisible();
  expect(within(table).queryByText("تسجيل يدوي")).not.toBeInTheDocument();
  fireEvent.change(screen.getByRole("textbox", { name: "بحث التحصيلات" }), { target: { value: "rc-qa-720" } });
  const matching = within(table).getByRole("link", { name: "عرض الإيصال RC-QA-720" });
  expect(matching).toHaveAttribute("href", `/dashboard/subscriptions/receipts/${item.receiptId}?q=rc-qa-720&pageSize=10&report=collections`);
  expect(within(table).getAllByRole("link")).toHaveLength(1);
  const summary = screen.getByText("الإجمالي قبل ردّ المبالغ").closest("p")!;
  expect(summary).toHaveTextContent("720 جنيه");
  expect(summary).toHaveTextContent("42");
  expect(summary).not.toHaveTextContent("762");
  expect(fetcher).toHaveBeenCalledTimes(1);
});

it("recovers a malformed read with the current search and makes no write", async () => {
  window.history.replaceState(null, "", "/dashboard/subscriptions/collections?q=rc-qa-720");
  const fetcher = vi.fn().mockResolvedValueOnce(Response.json({ totals, items: [null] })).mockImplementationOnce(async () => data());
  vi.stubGlobal("fetch", fetcher);
  render(<CollectionList />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل تحصيلات الاشتراكات.");
  expect(screen.queryByText("الإجمالي قبل ردّ المبالغ")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  expect(await screen.findByRole("textbox", { name: "بحث التحصيلات" })).toHaveValue("rc-qa-720");
  expect(screen.getByRole("link", { name: "عرض الإيصال RC-QA-720" })).toBeVisible();
  expect(fetcher.mock.calls.map(([url, init]) => [url, init?.method, init?.cache])).toEqual([
    ["/api/v1/subscriptions/collections", undefined, "no-store"],
    ["/api/v1/subscriptions/collections", undefined, "no-store"],
  ]);
});

it("shows a permission error without retry or amounts for a forbidden read", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 403 })));
  render(<CollectionList />);
  expect(await screen.findByRole("alert")).toHaveTextContent("سجل التحصيلات غير متاح لحسابك.");
  expect(screen.queryByRole("button", { name: "إعادة المحاولة" })).not.toBeInTheDocument();
  expect(screen.queryByRole("table")).not.toBeInTheDocument();
});
