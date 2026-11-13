import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { AcademyActivities, AcademyActivityPreview } from "./academy-activities";

const event = {
  id: "8e90c0e5-831f-4db1-bdec-d021b2f75a3c",
  title: "احتفال الموسم التجريبي",
  body: "التجمع يوم 13 نوفمبر الساعة الخامسة مساءً.\nالتكريم بعد المباراة الودية.",
  kind: "Event",
  publishedAtUtc: "2026-11-04T12:00:00Z",
  imageUrl: "/api/v1/assets/c5975f89-aab3-4353-86d1-5e26967c59da",
};
const announcement = { ...event, id: "70f6b833-6de8-45f4-a2a5-9cb95b286381", title: "موعد التدريب التجريبي", kind: "Announcement", imageUrl: null };
const feed = (items: unknown[], total = items.length) => Response.json({ items, total, pageSize: 20 });

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it("labels publication separately and keeps the events page in the article link", async () => {
  const fetcher = vi.fn(async () => feed([event], 45));
  vi.stubGlobal("fetch", fetcher);
  render(<AcademyActivities search={{ kind: "Event", page: "3" }} />);
  const card = await screen.findByRole("link", { name: /احتفال الموسم التجريبي/ });
  expect(within(card).getByText("4 نوفمبر 2026")).toHaveAttribute("datetime", event.publishedAtUtc);
  expect(card).toHaveTextContent("نُشر في");
  expect(card).toHaveAttribute("href", `/dashboard/activities?kind=Event&page=3&item=${event.id}`);
  expect(screen.getByRole("link", { name: "الفعاليات" })).toHaveAttribute("aria-current", "page");
  expect(screen.getByRole("link", { name: "السابق" })).toHaveAttribute("href", "/dashboard/activities?kind=Event&page=2");
  expect(screen.queryByText(/التجمع يوم/)).not.toBeInTheDocument();
});

it("loads only the selected activity and returns the guardian to the same tab and page", async () => {
  const fetcher = vi.fn(async () => feed([event, announcement]));
  vi.stubGlobal("fetch", fetcher);
  render(<AcademyActivities guardian search={{ kind: "Event", page: "3", item: event.id }} />);
  expect(await screen.findByRole("heading", { name: event.title })).toBeVisible();
  expect(screen.getByText(/التجمع يوم/)).toHaveTextContent("التكريم بعد المباراة الودية.");
  expect(screen.queryByText(announcement.title)).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "العودة إلى الفعاليات" })).toHaveAttribute("href", `/guardian/activities?kind=Event&page=3#activity-${event.id}`);
  expect(fetcher).toHaveBeenCalledWith(`/api/v1/news?kind=Event&page=3&item=${event.id}`, expect.objectContaining({ cache: "no-store" }));
});

it("cannot replace the active tab with a delayed response from the previous tab", async () => {
  let finish!: (value: Response) => void;
  const late = new Promise<Response>(resolve => { finish = resolve; });
  vi.stubGlobal("fetch", vi.fn(async (path: string) => path.includes("kind=News") ? late : feed([event])));
  const view = render(<AcademyActivities search={{ kind: "News" }} />);
  view.rerender(<AcademyActivities search={{ kind: "Event" }} />);
  await screen.findByRole("link", { name: /احتفال الموسم/ });
  await act(async () => finish(feed([announcement])));
  expect(screen.getByRole("link", { name: /احتفال الموسم/ })).toBeVisible();
  expect(screen.queryByText(announcement.title)).not.toBeInTheDocument();
});

it("retries a failed detail read and handles an item that is no longer available", async () => {
  const fetcher = vi.fn().mockRejectedValueOnce(new TypeError("Failed to fetch")).mockResolvedValueOnce(feed([announcement]));
  vi.stubGlobal("fetch", fetcher);
  render(<AcademyActivities search={{ kind: "Event", item: event.id }} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل الأنشطة.");
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  expect(await screen.findByText("هذا النشاط غير متاح.")).toBeVisible();
  expect(screen.queryByText(announcement.title)).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "العودة إلى الفعاليات" })).toBeVisible();
});

it("keeps the preview usable when the private cover image cannot load", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json([event])));
  render(<AcademyActivityPreview />);
  const card = await screen.findByRole("link", { name: /احتفال الموسم/ });
  const image = card.querySelector("img")!;
  fireEvent.error(image);
  expect(card.querySelector("img")).toBeNull();
  expect(card).toHaveAttribute("href", `/dashboard/activities?kind=Event&item=${event.id}`);
});
