import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { AcademyDateDefaults } from "./academy-date-defaults";
import { useDateFilterDefaults } from "./date-filter-defaults";

const query = new URLSearchParams();
vi.mock("next/navigation", () => ({ usePathname: () => "/dashboard/enrollment-requests", useSearchParams: () => query }));

function Period() {
  const { from, to } = useDateFilterDefaults();
  return <output>{from} إلى {to}</output>;
}

function Screen({ requireBothDates = false }: { requireBothDates?: boolean }) {
  return <AcademyDateDefaults title="طلبات الاشتراك الجديدة" context="اللاعبون" requireBothDates={requireBothDates}><Period /></AcademyDateDefaults>;
}

afterEach(() => {
  cleanup();
  for (const key of [...query.keys()]) query.delete(key);
  vi.unstubAllGlobals();
});

it("waits for the calendar, recovers a failed read and keeps the screen mounted when the URL changes", async () => {
  let release!: (response: Response) => void;
  const fetcher = vi.fn().mockImplementationOnce(() => new Promise<Response>(resolve => { release = resolve; }))
    .mockResolvedValueOnce(Response.json({ today: "2026-10-01" }));
  vi.stubGlobal("fetch", fetcher);
  const view = render(<Screen />);
  expect(screen.getByRole("heading")).toHaveTextContent("طلبات الاشتراك الجديدة");
  expect(screen.getByText("جارٍ تحميل تاريخ الأكاديمية…")).toBeVisible();
  expect(screen.queryByText(/2026-/)).not.toBeInTheDocument();
  await act(async () => release(new Response(null, { status: 503 })));
  expect(screen.getByRole("alert")).toHaveTextContent("تعذر تحميل تاريخ الأكاديمية.");
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  const period = await screen.findByText("2026-10-01 إلى 2026-10-01");
  query.set("allDates", "true");
  view.rerender(<Screen />);
  expect(screen.getByText("2026-10-01 إلى 2026-10-01")).toBe(period);
  expect(fetcher).toHaveBeenCalledTimes(2);
});

it("rejects an impossible date and aborts a retry when leaving the route", async () => {
  let signal: AbortSignal | undefined;
  const fetcher = vi.fn().mockResolvedValueOnce(Response.json({ today: "2026-02-30" }))
    .mockImplementationOnce((_url, options) => { signal = options.signal; return new Promise(() => {}); });
  vi.stubGlobal("fetch", fetcher);
  const view = render(<Screen />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل تاريخ الأكاديمية.");
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  view.unmount();
  expect(signal?.aborted).toBe(true);
});

it("skips the calendar for a complete explicit period and needs it for the payment queue's missing boundary", async () => {
  query.set("from", "2026-11-01");
  query.set("to", "2026-11-12");
  const fetcher = vi.fn().mockResolvedValueOnce(Response.json({ today: "2026-11-12" }));
  vi.stubGlobal("fetch", fetcher);
  const explicit = render(<Screen requireBothDates />);
  expect(fetcher).not.toHaveBeenCalled();
  explicit.unmount();
  query.delete("to");
  render(<Screen requireBothDates />);
  expect(await screen.findByText("2026-11-01 إلى 2026-11-12")).toBeVisible();
  expect(fetcher).toHaveBeenCalledTimes(1);
});
