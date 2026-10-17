import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { HelpCenter, HelpLesson } from "./help-center";
import { HelpEntry } from "../components/help-entry";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const lesson: HelpLesson = { id: "family-renewal", section: "subscriptions", title: { ar: "تجديد اشتراك أبنائك", en: "Renew your children" }, summary: { ar: "راجع الإجمالي قبل رفع الإيصال.", en: "Review total." }, contentKind: "WrittenGuide", videoStatus: "AwaitingUpload", videoUrl: null, version: 1, steps: [{ ar: "راجع الباقة والأبناء.", en: "Review package and children." }, { ar: "انتظر مراجعة الطلب.", en: "Wait for review." }] };
it("uses authorized catalog and a guardian detail route, not a fake watch action", async () => {
  global.fetch = vi.fn(async () => Response.json({ items: [lesson] })) as typeof fetch;
  render(<HelpCenter guardian />);
  expect(await screen.findByRole("link", { name: /تجديد اشتراك أبنائك/ })).toHaveAttribute("href", "/guardian/help/family-renewal");
  expect(screen.getByText(/الفيديوهات بانتظار رفع المحتوى/)).toBeVisible();
  expect(screen.queryByRole("button", { name: /مشاهدة/ })).not.toBeInTheDocument();
});
it("sends search and section to the server, clears both, and shows honest empty results", async () => {
  global.fetch = vi.fn(async () => Response.json({ items: [] })) as typeof fetch; render(<HelpCenter />);
  await screen.findByText(/لا توجد أدلة ضمن/);
  fireEvent.change(screen.getByLabelText("البحث في الأدلة"), { target: { value: "الحضور" } });
  fireEvent.change(screen.getByLabelText("القسم"), { target: { value: "operations" } });
  await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([p]) => new URL(String(p), "http://localhost").searchParams.get("search") === "الحضور" && String(p).includes("section=operations"))).toBe(true));
  fireEvent.click(screen.getByRole("button", { name: "مسح" }));
  expect(screen.getByLabelText("القسم")).toHaveValue(""); expect(screen.getByLabelText("البحث في الأدلة")).toHaveValue("");
  await waitFor(() => expect(String(vi.mocked(fetch).mock.lastCall?.[0])).toContain("search=&section="));
});
it("renders actual written steps and does not invent a published video", async () => {
  global.fetch = vi.fn(async () => Response.json(lesson)) as typeof fetch; render(<HelpCenter guardian id={lesson.id} />);
  expect(await screen.findByRole("list")).toHaveTextContent("راجع الباقة والأبناء.");
  expect(screen.getByText(/ليس فيديو منشورًا/)).toBeVisible();
  expect(screen.getByRole("link", { name: "كل الأدلة" })).toHaveAttribute("href", "/guardian/help");
});
it("conceals withdrawn details and removes stale content on focus refresh", async () => {
  global.fetch = vi.fn().mockResolvedValueOnce(Response.json(lesson)).mockResolvedValue(Response.json({}, { status: 404 })); render(<HelpCenter id={lesson.id} />);
  await screen.findByRole("list"); fireEvent(window, new Event("focus"));
  expect(await screen.findByRole("alert")).toHaveTextContent("غير متاح"); expect(screen.queryByRole("list")).not.toBeInTheDocument();
});
it("late aborted search response cannot replace the latest catalog", async () => {
  let complete: (r: Response) => void = () => {};
  global.fetch = vi.fn().mockImplementationOnce(() => new Promise<Response>(r => { complete = r; })).mockResolvedValue(Response.json({ items: [lesson] }));
  render(<HelpCenter />); fireEvent.click(screen.getByRole("button", { name: "مسح" }));
  await screen.findByRole("link", { name: /تجديد اشتراك أبنائك/ }); complete(Response.json({ items: [] }));
  await waitFor(() => expect(screen.queryByText(/لا توجد أدلة ضمن/)).not.toBeInTheDocument());
});
it("pending content is not presented as executable steps", async () => {
  global.fetch = vi.fn(async () => Response.json({ ...lesson, contentKind: "AwaitingContent", steps: [] })) as typeof fetch; render(<HelpCenter id={lesson.id} />);
  expect(await screen.findByText(/بانتظار استكمال المحتوى؛/)).toHaveAttribute("role", "status"); expect(screen.queryByRole("list")).not.toBeInTheDocument();
});
it("help entry respects availability and does not self-enable a premium license", async () => {
  const closeDrawer = vi.fn();
  global.fetch = vi.fn().mockResolvedValueOnce(Response.json({ available: false })).mockResolvedValue(Response.json({ available: true })); render(<HelpEntry onNavigate={closeDrawer} />);
  await waitFor(() => expect(fetch).toHaveBeenCalledOnce()); expect(screen.queryByRole("link")).not.toBeInTheDocument();
  fireEvent(window, new Event("focus")); expect(await screen.findByRole("link", { name: "دليل الاستخدام" })).toHaveAttribute("href", "/dashboard/help");
  fireEvent.click(screen.getByRole("link", { name: "دليل الاستخدام" })); expect(closeDrawer).toHaveBeenCalledOnce();
});
