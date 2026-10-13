import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { FreeTraining } from "./free-training";
vi.mock("../lib/pending-financial-intent", () => ({ pendingFinancialIntent: vi.fn(async () => "stable-waiver-key") }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const options = { enrollment: { id: "e", playerId: "p", player: "طفل اصطناعي", sport: "كرة القدم", branch: "فرع اصطناعي" }, plans: [{ id: "plan", arabicName: "شهر تدريب", price: "1200.00", currency: "EGP" }] };
const quote = { quoteHash: "hash", player: "طفل اصطناعي", plan: "شهر تدريب", waivedAmount: "1200.00", payable: "0.00", currency: "EGP", startDate: "2026-09-28", endDate: "2026-10-27", reason: "منحة اصطناعية" };
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });
async function review() { await screen.findByRole("combobox", { name: "الباقة المعفاة" }); fireEvent.change(screen.getByRole("combobox"), { target: { value: "plan" } }); fireEvent.change(screen.getByRole("textbox", { name: "سبب الإعفاء" }), { target: { value: "منحة اصطناعية" } }); fireEvent.click(screen.getByRole("button", { name: "مراجعة الإعفاء" })); await screen.findByRole("region", { name: "مراجعة الإعفاء" }); }
describe("owner free training", () => {
  it("reviews without saving, requires consent and links the period rather than a fake receipt", async () => {
    const writes: string[] = []; global.fetch = vi.fn(async (url) => { const path = String(url); if (path.endsWith("/options")) return response(options); if (path.endsWith("/csrf")) return response({ token: "csrf" }); writes.push(path); return response(path.endsWith("/quote") ? quote : { periodId: "period" }); });
    render(<FreeTraining enrollmentId="e" />); await review(); expect(writes).toEqual(["/api/v1/finance/free-training/quote"]); expect(screen.getByRole("button", { name: "اعتماد الإعفاء وتفعيل الفترة" })).toBeDisabled(); fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "اعتماد الإعفاء وتفعيل الفترة" })); await screen.findByRole("status"); expect(screen.getByRole("link", { name: "عرض الاشتراك المعتمد" })).toHaveAttribute("href", "/dashboard/subscriptions/periods/period"); expect(screen.queryByRole("link", { name: /إيصال/ })).not.toBeInTheDocument(); expect(writes).toHaveLength(2);
  });
  it("keeps the original body and key on uncertain retry and does not expose technical errors", async () => {
    const approvals: unknown[] = []; global.fetch = vi.fn(async (url, init) => { const path = String(url); if (path.endsWith("/options")) return response(options); if (path.endsWith("/csrf")) return response({ token: "csrf" }); if (path.endsWith("/quote")) return response(quote); approvals.push([init?.body, (init?.headers as Record<string, string>)["Idempotency-Key"]]); throw new Error("SQL technical network"); });
    render(<FreeTraining enrollmentId="e" />); await review(); fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "اعتماد الإعفاء وتفعيل الفترة" })); expect(await screen.findByRole("alert")).not.toHaveTextContent("SQL"); expect(screen.getByRole("combobox")).toBeDisabled(); fireEvent.click(screen.getByRole("button", { name: "إعادة تأكيد نفس الاعتماد" })); await waitFor(() => expect(approvals).toHaveLength(2)); expect(approvals[0]).toEqual(approvals[1]);
  });
  it("does not render a privileged approval form when access is refused", async () => {
    global.fetch = vi.fn(async () => response({}, 403)); render(<FreeTraining enrollmentId="foreign" />); await screen.findByRole("alert"); expect(screen.queryByRole("combobox")).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "اعتماد الإعفاء وتفعيل الفترة" })).not.toBeInTheDocument();
  });
});
