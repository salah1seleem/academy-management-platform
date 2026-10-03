import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import PlayersPage from "../app/dashboard/players/page";
import { PlayerDetails } from "./player-record";
import { StructureList } from "./structure-list";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), back: vi.fn() }) }));
afterEach(cleanup);
const response = (data: unknown) => Promise.resolve(new Response(JSON.stringify(data), { status: 200 }));
const enrollment = { id: "e1", sportName: "كرة القدم", branchName: "فرع مدينة نصر", groupName: "براعم أ", categoryName: "تحت 10", status: "Active", subscription: { id: "sub1", plan: "شهري", status: "Expired", startDate: "2026-08-01", endDate: "2026-08-31" } };
const player = { id: "p1", arabicName: "عمر خالد", playerCode: "NG-001", dateOfBirth: "2016-04-01", isActive: true, preferredFoot: "Left", footballPosition: "وسط", asOfDate: "2026-09-28" };

describe("sports read-model presentation", () => {
  it("shows each enrollment context without issuing per-player queries", async () => {
    global.fetch = vi.fn(input => String(input).includes("structure/options") ? response({ branches: [], sports: [], categories: [], groups: [] }) : response([{ ...player, enrollments: 2, enrollmentSummaries: [enrollment, { ...enrollment, id: "e2", sportName: "السباحة", branchName: "فرع الشروق", groupName: "سباحة أ" }] }])) as typeof fetch;
    render(<PlayersPage />);
    expect(await screen.findByText("عمر خالد")).toBeVisible();
    expect(screen.getByText("فرع الشروق")).toBeVisible(); expect(screen.getByText("فرع مدينة نصر")).toBeVisible();
    expect(screen.getAllByText("منتهي")).toHaveLength(2);
    expect(vi.mocked(fetch).mock.calls).toHaveLength(2);
  });
  it("uses the server date, preferred foot and verified guardian relation in the profile", async () => {
    global.fetch = vi.fn(() => response({ ...player, enrollments: [enrollment], guardians: [{ id: "g1", displayName: "خالد محمود", relationshipType: "الأب" }] }));
    render(<PlayerDetails id="p1" />);
    expect(await screen.findByText("عمر خالد")).toBeVisible(); expect(screen.getByText("10 سنوات")).toBeVisible(); expect(screen.getByText("اليسرى")).toBeVisible();
    expect(screen.getByRole("link", { name: "عرض ولي الأمر" })).toHaveAttribute("href", "/dashboard/guardians/g1");
    expect(screen.getByRole("link", { name: "سجل الاشتراك" })).toHaveAttribute("href", "/dashboard/subscriptions/periods/sub1");
  });
  it("renders stored coach, schedule and player count on group cards", async () => {
    global.fetch = vi.fn(() => response([{ id: "g1", arabicName: "براعم أ", isActive: true, playerCount: 7, coaches: ["كريم حسن"], branchName: "فرع مدينة نصر", categoryName: "تحت 10", schedules: [{ dayOfWeek: "Monday", startTime: "18:00:00", endTime: "19:00:00" }] }]));
    render(<StructureList kind="groups" />);
    expect(await screen.findByText("براعم أ")).toBeVisible(); expect(screen.getByText("7 لاعب مسجل")).toBeVisible(); expect(screen.getByText(/المدرب: كريم حسن/)).toBeVisible(); expect(screen.getByText(/الاثنين · 18:00–19:00/)).toBeVisible();
  });
});
