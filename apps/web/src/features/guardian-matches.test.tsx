import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";
import { GuardianMatches } from "./guardian-matches";

vi.mock("../components/guardian-shell", () => ({ GuardianShell: ({ children }: { children: ReactNode }) => <main>{children}</main> }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

const match = { id: "46b93599-f407-4219-9c3a-4b757490e474", team: "فريق العرض", opponent: "المنافس الأول", tournament: "بطولة العرض", matchDate: "2026-11-06", startTime: "17:00", venue: "ملعب العرض", status: "Published", isUpcoming: false };
const playerId = "0cda5724-7fa6-465a-83c0-e18a0b6e31a3";

it("separates upcoming fixtures from history, including a cancelled future match", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json([
    match,
    { ...match, id: "future", opponent: "المنافس القادم", isUpcoming: true },
    { ...match, id: "cancelled", opponent: "المنافس الملغى", isUpcoming: true, status: "Cancelled" },
    { ...match, id: "played", opponent: "المنافس الذي انتهت مباراته", isUpcoming: true, status: "Completed", ourScore: 0, opponentScore: 0 },
  ])));
  render(<GuardianMatches />);
  const history = await screen.findByRole("region", { name: "سجل المباريات" });
  expect(within(history).getByText("بانتظار النتيجة")).toBeVisible();
  expect(within(history).getByText("ملغاة")).toBeVisible();
  expect(within(history).getByText("تعادل")).toBeVisible();
  expect(within(screen.getByRole("region", { name: "المباريات القادمة" })).getByText("المنافس القادم")).toBeVisible();
});

it("retries the exact scoped match and filters a legacy list response", async () => {
  const fetcher = vi.fn()
    .mockRejectedValueOnce(new TypeError("Failed to fetch"))
    .mockResolvedValueOnce(Response.json([match, { ...match, id: "other", opponent: "سجل آخر" }]));
  vi.stubGlobal("fetch", fetcher);
  render(<GuardianMatches playerId={playerId} matchId={match.id} />);
  await screen.findByRole("alert");
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  expect(await screen.findByText(match.opponent)).toBeVisible();
  expect(screen.queryByText("سجل آخر")).not.toBeInTheDocument();
  expect(fetcher).toHaveBeenLastCalledWith(`/api/v1/my-matches?playerId=${playerId}&matchId=${match.id}`, expect.objectContaining({ cache: "no-store" }));
  expect(screen.getByRole("link", { name: "العودة إلى المباريات" })).toHaveAttribute("href", `/guardian/matches?playerId=${playerId}`);
});
