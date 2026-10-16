import { expect, test } from "@playwright/test";
import { mobileDemoToken } from "../support/mobile-demo-session";

test("Player and coach file matches retain context, clear all dates, open the selected match and share cancellation with guardian", async ({ page }, info) => {
  test.setTimeout(90_000); // Shared Demo OTP helper may wait 31s on a real 429.
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const options = await (await page.request.get("/api/v1/manage/matches/options")).json();
  const team = options.teams.find((t: { name: string }) => t.name.includes("منتخب 2018")); expect(team).toBeTruthy();
  const roster = await (await page.request.get(`/api/v1/manage/matches/teams/${team.id}/roster`)).json();
  const guardian = await mobileDemoToken(page.request, "+201099900100", "ملف مباريات اصطناعي");
  const headers = { Authorization: `Bearer ${guardian}` };
  const home = await (await page.request.get("/api/v1/guardian/home", { headers })).json();
  const player = roster.items.find((p: { playerId: string }) => home.children.some((c: { id: string }) => c.id === p.playerId)); expect(player).toBeTruthy();
  const tournament = options.tournaments.find((t: { sportId: string }) => t.sportId === team.sportId);
  const csrf = (await (await page.request.get("/api/v1/auth/csrf")).json()).token;
  const opponent = `منافس ملف اصطناعي ${Date.now()}`;
  const body = { tournamentId: tournament.id, teamId: team.id, opponent, matchDate: "2026-10-25", startTime: "17:30:00", venue: "ملعب اختبار الملف", notes: null, ourScore: null, opponentScore: null, benchPlayerIds: [player.playerId], publish: true, isActive: true, rosterQuoteHash: roster.quoteHash };
  const created = await page.request.post("/api/v1/manage/matches/", { headers: { "X-CSRF-TOKEN": csrf }, data: body }); expect(created.status()).toBe(200); const id = (await created.json()).id;
  await page.goto(`/dashboard/players/${player.playerId}?tab=matches`);
  await expect(page.getByRole("tab", { name: "المباريات", exact: true })).toHaveAttribute("aria-selected", "true");
  const panel = page.getByRole("region", { name: "مباريات الملف" });
  await panel.getByRole("button", { name: /مسح الفلاتر/ }).click();
  await expect(panel.getByLabel("من تاريخ")).toHaveValue(""); await expect(panel.getByLabel("إلى تاريخ")).toHaveValue("");
  const selected = panel.locator("article").filter({ hasText: opponent }); await expect(selected).toHaveCount(1);
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 1000 }); await selected.scrollIntoViewIfNeeded(); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`profile-player-matches-${width}.png`), animations: "disabled" }); }
  await selected.getByRole("link", { name: "عرض المباراة" }).click();
  const editor = page.getByRole("form", { name: "محرر المباراة" }); await expect(editor.getByLabel("الفريق المنافس")).toHaveValue(opponent); await expect(page).toHaveURL(new RegExp(`matchId=${id}`));
  await page.goBack(); await expect(page.getByRole("tab", { name: "المباريات", exact: true })).toHaveAttribute("aria-selected", "true");
  await expect(page.getByLabel("من تاريخ")).toHaveValue(""); await expect(page.getByLabel("إلى تاريخ")).toHaveValue(""); await expect(page.locator("article").filter({hasText:opponent})).toHaveCount(1);
  await page.goto(`/dashboard/academy/coaches/${team.coachMembershipId}?tab=matches`);
  await expect(page.getByText(/ليس سجلًا تاريخيًا لإسناد المدربين/)).toBeVisible();
  await page.getByRole("button", { name: /مسح الفلاتر/ }).click(); await expect(page.locator("article").filter({ hasText: opponent })).toHaveCount(1);
  await page.setViewportSize({ width: 390, height: 1000 }); await page.getByRole("heading", { name: "المباريات والمنافسات" }).scrollIntoViewIfNeeded(); await page.screenshot({ path: info.outputPath("profile-coach-matches-390.png"), animations: "disabled" });
  const own = await page.request.get(`/api/v1/my-matches?playerId=${player.playerId}`, { headers }); expect(own.status()).toBe(200); expect((await own.json()).some((m: { id: string }) => m.id === id)).toBe(true);
  const before = await (await page.request.get("/api/v1/reports/owner-summary")).json();
  const cancelled = await page.request.put(`/api/v1/manage/matches/${id}`, { headers: { "X-CSRF-TOKEN": csrf }, data: { ...body, isActive: false } }); expect(cancelled.status()).toBe(200);
  const feed = await (await page.request.get(`/api/v1/my-matches?playerId=${player.playerId}`, { headers })).json(); const final = feed.find((m: { id: string }) => m.id === id); expect(final.status).toBe("Cancelled"); expect(final.isUpcoming).toBe(false); expect(final.participation).toBe("Bench");
  const after = await (await page.request.get("/api/v1/reports/owner-summary")).json(); expect(before.collections).toEqual(expect.any(Object)); expect(before.paymentRequests).toEqual(expect.any(Object)); expect(after.collections).toEqual(before.collections); expect(after.paymentRequests).toEqual(before.paymentRequests);
});
