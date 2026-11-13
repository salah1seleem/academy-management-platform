import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { PlayerMonthly } from "./player-monthly";

vi.mock("next/navigation", async () => {
  const { useSyncExternalStore } = await import("react");
  return {
    usePathname: () => window.location.pathname,
    useSearchParams: () => new URLSearchParams(useSyncExternalStore(listener => {
      window.addEventListener("popstate", listener);
      return () => window.removeEventListener("popstate", listener);
    }, () => window.location.search)),
  };
});

const nativeReplace = window.history.replaceState.bind(window.history);
beforeEach(() => {
  nativeReplace(null, "", "/");
  vi.spyOn(window.history, "replaceState").mockImplementation((state, title, url) => {
    nativeReplace(state, title, url);
    window.dispatchEvent(new PopStateEvent("popstate"));
  });
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

const report = {
  enrollment: { id: "e", playerId: "child", player: "لاعب اصطناعي", sport: "كرة القدم" },
  enrollments: [{ id: "e", sport: "كرة القدم", branch: "فرع اصطناعي" }, { id: "swim", sport: "السباحة", branch: "فرع سباحة" }],
  month: "2026-10", from: "2026-10-01", to: "2026-10-18", asOfDate: "2026-10-18", page: 0,
  counts: { skills: 28, badges: 1, certificates: 0 },
  items: [
    { id: "r", kind: "Skill", on: "2026-10-17", title: "عدد التمريرات", state: "Learning", value: 0, unit: "Count", note: "بروتوكول اصطناعي واحد", actor: "مدرب اصطناعي", planId: "p" },
    { id: "b", kind: "Badge", on: "2026-10-18", title: "روح التعاون", state: "Revoked", value: 10, unit: "Encouragement", note: null, actor: "مدرب اصطناعي" },
  ],
  hasMore: true,
};

it("shows separate evidence, a measured zero, current revocation, and a full-month export link", async () => {
  global.fetch = vi.fn(async () => Response.json(report)) as typeof fetch;
  render(<PlayerMonthly playerId="child" />);
  await screen.findByText("عدد التمريرات");
  expect(screen.getByText("شارة مسحوبة")).toBeVisible();
  expect(screen.getByText("نقاط تشجيعية غير مالية", { exact: false })).toBeVisible();
  expect(screen.getByText("0", { selector: "bdi" })).toBeVisible();
  expect(screen.getByRole("link", { name: "فتح خطة المهارة" })).toHaveAttribute("href", "/guardian/children/child/skills/plans/p");
  expect(screen.getByRole("link", { name: "فتح سجل الشارات" })).toHaveAttribute("href", "/guardian/children/child/badges?enrollmentId=e");
  expect(screen.getByRole("link", { name: "تنزيل CSV" })).toHaveAttribute("href", "/api/v1/guardian/children/child/monthly?enrollmentId=e&month=2026-10&format=csv");
  fireEvent.click(screen.getByRole("button", { name: "الصفحة التالية" }));
  await waitFor(() => expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining("page=1"), expect.anything()));
  expect(new URLSearchParams(location.search).get("enrollmentId")).toBe("e");
});

it("restores the URL scope, resets page when changing month and sport, and clears evidence after denial", async () => {
  nativeReplace(null, "", "/guardian/children/child/monthly?month=2026-10&enrollmentId=e&page=2");
  let allowed = true;
  global.fetch = vi.fn(async () => allowed ? Response.json(report) : Response.json({}, { status: 404 })) as typeof fetch;
  render(<PlayerMonthly playerId="child" />);
  await screen.findByText("عدد التمريرات");
  expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining("page=2&month=2026-10&enrollmentId=e"), expect.anything());
  fireEvent.change(screen.getByLabelText("شهر التقرير"), { target: { value: "9" } });
  fireEvent.change(screen.getByLabelText("التسجيل الرياضي"), { target: { value: "swim" } });
  fireEvent.submit(screen.getByRole("button", { name: "عرض التقرير" }).closest("form")!);
  await waitFor(() => expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining("page=0&month=2026-09&enrollmentId=swim"), expect.anything()));
  allowed = false;
  fireEvent.focus(window);
  await screen.findByRole("alert");
  expect(screen.queryByText("عدد التمريرات")).not.toBeInTheDocument();
  expect(screen.queryByRole("link", { name: "تنزيل CSV" })).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "عرض الشهر الحالي" })).toBeVisible();
});

it("shows an empty month without reporting a player score or redundant pagination", async () => {
  global.fetch = vi.fn(async () => Response.json({ ...report, counts: { skills: 0, badges: 0, certificates: 0 }, items: [], hasMore: false })) as typeof fetch;
  render(<PlayerMonthly enrollmentId="e" />);
  await screen.findByText("لا توجد مراجعات أو شارات أو شهادات مسجلة لهذا الشهر.");
  expect(screen.queryByRole("button", { name: "الصفحة التالية" })).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "العودة إلى ملف اللاعب" })).toHaveAttribute("href", "/dashboard/players/child?tab=development");
});

it("reports export rejection without navigating away from the selected month", async () => {
  let rejectExport!: (response: Response) => void;
  const exportReply = new Promise<Response>(resolve => { rejectExport = resolve; });
  global.fetch = vi.fn(async path => String(path).includes("format=csv")
    ? exportReply
    : Response.json(report)) as typeof fetch;
  nativeReplace(null, "", "/guardian/children/child/monthly?month=2026-10&page=1");
  render(<PlayerMonthly playerId="child" />);
  const link = await screen.findByRole("link", { name: "تنزيل CSV" });
  fireEvent.click(link);
  expect(link).toHaveAttribute("aria-busy", "true");
  await act(async () => { rejectExport(Response.json({ message: "يتجاوز الشهر حد التصدير." }, { status: 409 })); });
  expect(await screen.findByRole("alert")).toHaveTextContent("يتجاوز الشهر حد التصدير.");
  expect(location.search).toBe("?month=2026-10&page=1");
  expect(screen.getByText("عدد التمريرات")).toBeVisible();
  expect(global.fetch).toHaveBeenCalledWith("/api/v1/guardian/children/child/monthly?enrollmentId=e&month=2026-10&format=csv", expect.anything());
});
