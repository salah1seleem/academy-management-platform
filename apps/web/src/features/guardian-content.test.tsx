import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { GuardianGallery, GuardianMedical, GuardianSportCatalog, NutritionDetails, NutritionLibrary } from "./guardian-content";

vi.mock("next/navigation", () => ({ usePathname: () => "/guardian", useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }));
const ok = (value: unknown) => Promise.resolve(new Response(JSON.stringify(value), { status: 200 }));
beforeEach(() => { global.fetch = vi.fn(() => ok([])); });
afterEach(() => cleanup());

describe("Guardian Slice 7 content", () => {
  it("shows each linked sport catalog once with backend items", async () => { global.fetch = vi.fn(() => ok([{ sportId: "f", sport: "كرة القدم", items: [{ id: "1", arabicName: "كرة تدريب", imageReference: "/demo-assets/catalog-football.svg", displayPrice: 450, currency: "EGP" }] }, { sportId: "s", sport: "السباحة", items: [{ id: "2", arabicName: "نظارة سباحة", imageReference: "/demo-assets/catalog-swimming.svg" }] }])); render(<GuardianSportCatalog />); expect(await screen.findAllByRole("heading", { name: "كرة القدم" })).toHaveLength(1); expect(screen.getByText("كرة تدريب")).toBeVisible(); expect(screen.getByText("نظارة سباحة")).toBeVisible(); });
  it("catalog has no commerce action", async () => { global.fetch = vi.fn(() => ok([{ sportId: "f", sport: "كرة القدم", items: [{ id: "1", arabicName: "كرة تدريب", imageReference: "/demo-assets/catalog-football.svg" }] }])); render(<GuardianSportCatalog />); await screen.findByText("كرة تدريب"); expect(screen.queryByText(/شراء|أضف للسلة|اطلب الآن/)).not.toBeInTheDocument(); });
  it("nutrition has three Arabic tabs and two-column card grid", async () => { global.fetch = vi.fn(() => ok([{ id: "1", arabicName: "شكشوكة", arabicDescription: "وصف", imageReference: "/demo-assets/nutrition-meal.svg", category: "Breakfast", dataStatus: "DemoUnreviewed" }])); const { container } = render(<NutritionLibrary playerId="p" />); expect(screen.getByRole("tab", { name: "الإفطار" })).toBeVisible(); expect(screen.getByRole("tab", { name: "الغداء" })).toBeVisible(); expect(screen.getByRole("tab", { name: "العشاء" })).toBeVisible(); await screen.findByText("شكشوكة"); expect(container.querySelector(".nutrition-grid")).toBeInTheDocument(); });
  it("switches categories with the RTL arrow key, clears stale cards, and retries a failed category", async () => {
    let finishLunch: (value: Response) => void = () => {};
    let lunchReads = 0;
    const breakfast = { id: "1", arabicName: "شكشوكة", arabicDescription: "وصف", imageReference: "/demo-assets/nutrition-meal.svg", dataStatus: "Reviewed" };
    global.fetch = vi.fn((path: string) => {
      if (path.endsWith("category=Breakfast")) return ok([breakfast]);
      if (path.endsWith("category=Lunch")) return ++lunchReads === 1
        ? new Promise<Response>(resolve => { finishLunch = resolve; }) : ok([{ ...breakfast, id: "2", arabicName: "فتة" }]);
      return ok({});
    }) as typeof fetch;
    render(<NutritionLibrary playerId="p" />);
    await screen.findByRole("heading", { name: "شكشوكة" });
    const breakfastTab = screen.getByRole("tab", { name: "الإفطار" });
    breakfastTab.focus();
    fireEvent.keyDown(breakfastTab, { key: "ArrowLeft" });
    const lunchTab = screen.getByRole("tab", { name: "الغداء" });
    expect(lunchTab).toHaveFocus();
    const panel = screen.getByRole("tabpanel", { name: "الغداء" });
    expect(within(panel).queryByText("شكشوكة")).not.toBeInTheDocument();
    expect(within(panel).getByRole("status")).toBeVisible();
    finishLunch(new Response(null, { status: 500 }));
    expect(await within(panel).findByRole("alert")).toHaveTextContent("تعذر تحميل هذا القسم");
    fireEvent.click(within(panel).getByRole("button", { name: "إعادة تحميل القسم" }));
    expect(await within(panel).findByRole("heading", { name: "فتة" })).toBeVisible();
    expect(within(panel).queryByRole("alert")).not.toBeInTheDocument();
  });
  it("meal details show four facts serving and review label", async () => { global.fetch = vi.fn(() => ok({ id: "1", arabicName: "شكشوكة", arabicDescription: "وصف معلوماتي", imageReference: "/demo-assets/nutrition-meal.svg", servingDescription: "حصة واحدة", calories: 275, proteinGrams: 26, carbohydratesGrams: 36, fatGrams: 16, dataStatus: "DemoUnreviewed", sourceDescription: "غير مراجعة" })); render(<NutritionDetails id="1" playerId="p" />); expect(await screen.findByRole("heading", { name: "شكشوكة" })).toBeVisible(); for (const label of ["السعرات الحرارية", "البروتين", "الكربوهيدرات", "الدهون"]) expect(screen.getByText(label)).toBeVisible(); expect(screen.getByText("بيانات تجريبية غير مراجعة")).toBeVisible(); expect(screen.getByText(/الحصة: حصة واحدة/)).toBeVisible(); });
  it("keeps the requested nutrition category and date in meal links", async () => {
    global.fetch = vi.fn(path => String(path).endsWith("category=Lunch")
      ? ok([{ id: "m1", arabicName: "وجبة الغداء", arabicDescription: "وصف", imageReference: "/demo-assets/nutrition-meal.svg", category: "Lunch", dataStatus: "Reviewed" }])
      : ok({}));
    render(<NutritionLibrary playerId="p" day="2026-09-28" initialCategory="Lunch" />);
    expect(screen.getByRole("tab", { name: "الغداء" })).toHaveAttribute("aria-selected", "true");
    const meal = await screen.findByRole("link", { name: /وجبة الغداء/ });
    expect(meal).toHaveAttribute("href", "/guardian/nutrition/m1?category=Lunch&day=2026-09-28&player=p");
  });
  it("retains the return path and retries when meal detail loading fails", async () => {
    let reads = 0;
    global.fetch = vi.fn(path => {
      if (!String(path).endsWith("/nutrition/m1")) return ok({});
      if (++reads === 1) return Promise.resolve(new Response(null, { status: 503 }));
      return ok({ id: "m1", arabicName: "وجبة الغداء", imageReference: "/demo-assets/nutrition-meal.svg", dataStatus: "Reviewed" });
    });
    render(<NutritionDetails id="m1" playerId="p" category="Lunch" day="2026-09-28" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل تفاصيل الوجبة.");
    expect(screen.getByRole("link", { name: "العودة للمكتبة" })).toHaveAttribute("href", "/guardian/children/p/nutrition?category=Lunch&day=2026-09-28");
    fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
    expect(await screen.findByRole("heading", { name: "وجبة الغداء" })).toBeVisible();
  });
  it("nutrition UI has no price rating quantity cart order or checkout", async () => { global.fetch = vi.fn(() => ok({ id: "1", arabicName: "شكشوكة", arabicDescription: "وصف", imageReference: "/demo-assets/nutrition-meal.svg", servingDescription: "حصة", dataStatus: "DemoUnreviewed" })); const { container } = render(<NutritionDetails id="1" />); await screen.findByText("شكشوكة"); const details = within(container.querySelector(".meal-details")!); for (const label of ["السعر", "التقييم", "الكمية", "السلة", "الطلب", "الدفع"]) expect(details.queryByText(label, { exact: true })).not.toBeInTheDocument(); });
  it("medical view exposes published content without internal notes", async () => { global.fetch = vi.fn(() => ok([{ id: "m", type: "Consultation", arabicTitle: "متابعة إجهاد بسيط", recordDate: "2026-09-20", arabicDescription: "سجل اصطناعي", status: "Resolved", guardianVisibleNotes: "تمت المتابعة" }])); render(<GuardianMedical playerId="p" />); const card = (await screen.findByText("متابعة إجهاد بسيط")).closest("article")!; expect(within(card).getByText("استشارة")).toBeVisible(); expect(within(card).getByText("تمت المتابعة")).toBeVisible(); expect(within(card).queryByText(/داخلي/)).not.toBeInTheDocument(); });
  it("medical empty state is honest", async () => { render(<GuardianMedical playerId="p" />); expect(await screen.findByText("لا توجد إصابات أو استشارات منشورة")).toBeVisible(); });
  it("gallery displays published project media", async () => { global.fetch = vi.fn((path) => String(path).includes('group-media-consent')?ok({player:'طفل اصطناعي',allowed:false,revision:0,notice:'موافقة مستقلة'}):String(path).includes('/assets/gallery')?ok([]):ok([{ id: "g", type: "Image", mediaReference: "/demo-assets/gallery-training.svg", arabicCaption: "لحظة من التدريب" }])); render(<GuardianGallery playerId="p" />); expect(await screen.findByText("لحظة من التدريب")).toBeVisible(); expect(screen.getByRole("img", { name: "لحظة من التدريب" })).toHaveAttribute("src", expect.stringContaining("gallery-training.svg")); });
});
