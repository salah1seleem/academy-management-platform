import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import Renewal from "../app/guardian/renew/[enrollmentId]/page";
import ExternalRenewal from "../app/guardian/renew-for-another/code/page";
import { pendingFinancialIntent } from "../lib/pending-financial-intent";

const { push } = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock("next/navigation", () => ({ useParams: () => ({ enrollmentId: "enrollment-1" }), useRouter: () => ({ push }) }));
vi.mock("../lib/pending-financial-intent", () => ({
  pendingFinancialIntent: vi.fn(async () => "same-renewal-key"),
  clearFinancialIntent: vi.fn(),
}));

const json = (data: unknown, status = 200) => Response.json(data, { status });
const plan = { id: "plan-1", arabicName: "باقة تجريبية", planType: "Sessions", price: 450, currency: "EGP", sessionCount: 12 };
const enrollment = { id: "enrollment-1", player: "لاعب تجريبي", sport: "كرة القدم", branch: "فرع الاختبار" };
const review = (plans: typeof plan[] = [plan], openRenewal: object | null = null) => ({ enrollment, plans, openRenewal });

beforeEach(() => vi.clearAllMocks());
afterEach(cleanup);

it("separates a failed plan read from loading and empty results", async () => {
  let failed = true;
  global.fetch = vi.fn(async () => failed ? json({}, 503) : json(review([])));
  render(<Renewal />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل بيانات التسجيل والباقات.");
  expect(screen.queryByRole("status")).not.toBeInTheDocument();
  failed = false;
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل الباقات" }));
  expect(await screen.findByText("لا توجد باقات متاحة لهذا التسجيل. تواصل مع الأكاديمية.")).toBeVisible();
  expect(screen.queryByRole("button", { name: "متابعة إلى طلب الدفع" })).not.toBeInTheDocument();
});

it("keeps the same renewal command and plan after a lost response", async () => {
  let attempts = 0;
  global.fetch = vi.fn(async (path, init) => {
    const url = String(path);
    if (url.endsWith("/csrf")) return json({ token: "csrf" });
    if (url.endsWith("/renewal-review")) return json(review());
    if (url.endsWith("/renewals") && init?.method === "POST") {
      expect(new Headers(init.headers).get("Idempotency-Key")).toBe("same-renewal-key");
      if (++attempts === 1) throw new TypeError("connection lost");
      return json({ paymentId: "saved-payment" }, 201);
    }
    throw new Error(`Unexpected ${url}`);
  });
  render(<Renewal />);
  expect(await screen.findByText(enrollment.player)).toBeVisible();
  fireEvent.click(screen.getByRole("radio", { name: /باقة تجريبية/ }));
  const create = screen.getByRole("button", { name: "متابعة إلى طلب الدفع" });
  fireEvent.click(create);
  fireEvent.click(create);
  const retry = await screen.findByRole("button", { name: "إعادة إرسال نفس الطلب" });
  expect(screen.getByRole("radio", { name: /باقة تجريبية/ })).toBeDisabled();
  expect(attempts).toBe(1);
  fireEvent.click(retry);
  await waitFor(() => expect(push).toHaveBeenCalledWith("/guardian/checkout/saved-payment"));
  const body = { sportEnrollmentId: enrollment.id, subscriptionPlanId: plan.id };
  expect(vi.mocked(pendingFinancialIntent).mock.calls).toEqual([
    ["guardian-renewal:enrollment-1:plan-1", body], ["guardian-renewal:enrollment-1:plan-1", body],
  ]);
});

it.each([
  [{ kind: "Payment", paymentId: "current-payment" }, "متابعة طلب الدفع", "/guardian/checkout/current-payment"],
  [{ kind: "Family", familyRequestId: "family-request" }, "متابعة طلب الأسرة", "/guardian/family-renewals/family-request"],
] as const)("links to the authorized open request", async (open, action, href) => {
  global.fetch = vi.fn(async () => json(review([plan], open)));
  render(<Renewal />);
  expect(await screen.findByRole("link", { name: action })).toHaveAttribute("href", href);
  expect(screen.queryByRole("radio")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "متابعة إلى طلب الدفع" })).not.toBeInTheDocument();
});

it("explains an academy payment without exposing a checkout link", async () => {
  global.fetch = vi.fn(async () => json(review([], { kind: "Payment", paymentId: null })));
  render(<Renewal />);
  expect(await screen.findByText("تواصل مع الأكاديمية لمتابعة الطلب.")).toBeVisible();
  expect(screen.queryByRole("link", { name: "متابعة طلب الدفع" })).not.toBeInTheDocument();
  expect(screen.queryByRole("radio")).not.toBeInTheDocument();
});

it("reloads the review after a competing request without submitting a different plan or opening checkout", async () => {
  let reads = 0, writes = 0;
  global.fetch = vi.fn(async (path, init) => {
    const url = String(path);
    if (url.endsWith("/csrf")) return json({ token: "csrf" });
    if (url.endsWith("/renewal-review")) return ++reads === 1 ? json(review()) : json(review([], { kind: "Payment", paymentId: "original-payment" }));
    if (url.endsWith("/renewals") && init?.method === "POST") {
      writes++;
      return json({ code: "renewal_already_open", message: "يوجد طلب تجديد مفتوح." }, 409);
    }
    throw new Error(`Unexpected ${url}`);
  });
  render(<Renewal />);
  fireEvent.click(await screen.findByRole("radio"));
  fireEvent.click(screen.getByRole("button", { name: "متابعة إلى طلب الدفع" }));
  expect(await screen.findByRole("link", { name: "متابعة طلب الدفع" })).toHaveAttribute("href", "/guardian/checkout/original-payment");
  expect(push).not.toHaveBeenCalled();
  expect(writes).toBe(1);
  expect(screen.queryByRole("radio")).not.toBeInTheDocument();
});

it("never offers payment for a review response with a different enrollment", async () => {
  global.fetch = vi.fn(async () => json({ ...review([], { kind: "Payment", paymentId: "other-payment" }),
    enrollment: { ...enrollment, id: "other-enrollment" } }));
  render(<Renewal />);
  expect(await screen.findByRole("alert")).toBeVisible();
  expect(screen.queryByRole("link", { name: "متابعة طلب الدفع" })).not.toBeInTheDocument();
});

it("removes a previous beneficiary and ignores a slow lookup for an edited code", async () => {
  let finishFirst!: (response: Response) => void;
  const first = new Promise<Response>(resolve => { finishFirst = resolve; });
  const firstName = "لاعب قديم تجريبي";
  const secondName = "لاعب حالي تجريبي";
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith("/csrf")) return json({ token: "csrf" });
    if (String(path).endsWith("/resolve")) {
      const code = JSON.parse(String(init?.body)).reference;
      if (code === "old-code") return first;
      return json({ playerDisplayName: secondName, sport: "كرة القدم", academyName: "أكاديمية اختبار", plans: [plan] });
    }
    throw new Error("Unexpected request");
  });
  render(<ExternalRenewal />);
  const input = screen.getByLabelText("كود التجديد");
  fireEvent.change(input, { target: { value: "old-code" } });
  fireEvent.click(screen.getByRole("button", { name: "تحقّق من الكود" }));
  await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([path, init]) =>
    String(path).endsWith("/resolve") && String(init?.body).includes("old-code"),
  )).toBe(true));
  fireEvent.change(input, { target: { value: "new-code" } });
  fireEvent.click(screen.getByRole("button", { name: "تحقّق من الكود" }));
  expect(await screen.findByText(secondName)).toBeVisible();
  await act(async () => {
    finishFirst(json({ playerDisplayName: firstName, sport: "السباحة", academyName: "أكاديمية أخرى", plans: [plan] }));
    await first;
  });
  expect(screen.queryByText(firstName)).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("radio", { name: /باقة تجريبية/ }));
  fireEvent.change(input, { target: { value: "third-code" } });
  expect(screen.queryByText(secondName)).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "متابعة إلى طلب الدفع" })).not.toBeInTheDocument();
});

it("removes external payment actions after a conflict and accepts another code", async () => {
  let writes = 0;
  global.fetch = vi.fn(async (path, init) => {
    const url = String(path);
    if (url.endsWith("/csrf")) return json({ token: "csrf" });
    if (url.endsWith("/resolve")) {
      return json({ playerDisplayName: "مستفيد تجريبي", sport: "كرة القدم", academyName: "أكاديمية اختبار", plans: [plan],
        ...(writes && JSON.parse(String(init?.body)).reference === "first-code" && { openRenewal: { kind: "Unavailable" } }) });
    }
    if (url.endsWith("/renewals")) {
      writes++;
      return json({ code: "renewal_already_open", message: "يوجد طلب تجديد مفتوح." }, 409);
    }
    throw new Error(`Unexpected ${url}`);
  });
  render(<ExternalRenewal />);
  const input = screen.getByLabelText("كود التجديد");
  const verify = screen.getByRole("button", { name: "تحقّق من الكود" });
  fireEvent.change(input, { target: { value: "first-code" } });
  fireEvent.click(verify);
  fireEvent.click(await screen.findByRole("radio"));
  fireEvent.click(screen.getByRole("button", { name: "متابعة إلى طلب الدفع" }));
  expect(await screen.findByRole("status")).toHaveTextContent("تواصل مع الأكاديمية لمتابعة الطلب.");
  expect(screen.queryByRole("radio")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "متابعة إلى طلب الدفع" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "تحقّق من الكود" })).not.toBeInTheDocument();
  expect(push).not.toHaveBeenCalled();
  fireEvent.change(input, { target: { value: "other-code" } });
  const verifyAgain = screen.getByRole("button", { name: "تحقّق من الكود" });
  expect(verifyAgain).toBeEnabled();
  expect(screen.queryByRole("status")).not.toBeInTheDocument();
  fireEvent.click(verifyAgain);
  expect(await screen.findByRole("radio")).toBeEnabled();
  expect(writes).toBe(1);
});
