import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { DateFilterDefaults } from "../components/date-filter-defaults";
import { ProfileMatches } from "./profile-matches";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const replace=vi.fn();
vi.mock("next/navigation",()=>({useSearchParams:()=>new URLSearchParams("tab=matches"),usePathname:()=>"/dashboard/players/player-1",useRouter:()=>({replace})}));
const match = { id: "match-1", team: "منتخب اصطناعي", opponent: "الفريق الآخر", tournament: "منافسة عرض", matchDate: "2026-10-12", startTime: "17:30:00", venue: "ملعب العرض", status: "Cancelled", ourScore: null, opponentScore: null };
test("month defaults and clear keep the player's fixed scope, status and deep match link", async () => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => new Response(JSON.stringify([match]))); vi.stubGlobal("fetch", fetcher);
  render(<DateFilterDefaults range={{ from: "2026-10-01", to: "2026-10-17" }}><ProfileMatches playerId="player-1" /></DateFilterDefaults>);
  expect(await screen.findByText("منتخب اصطناعي × الفريق الآخر")).toBeVisible();
  expect(fetcher.mock.calls[0][0]).toContain("playerId=player-1&from=2026-10-01&to=2026-10-17");
  expect(screen.getByRole("link", { name: "عرض المباراة" })).toHaveAttribute("href", "/dashboard/matches?matchId=match-1");
  fireEvent.click(screen.getByRole("button", { name: /مسح الفلاتر/ }));
  await waitFor(() => expect(fetcher.mock.calls.at(-1)?.[0]).toBe("/api/v1/manage/matches/?playerId=player-1"));
  expect(screen.getByLabelText("من تاريخ")).toHaveValue(""); expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("");
  expect(replace).toHaveBeenCalledWith("/dashboard/players/player-1?tab=matches&matchDates=custom", { scroll: false });
  fireEvent.change(screen.getByLabelText("الحالة"), { target: { value: "Published" } });
  expect(await screen.findByText("لا توجد مباريات تخص هذا الملف ضمن الفلاتر.")).toBeVisible();
});
test("coach scope is explicit; denial is not an empty success and retry reads again", async () => {
  const fetcher = vi.fn().mockResolvedValueOnce(new Response("", { status: 403 })).mockResolvedValue(new Response(JSON.stringify([match]))); vi.stubGlobal("fetch", fetcher);
  render(<ProfileMatches coachMembershipId="coach-1" />);
  expect(await screen.findByText("لا تملك صلاحية عرض المباريات في هذا النطاق.")).toBeVisible();
  expect(fetcher.mock.calls[0][0]).toContain("coachMembershipId=coach-1");
  expect(screen.getByText(/ليس سجلًا تاريخيًا/)).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  expect(await screen.findByText("منتخب اصطناعي × الفريق الآخر")).toBeVisible();
});
test("changing record does not show a late response from the previous player", async () => {
  let finish!: (r: Response) => void;
  vi.stubGlobal("fetch", vi.fn((url: string) => url.includes("player-1") ? new Promise<Response>(r => { finish = r; }) : Promise.resolve(new Response("[]"))));
  const view = render(<ProfileMatches playerId="player-1" />);
  view.rerender(<ProfileMatches playerId="player-2" />);
  expect(await screen.findByText("لا توجد مباريات تخص هذا الملف ضمن الفلاتر.")).toBeVisible();
  finish(new Response(JSON.stringify([match])));
  await waitFor(() => expect(screen.queryByText("منتخب اصطناعي × الفريق الآخر")).not.toBeInTheDocument());
});
