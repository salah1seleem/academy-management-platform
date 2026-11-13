import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { FreeTraining } from "./free-training";
import { readFinancialIntentKey } from "../lib/pending-financial-intent";
vi.mock("../lib/pending-financial-intent", () => ({
  pendingFinancialIntent: vi.fn(async () => "stable-waiver-key"),
  readFinancialIntentKey: vi.fn(() => null),
  clearFinancialIntent: vi.fn(),
}));
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.mocked(readFinancialIntentKey).mockReturnValue(null); });
const options = { enrollment: { id: "e", playerId: "p", player: "طفل اصطناعي", sport: "كرة القدم", branch: "فرع اصطناعي" }, plans: [{ id: "plan", arabicName: "شهر تدريب", price: "1200.00", currency: "EGP" }] };
const quote = { quoteHash: "hash", player: "طفل اصطناعي", plan: "شهر تدريب", waivedAmount: "1200.00", payable: "0.00", currency: "EGP", startDate: "2026-09-28", endDate: "2026-10-27", reason: "منحة اصطناعية" };
const outcome = { sportEnrollmentId: "e", playerId: "p", periodId: "period", source: "ApprovedWaiver" };
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });
async function review() { await screen.findByRole("combobox", { name: "الباقة المعفاة" }); fireEvent.change(screen.getByRole("combobox"), { target: { value: "plan" } }); fireEvent.change(screen.getByRole("textbox", { name: "سبب الإعفاء" }), { target: { value: "منحة اصطناعية" } }); fireEvent.click(screen.getByRole("button", { name: "مراجعة الإعفاء" })); await screen.findByRole("region", { name: "مراجعة الإعفاء" }); }
describe("owner free training", () => {
  it("reviews without saving, requires consent and links the period rather than a fake receipt", async () => {
    const writes: string[] = []; global.fetch = vi.fn(async (url) => { const path = String(url); if (path.endsWith("/options")) return response(options); if (path.endsWith("/csrf")) return response({ token: "csrf" }); writes.push(path); return response(path.endsWith("/quote") ? quote : outcome); });
    render(<FreeTraining enrollmentId="e" />); await review(); expect(writes).toEqual(["/api/v1/finance/free-training/quote"]); expect(screen.getByRole("button", { name: "اعتماد الإعفاء" })).toBeDisabled(); fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "اعتماد الإعفاء" })); await screen.findByRole("status"); expect(screen.getByRole("link", { name: "عرض الاشتراك المعتمد" })).toHaveAttribute("href", "/dashboard/subscriptions/periods/period"); expect(screen.queryByRole("link", { name: /إيصال/ })).not.toBeInTheDocument(); expect(writes).toHaveLength(2);
  });
  it("reads the same attempt after a lost response without repeating approval", async () => {
    const requests: { path: string; key?: string }[] = [];
    let checks = 0;
    global.fetch = vi.fn(async (url, init) => {
      const path = String(url);
      if (path.endsWith("/options")) return response(options);
      if (path.endsWith("/csrf")) return response({ token: "csrf" });
      if (path.endsWith("/quote")) return response(quote);
      requests.push({ path, key: (init?.headers as Record<string, string>)["Idempotency-Key"] });
      if (path.endsWith("/attempt")) return ++checks === 1 ? response({}, 503) : response(outcome);
      throw new Error("SQL technical network");
    });
    render(<FreeTraining enrollmentId="e" />);
    await review();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "اعتماد الإعفاء" }));
    expect(await screen.findByRole("alert")).not.toHaveTextContent("SQL");
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "التحقق من النتيجة" }));
    await screen.findByRole("link", { name: "عرض الاشتراك المعتمد" });
    expect(requests.map(request => request.path.split("/").at(-1))).toEqual(["approve", "attempt", "attempt"]);
    expect(requests.every(request => request.key === "stable-waiver-key")).toBe(true);
  });
  it("does not render a privileged approval form when access is refused", async () => {
    global.fetch = vi.fn(async () => response({}, 403)); render(<FreeTraining enrollmentId="foreign" />); await screen.findByRole("alert"); expect(screen.queryByRole("combobox")).not.toBeInTheDocument(); expect(screen.queryByRole("button", { name: "اعتماد الإعفاء" })).not.toBeInTheDocument();
  });
  it("returns an explicitly rejected review to editable values and clears consent", async () => {
    global.fetch = vi.fn(async url => {
      const path = String(url);
      if (path.endsWith("/options")) return response(options);
      if (path.endsWith("/csrf")) return response({ token: "csrf" });
      if (path.endsWith("/quote")) return response(quote);
      return response({ title: "تغيرت الأهلية. حدّث المراجعة." }, 409);
    });
    render(<FreeTraining enrollmentId="e" />);
    await review();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "اعتماد الإعفاء" }));
    expect(await screen.findByRole("combobox")).toHaveValue("plan");
    expect(screen.getByRole("textbox", { name: "سبب الإعفاء" })).toHaveValue("منحة اصطناعية");
    fireEvent.click(screen.getByRole("button", { name: "مراجعة الإعفاء" }));
    expect(await screen.findByRole("button", { name: "اعتماد الإعفاء" })).toBeDisabled();
  });
  it("checks stored recovery before offering a new review", async () => {
    vi.mocked(readFinancialIntentKey).mockReturnValue("stable-waiver-key");
    const calls: string[] = [];
    global.fetch = vi.fn(async url => {
      calls.push(String(url));
      return String(url).endsWith("/attempt") ? response({}, 404) : response(options);
    });
    render(<FreeTraining enrollmentId="e" />);
    const restart = await screen.findByRole("button", { name: "فتح مراجعة جديدة" });
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
    expect(calls).toEqual(["/api/v1/finance/free-training/e/attempt"]);
    fireEvent.click(restart);
    await screen.findByRole("combobox", { name: "الباقة المعفاة" });
    expect(calls).toEqual(["/api/v1/finance/free-training/e/attempt", "/api/v1/finance/free-training/e/options"]);
  });
});
