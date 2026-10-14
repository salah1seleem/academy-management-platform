import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { FinancialDecisions } from "./financial-decisions";

afterEach(cleanup);

it("shows only stored financial decisions with source drill-down and branch/date filters", async () => {
  const calls: string[] = [];
  global.fetch = vi.fn(input => {
    const url = String(input); calls.push(url);
    const body = url.includes("options") ? { branches: [{ id: "branch-1", arabicName: "فرع تجريبي" }] } : {
      count: 2, items: [
        { sourceId: "expense-1", branchId: "branch-1", branchName: "فرع تجريبي", kind: "Expense", state: "Posted", actorName: "مالك تجريبي", occurredAtUtc: "2026-10-14T10:00:00Z", amount: 200, currency: "EGP" },
        { sourceId: "payment-1", branchId: "branch-1", branchName: "فرع تجريبي", kind: "RenewalProof", state: "Accepted", actorName: "إداري تجريبي", occurredAtUtc: "2026-10-14T09:00:00Z", amount: 900, currency: "EGP" },
      ],
    };
    return Promise.resolve(new Response(JSON.stringify(body), { headers: { "Content-Type": "application/json" } }));
  }) as typeof fetch;
  render(<FinancialDecisions initialFrom="2026-10-01" initialTo="2026-10-14" />);
  expect(await screen.findByText("ترحيل مصروف")).toBeVisible();
  expect(screen.getByText("قبول إثبات تجديد")).toBeVisible();
  expect(screen.getByText(/ليس سجلًا شاملًا/)).toBeVisible();
  expect(screen.getAllByRole("link", { name: "فتح السجل" })[0]).toHaveAttribute("href", "/dashboard/finance/operating-expenses?expenseId=expense-1");
  fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "branch-1" } });
  await waitFor(() => expect(calls.some(x => x.includes("financial-decisions") && x.includes("branchId=branch-1"))).toBe(true));
  fireEvent.click(screen.getByRole("button", { name: "مسح" }));
  await waitFor(() => expect(calls.some(x => x.includes("financial-decisions") && x.includes("allDates=true"))).toBe(true));
});
