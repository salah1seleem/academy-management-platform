import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StructureDetails } from "./structure-record";

const nav = vi.hoisted(() => ({tab:"performance"}));
afterEach(() => { cleanup(); nav.tab="performance"; });

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(`tab=${nav.tab}`), usePathname: () => "/dashboard/academy/coaches/coach-1", useRouter: () => ({ replace: vi.fn() }) }));

it("opens the assigned group directly from coach overview without requiring the performance tab", async () => {
  nav.tab="overview";
  global.fetch=vi.fn(async()=>new Response(JSON.stringify({id:"coach-1",arabicName:"مدرب تجريبي",isActive:true,assignments:[{id:"assignment",trainingGroupId:"group-1",groupName:"مجموعة اصطناعية",isActive:true}]}))) as typeof fetch;
  render(<StructureDetails kind="coaches" id="coach-1"/>);
  expect(await screen.findByRole("link",{name:"مجموعة اصطناعية"})).toHaveAttribute("href","/dashboard/academy/groups/group-1");
});

it("shows stored coach performance and keeps the owner period on drill-down", async () => {
  const calls: string[] = [];
  global.fetch = vi.fn((input) => {
    const url = String(input); calls.push(url);
    if (url.includes("private-services")) return Promise.resolve(new Response("{}", { status: 403 }));
    const body = url.includes("coach-performance")
      ? { from: "2026-10-01", to: "2026-10-14", groups: [{ id: "group-1", name: "منتخب 2013", branch: "الرحاب", sessions: 4 }], sessionCount: 4, publishedEvaluations: 3, parentRating: 4.5, parentRatingCount: 2, complaintCount: 1 }
      : { id: "coach-1", arabicName: "مدرب تجريبي", isActive: true, assignments: [{ id: "assignment-1", groupName: "منتخب 2013", isActive: true }] };
    return Promise.resolve(new Response(JSON.stringify(body), { headers: { "Content-Type": "application/json" } }));
  }) as typeof fetch;
  render(<StructureDetails kind="coaches" id="coach-1" performanceFilters={{ from: "2026-10-01", to: "2026-10-14", branchId: "branch-1" }} />);
  expect(await screen.findByRole("heading", { name: "أداء المدرب خلال الفترة" })).toBeVisible();
  expect(screen.getByText("4.5/5 من 2 تقييم")).toBeVisible();
  expect(screen.getByRole("link", { name: "ملاحظات الأسر المرتبطة بالمدرب" })).toHaveAttribute("href", "/dashboard/feedback?coachId=coach-1");
  expect(calls.find(x => x.includes("coach-performance"))).toContain("branchId=branch-1");
});
