import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AdminRenewalFlow } from "./subscriptions";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }), useSearchParams: () => new URLSearchParams() }));

const row = { id: "enrollment-1", playerId: "player-1", playerCode: "NG-0003", player: "عمر أحمد حسن", sport: "كرة القدم", branch: "مدينة نصر", group: "مجموعة أ", guardianPhone: "+201000000002", period: null };
const details = { enrollment: { ...row, sportId: "sport-1" }, current: null, previewStart: "2026-09-28", plans: [{ id: "plan-1", arabicName: "اشتراك شهري", planType: "Duration", price: 900, currency: "EGP", durationDays: 30 }] };

function response(data: unknown, ok = true) { return { ok, json: async () => data }; }

beforeEach(() => {
  push.mockReset();
  global.fetch = vi.fn(async input => {
    const url = String(input);
    if (url.includes("admin-renewals/enrollments/enrollment-1")) return response(details) as Response;
    if (url.includes("admin-renewals/enrollments")) return response([row]) as Response;
    if (url.includes("auth/csrf")) return response({ token: "csrf" }) as Response;
    if (url.includes("admin-renewals")) return response({ renewalId: "renewal-1", paymentId: "payment-1" }) as Response;
    return response({}) as Response;
  }) as typeof fetch;
});
afterEach(cleanup);

describe("admin renewal flow", () => {
  it("searches by player, code, or guardian phone", async () => { render(<AdminRenewalFlow />); expect(await screen.findByPlaceholderText("اسم اللاعب أو الكود أو هاتف ولي الأمر")).toBeVisible(); expect(await screen.findByText(/عمر أحمد حسن/)).toBeVisible(); });
  it("shows enrollment context and Arabic plan values", async () => { render(<AdminRenewalFlow />); fireEvent.click(await screen.findByRole("button", { name: "اختيار" })); expect(await screen.findByText("بداية الفترة المتوقعة")).toBeVisible(); expect(screen.getByText("بالمدة", { exact: false })).toBeVisible(); expect(screen.queryByText("Duration")).not.toBeInTheDocument(); expect(screen.queryByText("EGP")).not.toBeInTheDocument(); });
  it("shows that creation has no immediate financial effect", async () => { render(<AdminRenewalFlow />); fireEvent.click(await screen.findByRole("button", { name: "اختيار" })); expect(await screen.findByText(/لا يتم التحصيل أو تمديد الاشتراك قبل تأكيد الدفع التجريبي/)).toBeVisible(); });
  it("creates a request and navigates to its payment", async () => { render(<AdminRenewalFlow />); fireEvent.click(await screen.findByRole("button", { name: "اختيار" })); fireEvent.click(await screen.findByRole("radio")); fireEvent.click(screen.getByRole("button", { name: "إنشاء طلب التجديد والدفع" })); await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/subscriptions/payments/payment-1")); });
  it("renders server conflicts in Arabic", async () => { vi.mocked(fetch).mockImplementation(async input => { const url = String(input); if (url.includes("auth/csrf")) return response({ token: "csrf" }) as Response; if (url.endsWith("/admin-renewals")) return response({ message: "يوجد طلب تجديد ودفع جارٍ بالفعل لهذا التسجيل." }, false) as Response; if (url.includes("/enrollments/enrollment-1")) return response(details) as Response; return response([row]) as Response; }); render(<AdminRenewalFlow />); fireEvent.click(await screen.findByRole("button", { name: "اختيار" })); fireEvent.click(await screen.findByRole("radio")); fireEvent.click(screen.getByRole("button", { name: "إنشاء طلب التجديد والدفع" })); expect(await screen.findByRole("alert")).toHaveTextContent("يوجد طلب تجديد ودفع جارٍ بالفعل"); });
});
