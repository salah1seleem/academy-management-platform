import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PlanList } from "./plan-catalog";

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(window.location.search) }));
const endpoint = "/api/v1/subscriptions/plans";
const plan = { id: "plan", arabicName: "موسم السباحة", sport: "السباحة", planType: "Duration", durationMonths: 5, price: 2400, currency: "EGP", isActive: true };
const json = (body: unknown) => new Response(JSON.stringify(body), { headers: { "Content-Type": "application/json" } });

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  window.history.replaceState(null, "", "/");
});

function api(listResponse: () => Response, write: () => Response = () => new Response(null, { status: 204 }), readOnly = false) {
  const request = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith("/me")) return json({ role: readOnly ? "AcademyAdmin" : "AcademyOwner" });
    if (url.endsWith("/my-access")) return json({ useActionGrants: true, actions: [{ module: "subscriptions", read: true, add: false, edit: false }] });
    if (url.endsWith("/csrf")) return json({ token: "csrf" });
    if (url === endpoint && !init?.method) return listResponse();
    if (url.endsWith("/status") && init?.method === "PUT") return write();
    throw Error(`Unexpected request ${url}`);
  });
  vi.stubGlobal("fetch", request);
  return request;
}

describe("subscription plan catalog", () => {
  it("shows calendar terms, keeps search in detail links and hides writes for a reader", async () => {
    api(() => json([plan]), undefined, true);
    render(<PlanList />);
    expect(await screen.findByText("٥ أشهر تقويمية")).toBeVisible();
    fireEvent.change(screen.getByRole("textbox", { name: "بحث في الباقات" }), { target: { value: "السباحة" } });
    const href = screen.getByRole("link", { name: "موسم السباحة" }).getAttribute("href")!;
    expect(new URL(href, window.location.href).searchParams.get("q")).toBe("السباحة");
    expect(new URLSearchParams(window.location.search).get("q")).toBe("السباحة");
    expect(screen.queryByRole("link", { name: "إضافة باقة" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "إيقاف" })).not.toBeInTheDocument();
  });

  it("shows a failed read separately from an empty list and can reload", async () => {
    let reads = 0;
    api(() => ++reads === 1 ? new Response(null, { status: 503 }) : json([plan]));
    render(<PlanList />);
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل الباقات.");
    expect(screen.queryByText("لا توجد باقات اشتراك بعد.")).not.toBeInTheDocument();
    expect(screen.queryByText("0 نتيجة")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل الباقات" }));
    expect(await screen.findByRole("link", { name: "موسم السباحة" })).toBeVisible();
  });

  it("keeps shared plans readable without offering writes outside the branch scope", async () => {
    api(() => json([
      { ...plan, canManagePublication: false },
      { ...plan, id: "branch-plan", arabicName: "موسم فرع واحد", canManagePublication: true },
    ]));
    render(<PlanList />);
    const shared = await screen.findByRole("row", { name: /موسم السباحة/ });
    const own = screen.getByRole("row", { name: /موسم فرع واحد/ });
    expect(within(shared).getByRole("link", { name: "موسم السباحة" })).toBeVisible();
    expect(within(shared).queryByRole("link", { name: "تعديل" })).not.toBeInTheDocument();
    expect(within(shared).queryByRole("button", { name: "إيقاف" })).not.toBeInTheDocument();
    expect(within(own).getByRole("button", { name: "إيقاف" })).toBeVisible();
  });

  it("explains the effect of stopping and verifies an uncertain response without repeating the write", async () => {
    let committed = false;
    const request = api(() => json([{ ...plan, isActive: !committed }]), () => {
      committed = true;
      return new Response(null, { status: 502 });
    });
    render(<PlanList />);
    fireEvent.click(await screen.findByRole("button", { name: "إيقاف" }));
    const dialog = screen.getByRole("dialog", { name: "إيقاف «موسم السباحة»؟" });
    expect(within(dialog).getByText(/تبقى الاشتراكات الحالية/)).toBeVisible();
    fireEvent.click(within(dialog).getByRole("button", { name: "إلغاء" }));
    expect(request.mock.calls.filter(([, init]) => init?.method === "PUT")).toHaveLength(0);
    fireEvent.click(screen.getByRole("button", { name: "إيقاف" }));
    fireEvent.click(screen.getByRole("button", { name: "إيقاف الباقة" }));
    fireEvent.click(await screen.findByRole("button", { name: "تحقّق من حالة الباقة" }));
    expect(await screen.findByText("موقوفة")).toBeVisible();
    expect(request.mock.calls.filter(([, init]) => init?.method === "PUT")).toHaveLength(1);
    expect(request.mock.calls.filter(([url]) => url === endpoint)).toHaveLength(2);
  });
});
