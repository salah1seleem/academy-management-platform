import { expect, test } from "@playwright/test";
test("Owner previews a dated transfer; Guardian sees the correct new schedule without losing history", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const base = "5a000000-0000-0000-0000-", id = (n: number) => base + String(n).padStart(12, "0");
  let detail = await page.request.get(`/api/v1/people/players/${id(10)}`).then(r => r.json());
  const headers = { "X-CSRF-TOKEN": (await page.request.get("/api/v1/auth/csrf").then(r => r.json())).token, "Idempotency-Key": crypto.randomUUID() };
  if (!detail.enrollments[0].trainingGroupId) {
    const preview = await page.request.post(`/api/v1/manage/groups/${id(1)}/placement/preview`, { headers, data: { enrollmentIds: [id(11)] } }); expect(preview.ok(), await preview.text()).toBe(true);
    const place = await page.request.post(`/api/v1/manage/groups/${id(1)}/placement`, { headers, data: { enrollmentIds: [id(11)], fingerprint: (await preview.json()).fingerprint } }); expect(place.ok(), await place.text()).toBe(true);
  }
  await page.goto(`/dashboard/players/${id(10)}?tab=training`); await page.getByText("تعديل المجموعة", { exact: true }).click();
  if (!detail.enrollments[0].transfers?.length) {
    await page.getByLabel("تعديل المجموعة", { exact: true }).selectOption(id(50)); await page.getByLabel("بدء النقل").fill("2026-11-15"); await page.getByRole("button", { name: "معاينة نقل المجموعة" }).click();
    const preview = page.getByRole("region", { name: "معاينة النقل" }); await expect(preview).toBeVisible(); await expect(preview.getByRole("button", { name: "تأكيد نقل المجموعة" })).toBeEnabled();
    for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await preview.screenshot({ path: info.outputPath(`dated-transfer-${width}.png`), animations: "disabled" }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
    await preview.getByRole("button", { name: "تأكيد نقل المجموعة" }).click(); await expect(page.getByLabel("سجل نقل المجموعة")).toBeVisible();
  }
  detail = await page.request.get(`/api/v1/people/players/${id(10)}`).then(r => r.json()); expect(detail.enrollments[0].transfers).toContainEqual({ effectiveDate: "2026-11-15", groupName: "نقل السباحة — موعد مسائي تجريبي" });
  await page.request.post("/api/v1/auth/logout", { headers }); const token = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
  const challenge = await page.request.post("/api/v1/auth/guardian/otp/request", { data: { phoneNumber: "01000000001" }, headers: { "X-CSRF-TOKEN": token.token } }); expect(challenge.ok()).toBe(true);
  const verified = await page.request.post("/api/v1/auth/guardian/otp/verify", { data: { phoneNumber: "01000000001", code: "246810", challengeId: (await challenge.json()).challengeId }, headers: { "X-CSRF-TOKEN": token.token } }); expect(verified.ok(), await verified.text()).toBe(true);
  await page.goto(`/guardian/children/${id(10)}`); await expect(page.getByLabel("نقل مجموعة مجدول")).toContainText("نقل السباحة — موعد مسائي تجريبي");
  const child = await page.request.get(`/api/v1/guardian/children/${id(10)}/profile`).then(r => r.json()); expect(child.enrollments[0].upcoming[0].id).toBe(id(51)); expect(child.enrollments[0].upcoming[0].startTime).toBe("17:00:00"); expect(child.enrollments[0].transfers).toHaveLength(1);
});
