import { expect, test } from "@playwright/test";
test("Owner reviews an eligible bulk placement and unpaid child stays waiting; Guardian receives the new session", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const group = "5a000000-0000-0000-0000-000000000001", player = "5a000000-0000-0000-0000-000000000010", unpaid = "5a000000-0000-0000-0000-000000000030";
  // Exercise the normal edit path for these exact synthetic fixtures from the first seed run.
  // No reset: preserve placement, money, household links and all other records.
  for (const n of [10, 20, 30]) {
    const id = `5a000000-0000-0000-0000-${String(n).padStart(12, "0")}`, row = await page.request.get(`/api/v1/people/players/${id}`).then(r => r.json()); expect(row.playerCode).toBe(`DEMO-BULK-${n}`);
    if (row.dateOfBirth === "2018-01-01") { const token = await page.request.get("/api/v1/auth/csrf").then(r => r.json()); const edit = await page.request.put(`/api/v1/people/players/${id}`, { data: { ...row, dateOfBirth: "2017-01-01" }, headers: { "X-CSRF-TOKEN": token.token } }); expect(edit.ok(), await edit.text()).toBe(true); }
  }
  const detail = await page.request.get(`/api/v1/people/players/${player}`).then(r => r.json());
  await page.goto(`/dashboard/academy/groups/${group}?tab=players`); const region = page.getByRole("region", { name: "التسكين الجماعي" }); await expect(region).toBeVisible();
  const unpaidCard = region.getByRole("article").filter({ has: page.getByRole("checkbox", { name: /لاعب انتظار غير مدفوع/ }) });
  await expect(unpaidCard.getByRole("checkbox")).toBeDisabled(); await expect(unpaidCard.getByText(/لا يوجد استحقاق تدريب سار/)).toBeVisible();
  if (!detail.enrollments[0].trainingGroupId) {
    await region.getByRole("checkbox", { name: /لاعب تسكين مستوفٍ تجريبي 10/ }).check(); await region.getByRole("button", { name: "معاينة التسكين" }).click(); await expect(region.getByRole("region", { name: "معاينة التسكين" })).toBeVisible(); await expect(region.getByRole("button", { name: "تأكيد تسكين الدفعة" })).toBeEnabled();
    for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await region.screenshot({ path: info.outputPath(`placement-${width}.png`), animations: "disabled" }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
    await region.getByRole("button", { name: "تأكيد تسكين الدفعة" }).click(); await expect(region.getByText(/تم تسكين 1 لاعب بنجاح/)).toBeVisible();
  }
  const placed = await page.request.get(`/api/v1/people/players/${player}`).then(r => r.json()); expect(placed.enrollments[0].trainingGroupId).toBe(group); const waiting = await page.request.get(`/api/v1/people/players/${unpaid}`).then(r => r.json()); expect(waiting.enrollments[0].trainingGroupId).toBeNull();
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json()); await page.request.post("/api/v1/auth/logout", { headers: { "X-CSRF-TOKEN": csrf.token } });
  const token = await page.request.get("/api/v1/auth/csrf").then(r => r.json()); const challenge = await page.request.post("/api/v1/auth/guardian/otp/request", { data: { phoneNumber: "01000000001" }, headers: { "X-CSRF-TOKEN": token.token } }); expect(challenge.ok()).toBe(true); const challengeId = (await challenge.json()).challengeId;
  const activated = await page.request.post("/api/v1/auth/guardian/otp/verify", { data: { phoneNumber: "01000000001", code: "246810", challengeId }, headers: { "X-CSRF-TOKEN": token.token } }); expect(activated.ok(), await activated.text()).toBe(true);
  const child = await page.request.get(`/api/v1/guardian/children/${player}/profile`); expect(child.ok()).toBe(true); const profile = await child.json(); expect(JSON.stringify(profile)).toContain("2026-11-15");
});
