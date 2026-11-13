import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { formatDateAr } from "../lib/formatters";
import { SubscriptionSnapshot } from "./subscription-snapshot";

let search = new URLSearchParams();
vi.mock("next/navigation", () => ({ useSearchParams: () => search }));

const enrollment = {
  id: "e1", playerId: "p1", name: "لاعب خيالي", photoReference: null,
  sport: "كرة القدم", branch: "فرع تجريبي", group: "مجموعة تجريبية",
  active: true, endDate: "2026-10-07", price: 750, currency: "EGP",
  plan: "باقة الحصص", planType: "Sessions", remainingSessions: 2,
};
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it("reloads the selected page with the same branch, period and subscription state", async () => {
  search = new URLSearchParams("branchId=b1&asOf=2026-09-30&from=2026-09-01&state=expiring&page=2");
  const fetcher = vi.fn(async () => Response.json({
    asOf: "2026-09-30", from: "2026-09-01", total: 101, items: [enrollment], canOpenPlayer: true,
  }));
  vi.stubGlobal("fetch", fetcher);
  render(<SubscriptionSnapshot />);
  const list = await screen.findByRole("table", { name: "الاشتراكات التي تنتهي قريبًا" });
  expect(fetcher).toHaveBeenCalledWith("/api/v1/subscriptions/home/items?" + search, expect.anything());
  expect(within(list).getByRole("link", { name: "لاعب خيالي" })).toHaveAttribute("href", "/dashboard/players/p1");
  expect(within(list).getAllByRole("link")).toHaveLength(1);
  expect(within(list).getByText("ينتهي قريبًا")).toBeVisible();
  expect(within(list).getByText("الحصص المتبقية 2")).toBeVisible();
  expect(screen.getByText(formatDateAr("2026-09-30"))).toBeVisible();
  expect(screen.getByRole("link", { name: "الصفحة التالية" })).toHaveAttribute("href", "/dashboard/players/subscriptions?" + search.toString().replace("page=2", "page=3"));
});

it("renders each player once with their sports, without a profile action when access is absent", async () => {
  search = new URLSearchParams("uniquePlayers=true");
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({
    asOf: "2026-09-30", total: 1, canOpenPlayer: false,
    items: [{ ...enrollment, sports: [enrollment, { ...enrollment, id: "e2", sport: "السباحة", active: false, frozen: true }] }],
  })));
  const { container } = render(<SubscriptionSnapshot />);
  const list = await screen.findByRole("table", { name: "قائمة اللاعبين" });
  expect(within(list).getAllByText("لاعب خيالي")).toHaveLength(1);
  expect(within(list).getByText("السباحة")).toBeVisible();
  expect(within(list).getByText("مجمّد")).toBeVisible();
  expect(within(list).queryByRole("link")).toBeNull();
  expect(container.querySelector('img')?.alt).toBe("");
});

it("retries a failed cumulative list without reapplying a legacy lower date bound", async () => {
  search = new URLSearchParams("asOf=2026-09-30&from=2026-09-01&dimension=months&key=2026-08-31");
  const fetcher = vi.fn()
    .mockResolvedValueOnce(new Response(null, { status: 503 }))
    .mockResolvedValueOnce(Response.json({ asOf: "2026-08-31", total: 0, items: [] }));
  vi.stubGlobal("fetch", fetcher);
  render(<SubscriptionSnapshot />);
  expect(screen.getByRole("heading", { name: "تسجيلات بدأ اشتراكها" })).toBeVisible();
  fireEvent.click(await screen.findByRole("button", { name: "إعادة المحاولة" }));
  expect(await screen.findByText("لا توجد سجلات في هذا النطاق.")).toBeVisible();
  expect(fetcher.mock.calls.map(([url]) => url)).toEqual([
    "/api/v1/subscriptions/home/items?asOf=2026-09-30&dimension=months&key=2026-08-31&page=1",
    "/api/v1/subscriptions/home/items?asOf=2026-09-30&dimension=months&key=2026-08-31&page=1",
  ]);
  expect(screen.queryByRole("table")).toBeNull();
});

it("hides prior-page results while the new page is pending and starts a new scope on page one", async () => {
  search = new URLSearchParams("branchId=b1&asOf=2026-09-30");
  let finishPage: (response: Response) => void = () => {};
  let finishScope: (response: Response) => void = () => {};
  const data = (name: string, asOf = "2026-09-30") => Response.json({ items: [{ ...enrollment, name }], total: 60, asOf });
  const fetcher = vi.fn((url: unknown) => {
    const path = String(url);
    if (path.includes("b2")) return new Promise<Response>(resolve => { finishScope = resolve; });
    if (path.includes("page=2")) return new Promise<Response>(resolve => { finishPage = resolve; });
    return Promise.resolve(data("لاعب سبتمبر الخيالي"));
  });
  vi.stubGlobal("fetch", fetcher);
  const view = render(<SubscriptionSnapshot />);
  expect(await screen.findByText("لاعب سبتمبر الخيالي")).toBeVisible();
  const price = screen.getByRole("checkbox", { name: "إظهار عمود قيمة الباقة", hidden: true });
  fireEvent.click(price);
  expect(price).not.toBeChecked();
  expect(screen.getByText(formatDateAr("2026-09-30"))).toHaveAttribute("datetime", "2026-09-30");
  search = new URLSearchParams(screen.getByRole("link", { name: "الصفحة التالية" }).getAttribute("href")?.split("?")[1]);
  view.rerender(<SubscriptionSnapshot />);
  expect(screen.queryByText("لاعب سبتمبر الخيالي")).toBeNull();
  await act(async () => finishPage(data("لاعب الصفحة الثانية الخيالي")));
  expect(await screen.findByText("لاعب الصفحة الثانية الخيالي")).toBeVisible();
  expect(screen.queryByRole("columnheader", { name: "قيمة الباقة" })).not.toBeInTheDocument();
  search = new URLSearchParams("branchId=b2&asOf=2026-08-31");
  view.rerender(<SubscriptionSnapshot />);
  expect(screen.queryByText("لاعب الصفحة الثانية الخيالي")).toBeNull();
  expect(fetcher).toHaveBeenLastCalledWith("/api/v1/subscriptions/home/items?branchId=b2&asOf=2026-08-31&page=1", expect.anything());
  await act(async () => finishScope(data("لاعب أغسطس الخيالي", "2026-08-31")));
  expect(await screen.findByText("لاعب أغسطس الخيالي")).toBeVisible();
  expect(screen.queryByRole("link", { name: "الصفحة السابقة" })).toBeNull();
});

it("ignores a late response from a previous branch and displays the current access rejection", async () => {
  search = new URLSearchParams("branchId=b1");
  let late: (response: Response) => void = () => {};
  vi.stubGlobal("fetch", vi.fn((url: unknown) => String(url).includes("b1")
    ? new Promise<Response>(resolve => { late = resolve; })
    : Promise.resolve(new Response(null, { status: 403 }))));
  const view = render(<SubscriptionSnapshot />);
  search = new URLSearchParams("branchId=b2");
  view.rerender(<SubscriptionSnapshot />);
  expect(await screen.findByRole("alert")).toHaveTextContent("لا يمكن عرض الاشتراكات بهذا النطاق في حسابك الحالي.");
  await act(async () => late(Response.json({ items: [enrollment], total: 1, asOf: "2026-09-30" })));
  expect(screen.queryByText(enrollment.name)).toBeNull();
});

it("links a cumulative list back to the same dashboard analysis and keeps return metadata out of the API request", async () => {
  search = new URLSearchParams("branchId=b1&sportId=s1&asOf=2026-09-30&state=all&dimension=months&key=2026-08-31&origin=owner-subscriptions&returnFrom=2026-09-01");
  const fetcher = vi.fn(async () => Response.json({ asOf: "2026-08-31", total: 1, items: [enrollment] }));
  vi.stubGlobal("fetch", fetcher);
  render(<SubscriptionSnapshot />);
  expect(screen.getByRole("link", { name: "العودة إلى الاشتراكات" })).toHaveAttribute("href",
    "/dashboard?branchId=b1&subscriptionSport=s1&subscriptionTo=2026-09-30&subscriptionFrom=2026-09-01&subscriptionDimension=months#academy-subscriptions");
  const list = await screen.findByRole("table", { name: "تسجيلات بدأ اشتراكها" });
  expect(within(list).getByText(enrollment.name)).toBeVisible();
  expect(within(list).queryByRole("columnheader", { name: "الحالة" })).toBeNull();
  expect(within(list).queryByText(/الحصص المتبقية/)).toBeNull();
  expect(fetcher).toHaveBeenCalledWith(
    "/api/v1/subscriptions/home/items?branchId=b1&sportId=s1&asOf=2026-09-30&state=all&dimension=months&key=2026-08-31&page=1",
    expect.anything(),
  );
});

it("returns to the owner summary in the source period and distinguishes players with the same name", async () => {
  search = new URLSearchParams("uniquePlayers=true&origin=owner-cockpit&from=2026-09-01&to=2026-09-30&branchId=b1&page=2");
  const fetcher = vi.fn(async () => Response.json({
    total: 52, asOf: "2026-09-30", from: "2026-09-01", canOpenPlayer: true,
    items: [
      { ...enrollment, playerCode: "PL-51", sports: [enrollment] },
      { ...enrollment, id: "e2", playerId: "p2", playerCode: "PL-52", sports: [{ ...enrollment, id: "e2" }] },
    ],
  }));
  vi.stubGlobal("fetch", fetcher);
  render(<SubscriptionSnapshot />);
  expect(screen.getByRole("link", { name: "العودة إلى ملخص الأكاديمية" })).toHaveAttribute("href",
    "/dashboard?branchId=b1&period-cockpit=range%2C2026-09-01%2C2026-09-30#academy-cockpit");
  const list = await screen.findByRole("table", { name: "قائمة اللاعبين" });
  expect(list).toHaveAttribute("aria-rowcount", "53");
  expect(within(list).getByRole("row", { name: /PL-51/ })).toHaveAttribute("aria-rowindex", "52");
  expect(within(list).getByRole("link", { name: /PL-51/ })).toHaveAttribute("href", "/dashboard/players/p1");
  expect(within(list).getByRole("link", { name: /PL-52/ })).toHaveAttribute("href", "/dashboard/players/p2");
  expect(screen.getByText("51–52", { exact: true })).toHaveAttribute("dir", "ltr");
  expect(fetcher).toHaveBeenCalledWith("/api/v1/subscriptions/home/items?uniquePlayers=true&from=2026-09-01&to=2026-09-30&branchId=b1&page=2", expect.anything());
});
