import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import PlayersPage from "../app/dashboard/players/page";
import { PlayerDetails } from "./player-record";
import { StructureList } from "./structure-list";

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(), useRouter: () => ({ push: vi.fn(), back: vi.fn() }) }));
afterEach(cleanup);
const response = (data: unknown) => Promise.resolve(new Response(JSON.stringify(data), { status: 200 }));
const enrollment = { id: "e1", trainingGroupId: "group-1", sportName: "كرة القدم", branchName: "فرع مدينة نصر", groupName: "براعم أ", categoryName: "تحت 10", status: "Active", subscription: { id: "sub1", plan: "شهري", status: "Expired", startDate: "2026-08-01", endDate: "2026-08-31" } };
const player = { id: "p1", arabicName: "عمر خالد", playerCode: "NG-001", dateOfBirth: "2016-04-01", isActive: true, preferredFoot: "Left", footballPosition: "وسط", asOfDate: "2026-09-28" };

describe("sports read-model presentation", () => {
  it("shows each enrollment context without issuing per-player queries", async () => {
    global.fetch = vi.fn(input => String(input).includes("structure/options") ? response({ branches: [], sports: [], categories: [], groups: [] }) : response([{ ...player, enrollments: 2, enrollmentSummaries: [enrollment, { ...enrollment, id: "e2", sportName: "السباحة", branchName: "فرع الشروق", groupName: "سباحة أ" }] }])) as typeof fetch;
    render(<PlayersPage />);
    expect(await screen.findByText("عمر خالد")).toBeVisible();
    expect(screen.getByText("فرع الشروق")).toBeVisible(); expect(screen.getByText("فرع مدينة نصر")).toBeVisible();
    expect(screen.getAllByText("منتهي").filter(e => e.tagName !== "OPTION")).toHaveLength(2);
    expect(vi.mocked(fetch).mock.calls).toHaveLength(2);
  });
  it("uses the server date, preferred foot and verified guardian relation in the profile", async () => {
    global.fetch = vi.fn(input => {
      const url = String(input);
      if (url.includes("discount-rules/players")) return response([]);
      if (url.includes("discount-rules")) return response({ items: [] });
      if (url.includes("refunds")) return response({ collections: [], refunds: [] });
      if (url.includes("structure/options")) return response({ groups: [] });
      if (url.includes("subscriptions/periods")) return response({ id: "sub1", status: "Expired", planType: "Duration", plan: "شهري", player: "عمر خالد", playerCode: "NG-001", sportEnrollmentId: "e1", endDate: "2026-08-31", startDate: "2026-08-01", adjustments: [] });
      return response({ ...player, enrollments: [enrollment], guardians: [{ id: "g1", displayName: "خالد محمود", relationshipType: "الأب" }] });
    });
    render(<PlayerDetails id="p1" />);
    expect((await screen.findAllByText("عمر خالد"))[0]).toBeVisible(); expect(screen.getByText("10 سنوات")).toBeVisible(); expect(screen.getByText("اليسرى")).toBeVisible();
    expect(screen.getByRole("link", { name: "عرض ولي الأمر" })).toHaveAttribute("href", "/dashboard/guardians/g1");
    expect(screen.getByRole("link", { name: "سجل الاشتراك" })).toHaveAttribute("href", "/dashboard/subscriptions/periods/sub1");
    expect(screen.getAllByRole("link", { name: "تجديد الاشتراك" })[0]).toHaveAttribute("href", "/dashboard/subscriptions/renew/new?enrollmentId=e1");
    expect(screen.getByRole("link", { name: "تسجيل الحضور" })).toHaveAttribute("href", "/dashboard/attendance/sessions?groupId=group-1&playerId=p1");
    expect(screen.getByRole("link", { name: "تقييم جديد" })).toHaveAttribute("href", "/dashboard/evaluations/new?enrollmentId=e1");
    expect(screen.getByRole("link", { name: "سجل الحضور" })).toHaveAttribute("href", "/dashboard/attendance/history?playerId=p1");
  });
  it("renders stored coach, schedule and player count on group cards", async () => {
    global.fetch = vi.fn(() => response([{ id: "g1", arabicName: "براعم أ", isActive: true, playerCount: 7, coaches: ["كريم حسن"], branchName: "فرع مدينة نصر", categoryName: "تحت 10", schedules: [{ dayOfWeek: "Monday", startTime: "18:00:00", endTime: "19:00:00" }] }]));
    render(<StructureList kind="groups" />);
    expect(await screen.findByText("براعم أ")).toBeVisible(); expect(screen.getByText("7 لاعب مسجل")).toBeVisible(); expect(screen.getByText(/المدرب: كريم حسن/)).toBeVisible(); expect(screen.getByText(/الاثنين · 18:00–19:00/)).toBeVisible();
  });
});
