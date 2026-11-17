import { expect, test } from "@playwright/test";
import { assertPlacementViewport, ensurePlacement, placementRead, preparePlacementSessions, serviceIdentity, type PlacementPlayer } from "../support/placement";
test("Guardian and Owner distinguish parallel registration formats without duplicate player or unintended collection", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "تسجيل الدخول" }).click(); await page.waitForURL("**/dashboard");
  const player = "5a000000-0000-0000-0000-000000000020"; const privateId = "5a000000-0000-0000-0000-000000000041";
  const groupId = "5a000000-0000-0000-0000-000000000001";
  const money = await placementRead(page.request, "/api/v1/subscriptions/collections");
  await preparePlacementSessions(page.request);
  await ensurePlacement(page.request, 20, 21);
  await page.goto(`/dashboard/players/${player}?tab=subscriptions`); await expect(page.getByRole("link", { name: "تسجيل الاشتراك", exact: true })).toHaveAttribute("href", `/dashboard/subscriptions/renew/new?enrollmentId=${privateId}`);
  const offer = await page.request.get(`/api/v1/subscriptions/admin-renewals/enrollments/${privateId}`).then(r => r.json()); expect(offer.plans).toHaveLength(1); expect(offer.plans[0].trainingFormat).toBe("Private");
  const detail = await page.request.get(`/api/v1/people/players/${player}`).then(r => r.json()); expect(detail.enrollments.map((e: { registrationFormat: string }) => e.registrationFormat).sort()).toEqual(["Group", "Private"]); expect(detail.enrollments.find((e: { id: string }) => e.id === privateId).subscription).toBeNull();
  const services = serviceIdentity(detail);
  await page.goto(`/dashboard/players/${player}?tab=training`); const track = page.locator("section.detail-card").filter({ has: page.locator(".team-chip", { hasText: /^خاص$/ }) }); await track.getByText("اقتراح مجموعات مناسبة", { exact: true }).click(); await track.getByRole("button", { name: "عرض البدائل المؤهلة" }).click(); await expect(track.getByText(/لا توجد مجموعة تستوفي/)).toBeVisible();
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await track.screenshot({ path: info.outputPath(`registration-formats-${width}.png`), animations: "disabled" }); await assertPlacementViewport(page, width); }
  const root = `/api/v1/manage/groups/${groupId}`;
  const original = await placementRead<{ group: { registrationFormat: string } }>(page.request, `${root}/details`);
  try {
    await page.goto(`/dashboard/academy/groups/${groupId}?tab=players`); await page.getByText("نمط التدريب · جماعي", { exact: true }).click(); await page.getByLabel("نمط المجموعة").selectOption("Private"); await page.getByRole("checkbox", { name: "راجعت السعة المناسبة لهذا النمط" }).check();
    const rejected = page.waitForResponse(response => response.url().endsWith(`${root}/training-format`) && response.request().method() === "PUT");
    await page.getByRole("button", { name: "اعتماد نمط التدريب" }).click();
    expect((await rejected).status()).toBe(409);
    await expect(page.locator("p[role=alert]")).toContainText("لا تغير نمط مجموعة لها أعضاء");
    await page.screenshot({ path: info.outputPath("group-format-rejected.png"), fullPage: true, animations: "disabled" });
  } finally {
    const current = await placementRead<{ group: { registrationFormat: string } }>(page.request, `${root}/details`);
    if (current.group.registrationFormat !== original.group.registrationFormat) {
      const token = await placementRead<{ token: string }>(page.request, "/api/v1/auth/csrf");
      const restored = await page.request.put(`${root}/training-format`, { headers: { "X-CSRF-TOKEN": token.token }, data: { registrationFormat: 1, capacityReviewed: true } });
      expect(restored.ok(), await restored.text()).toBe(true);
    }
  }
  expect((await placementRead<{ group: { registrationFormat: string } }>(page.request, `${root}/details`)).group.registrationFormat).toBe("Group");
  expect(serviceIdentity(await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${player}`))).toEqual(services);
  expect(await placementRead(page.request, "/api/v1/subscriptions/collections")).toEqual(money);
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json()); await page.request.post("/api/v1/auth/logout", { headers: { "X-CSRF-TOKEN": csrf.token } });
  const token = await page.request.get("/api/v1/auth/csrf").then(r => r.json()); const challenge = await page.request.post("/api/v1/auth/guardian/otp/request", { data: { phoneNumber: "01000000001" }, headers: { "X-CSRF-TOKEN": token.token } }).then(r => r.json()); const activated = await page.request.post("/api/v1/auth/guardian/otp/verify", { data: { phoneNumber: "01000000001", code: "246810", challengeId: challenge.challengeId }, headers: { "X-CSRF-TOKEN": token.token } }); expect(activated.ok()).toBe(true);
  const child = await page.request.get(`/api/v1/guardian/children/${player}/profile`).then(r => r.json()); expect(child.enrollments.map((e: { registrationFormat: string }) => e.registrationFormat).sort()).toEqual(["Group", "Private"]);
});
