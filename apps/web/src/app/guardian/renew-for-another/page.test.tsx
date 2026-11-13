import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import Page from "./page";
import LegacyCodePage from "./code/page";
import Layout from "./layout";

const navigation = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => navigation }));

const player = { name: "لاعب تجريبي للبحث", sport: "كرة القدم", branch: "فرع النخيل", reference: "SR1.test" };
const swimming = { ...player, sport: "السباحة", branch: "فرع النيل", reference: "SR1.swim" };
const plan = { id: "plan-1", arabicName: "اشتراك شهري", planType: "Duration", price: 900, currency: "EGP", durationMonths: 1 };
const beneficiary = { playerDisplayName: player.name, sport: player.sport, branch: player.branch, plans: [plan] };
const pendingId = "54efb32a-20a0-4c58-8d52-1b7b3bc26774";
const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status, headers: { "content-type": "application/json" } });

function network(accept: (route: string, body: Record<string, string>, init?: RequestInit) => Response | Promise<Response>) {
  global.fetch = vi.fn(async (path, init) => {
    const route = String(path).split("/").at(-1)!;
    if (route === "csrf") return json({ token: "csrf" });
    return accept(route, JSON.parse(String(init?.body ?? "{}")), init);
  });
}
const searchButton = () => screen.getByRole("button", { name: "بحث عن لاعب" });
const paymentButton = () => screen.getByRole("button", { name: "متابعة إلى طلب الدفع" });
function search(name = player.name) {
  fireEvent.change(screen.getByLabelText("اسم اللاعب"), { target: { value: name } });
  fireEvent.click(searchButton());
}
const choose = (row = player) => screen.findByRole("button", { name: `اختيار ${row.name} · ${row.sport} · ${row.branch}` });

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
});
afterEach(cleanup);

it("starts with name search, trims valid input and omits private response fields", async () => {
  const accept = vi.fn<(route: string, body: object) => Response>(() => json([{ ...player, phone: "private", playerId: "private" }]));
  network(accept);
  render(<Page />);
  expect(screen.getByRole("link", { name: "العودة إلى الرئيسية" })).toHaveAttribute("href", "/guardian");
  expect(screen.getByRole("link", { name: "لديّ كود تجديد" })).toHaveAttribute("href", "/guardian/renew-for-another/code");
  expect(screen.queryByLabelText("كود التجديد")).not.toBeInTheDocument();
  expect(searchButton()).toBeDisabled();
  fireEvent.change(screen.getByLabelText("اسم اللاعب"), { target: { value: "  أب " } });
  expect(searchButton()).toBeDisabled();
  expect(fetch).not.toHaveBeenCalled();
  search(` ${player.name} `);
  expect(await choose()).toBeVisible();
  expect(accept.mock.calls[0]?.slice(0, 2)).toEqual(["search", { name: player.name }]);
  expect(document.body).not.toHaveTextContent(/SR1|private/);
});

it("groups indistinguishable results and retains distinct choices", async () => {
  network(() => json([player, { ...player, reference: "SR1.same" }, swimming]));
  render(<Page />);
  search();
  const list = await screen.findByRole("list");
  expect(within(list).getAllByRole("listitem")).toHaveLength(2);
  expect(within(list).getByText(/أكثر من تسجيل/)).toBeVisible();
  expect(within(list).getAllByRole("button")).toEqual([await choose(swimming)]);
});

it("clears old results and recovers from empty and malformed responses", async () => {
  network(vi.fn().mockResolvedValueOnce(json([player])).mockResolvedValueOnce(json([]))
    .mockResolvedValueOnce(json([{ ...player, branch: null }])).mockResolvedValueOnce(json([swimming])));
  render(<Page />);
  search();
  expect(await choose()).toBeVisible();
  search("لاعب غير موجود");
  expect(screen.queryByText(player.name)).not.toBeInTheDocument();
  expect(await screen.findByText(/لا توجد نتائج/)).toHaveAttribute("role", "status");
  search();
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر البحث عن اللاعب");
  fireEvent.click(searchButton());
  expect(await choose(swimming)).toBeVisible();
});

it("ignores a completed stale search after the query changes", async () => {
  let complete!: (response: Response) => void;
  const old = new Promise<Response>(resolve => { complete = resolve; });
  const accept = vi.fn().mockReturnValueOnce(old).mockReturnValueOnce(json([swimming]));
  network(accept);
  render(<Page />);
  search("لاعب قديم");
  await waitFor(() => expect(accept).toHaveBeenCalledTimes(1));
  const first = accept.mock.calls[0][2].signal as AbortSignal;
  search("لاعب حالي");
  expect(first.aborted).toBe(true);
  expect(await choose(swimming)).toBeVisible();
  await act(async () => { complete(json([player])); await old; });
  expect(screen.queryByRole("button", { name: /اختيار .*كرة القدم/ })).not.toBeInTheDocument();
});

it("restores focus and discards the prior plan when changing registration", async () => {
  network((route, body) => route === "search" ? json([player, swimming]) :
    json(body.reference === player.reference ? beneficiary : { ...beneficiary, sport: swimming.sport, branch: swimming.branch, plans: [] }));
  render(<Page />);
  search();
  fireEvent.click(await choose());
  expect(await screen.findByRole("heading", { name: player.name })).toHaveFocus();
  expect(paymentButton()).toBeDisabled();
  fireEvent.click(screen.getByRole("radio", { name: /اشتراك شهري/ }));
  expect(paymentButton()).toBeEnabled();
  fireEvent.click(screen.getByRole("button", { name: "تغيير التسجيل" }));
  expect(await choose()).toHaveFocus();
  fireEvent.click(await choose(swimming));
  expect(await screen.findByText(/لا توجد باقات متاحة/)).toBeVisible();
  expect(screen.getByText("السباحة · فرع النيل")).toBeVisible();
  expect(screen.queryByRole("radio")).not.toBeInTheDocument();
});

it("allows retry after a temporary resolve failure and clears a changed identity", async () => {
  const resolve = vi.fn().mockResolvedValueOnce(json({}, 503)).mockResolvedValueOnce(json({ ...beneficiary, branch: "فرع مختلف" }));
  network(route => route === "search" ? json([player]) : resolve());
  render(<Page />);
  search();
  fireEvent.click(await choose());
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل نتيجة الطلب");
  fireEvent.click(await choose());
  await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("أعد البحث"));
  expect(screen.getByLabelText("اسم اللاعب")).toHaveFocus();
  expect(screen.queryByRole("list")).not.toBeInTheDocument();
});

it("clears a stale confirmed registration on write rejection", async () => {
  network(route => route === "search" ? json([player]) : route === "resolve" ? json(beneficiary) : json({}, 404));
  render(<Page />);
  search();
  fireEvent.click(await choose());
  fireEvent.click(await screen.findByRole("radio"));
  fireEvent.click(paymentButton());
  expect(await screen.findByRole("alert")).toHaveTextContent("أعد البحث");
  expect(screen.getByLabelText("اسم اللاعب")).toHaveFocus();
  expect(screen.queryByRole("radio")).not.toBeInTheDocument();
  expect(navigation.push).not.toHaveBeenCalled();
});

it.each([
  [{ kind: "Payment", paymentId: pendingId }, "متابعة طلب الدفع", "/guardian/checkout/"],
  [{ kind: "Family", familyRequestId: pendingId }, "متابعة طلب الأسرة", "/guardian/family-renewals/"],
])("shows the authenticated payer's pending request instead of plans", async (openRenewal, label, path) => {
  network(route => route === "search" ? json([player]) : json({ ...beneficiary, openRenewal }));
  render(<Page />);
  search();
  fireEvent.click(await choose());
  expect(await screen.findByRole("link", { name: label })).toHaveAttribute("href", path + pendingId);
  expect(screen.queryByRole("radio")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "متابعة إلى طلب الدفع" })).not.toBeInTheDocument();
});

it("shows another payer's conflict without a private link or an available plan", async () => {
  network(route => route === "search" ? json([player]) : json({
    ...beneficiary, openRenewal: { kind: "Unavailable", paymentId: pendingId, familyRequestId: pendingId },
  }));
  render(<Page />);
  search();
  fireEvent.click(await choose());
  expect(await screen.findByText("طلب تجديد مفتوح")).toBeVisible();
  expect(screen.getByRole("status")).toHaveTextContent("تواصل مع الأكاديمية");
  expect(screen.queryByRole("link", { name: /متابعة طلب/ })).not.toBeInTheDocument();
  expect(screen.queryByRole("radio")).not.toBeInTheDocument();
});

it("rereads a conflict, retries a failed status check and never repeats the write", async () => {
  const resolve = vi.fn().mockResolvedValueOnce(json(beneficiary)).mockResolvedValueOnce(json({}, 503))
    .mockResolvedValueOnce(json({ ...beneficiary, openRenewal: { kind: "Payment", paymentId: pendingId } }));
  const write = vi.fn(() => json({ code: "renewal_already_open", message: "يوجد طلب تجديد مفتوح." }, 409));
  network(route => route === "search" ? json([player]) : route === "resolve" ? resolve() : write());
  render(<Page />);
  search();
  fireEvent.click(await choose());
  fireEvent.click(await screen.findByRole("radio"));
  fireEvent.click(paymentButton());
  fireEvent.click(await screen.findByRole("button", { name: "إعادة التحقق" }));
  expect(await screen.findByRole("link", { name: "متابعة طلب الدفع" })).toHaveAttribute("href", "/guardian/checkout/" + pendingId);
  expect(screen.queryByRole("radio")).not.toBeInTheDocument();
  expect(write).toHaveBeenCalledTimes(1);
});

it("ignores a conflict read after the payer changes registration", async () => {
  let finish!: (response: Response) => void;
  const late = new Promise<Response>(resolve => { finish = resolve; });
  let reads = 0;
  const recheck = vi.fn();
  network((route, body, init) => {
    if (route === "search") return json([player, swimming]);
    if (route === "renewals") return json({ code: "renewal_already_open", message: "يوجد طلب تجديد مفتوح." }, 409);
    if (body.reference === swimming.reference) return json({ ...beneficiary, sport: swimming.sport, branch: swimming.branch });
    if (++reads === 1) return json(beneficiary);
    recheck(init?.signal);
    return late;
  });
  render(<Page />);
  search();
  fireEvent.click(await choose());
  fireEvent.click(await screen.findByRole("radio"));
  fireEvent.click(paymentButton());
  await waitFor(() => expect(recheck).toHaveBeenCalledOnce());
  const change = screen.getByRole("button", { name: "تغيير التسجيل" });
  await waitFor(() => expect(change).toBeEnabled());
  fireEvent.click(change);
  fireEvent.click(await choose(swimming));
  expect(await screen.findByRole("button", { name: "متابعة إلى طلب الدفع" })).toBeDisabled();
  expect(await screen.findByText("السباحة · فرع النيل")).toBeVisible();
  await act(async () => {
    finish(json({ ...beneficiary, openRenewal: { kind: "Payment", paymentId: pendingId } }));
    await late;
  });
  expect(recheck.mock.calls[0][0].aborted).toBe(true);
  expect(screen.queryByRole("link", { name: "متابعة طلب الدفع" })).not.toBeInTheDocument();
  expect(paymentButton()).toBeDisabled();
});

it("removes the pending link when changing the academy code", async () => {
  network((_, body) => body.reference === "first-code"
    ? json({ ...beneficiary, openRenewal: { kind: "Payment", paymentId: pendingId } })
    : json({}, 404));
  render(<LegacyCodePage />);
  const input = screen.getByLabelText("كود التجديد");
  fireEvent.change(input, { target: { value: "first-code" } });
  fireEvent.click(screen.getByRole("button", { name: "تحقّق من الكود" }));
  expect(await screen.findByRole("link", { name: "متابعة طلب الدفع" })).toHaveAttribute("href", "/guardian/checkout/" + pendingId);
  fireEvent.change(input, { target: { value: "another-code" } });
  expect(screen.queryByRole("link", { name: "متابعة طلب الدفع" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "تحقّق من الكود" }));
  expect(await screen.findByRole("alert")).toBeVisible();
});

it("preserves the code lookup and keeps its beneficiary response minimal", async () => {
  network((route, body) => {
    expect([route, body]).toEqual(["resolve", { reference: "RNW-DEMO-NG-0003-7K9M" }]);
    return json({ ...beneficiary, academyName: "أكاديمية الاختبار" });
  });
  render(<LegacyCodePage />);
  expect(screen.getByRole("link", { name: "العودة إلى البحث بالاسم" })).toHaveAttribute("href", "/guardian/renew-for-another");
  fireEvent.change(screen.getByLabelText("كود التجديد"), { target: { value: "RNW-DEMO-NG-0003-7K9M" } });
  fireEvent.click(screen.getByRole("button", { name: "تحقّق من الكود" }));
  expect(await screen.findByText(player.name)).toBeVisible();
  expect(screen.getByText("كرة القدم · أكاديمية الاختبار")).toBeVisible();
});

it("waits for a guardian session and retries a failed session check", async () => {
  network(vi.fn().mockResolvedValueOnce(json({}, 503)).mockResolvedValueOnce(json({ role: "Guardian" })));
  render(<Layout><Page /></Layout>);
  expect(screen.queryByLabelText("اسم اللاعب")).not.toBeInTheDocument();
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر التحقق");
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  expect(await screen.findByLabelText("اسم اللاعب")).toBeVisible();
});

it.each([json({}, 401), json({ role: "AcademyOwner" })])("redirects sessions without the guardian role", async response => {
  network(() => response);
  render(<Layout><Page /></Layout>);
  await waitFor(() => expect(navigation.replace).toHaveBeenCalledWith("/guardian/sign-in"));
  expect(screen.queryByLabelText("اسم اللاعب")).not.toBeInTheDocument();
});
