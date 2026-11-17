import { expect, test } from "@playwright/test";
import { ensurePlacement, placementEnrollment, placementRead, preparePlacementSessions, serviceIdentity } from "../support/placement";
test("Owner previews a dated transfer; Guardian sees the correct new schedule without losing history", async ({ page }, info) => {
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
  const login = await page.request.post("/api/v1/auth/login", {
    headers: { "X-CSRF-TOKEN": csrf.token },
    data: { email: "owner.nogoom@example.test", password: "Demo-Only-123!" },
  });
  expect(login.ok(), await login.text()).toBe(true);
  const base = "5a000000-0000-0000-0000-", id = (n: number) => base + String(n).padStart(12, "0");
  const money = await placementRead(page.request, "/api/v1/subscriptions/collections");
  const sessions = await preparePlacementSessions(page.request);
  await ensurePlacement(page.request, 10, 11);
  let detail = await page.request.get(`/api/v1/people/players/${id(10)}`).then(r => r.json());
  const services = serviceIdentity(detail);
  const headers = { "X-CSRF-TOKEN": (await page.request.get("/api/v1/auth/csrf").then(r => r.json())).token, "Idempotency-Key": crypto.randomUUID() };
  await page.goto(`/dashboard/players/${id(10)}?tab=training`); await page.getByText("تعديل المجموعة", { exact: true }).click();
  if (!placementEnrollment(detail, id(11)).transfers?.length) {
    await page.getByLabel("تعديل المجموعة", { exact: true }).selectOption(id(50)); await page.getByLabel("بدء النقل").fill(sessions.date); await page.getByRole("button", { name: "معاينة نقل المجموعة" }).click();
    const preview = page.getByRole("region", { name: "معاينة النقل" }); await expect(preview).toBeVisible(); await expect(preview.getByRole("button", { name: "تأكيد نقل المجموعة" })).toBeEnabled();
    await expect(preview.getByRole("heading", { name: "مواعيد تضاف إلى جدول اللاعب" })).toBeVisible();
    for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await preview.screenshot({ path: info.outputPath(`dated-transfer-${width}.png`), animations: "disabled" }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
    await preview.getByRole("button", { name: "تأكيد نقل المجموعة" }).click(); await expect(page.getByLabel("سجل نقل المجموعة")).toBeVisible();
  }
  detail = await page.request.get(`/api/v1/people/players/${id(10)}`).then(r => r.json()); expect(placementEnrollment(detail, id(11)).transfers).toContainEqual({ effectiveDate: sessions.date, groupName: "نقل السباحة — موعد مسائي تجريبي" });
  expect(serviceIdentity(detail)).toEqual(services);
  expect(await placementRead(page.request, "/api/v1/subscriptions/collections")).toEqual(money);
  await page.request.post("/api/v1/auth/logout", { headers }); const token = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
  const challenge = await page.request.post("/api/v1/auth/guardian/otp/request", { data: { phoneNumber: "01000000001" }, headers: { "X-CSRF-TOKEN": token.token } }); expect(challenge.ok()).toBe(true);
  const verified = await page.request.post("/api/v1/auth/guardian/otp/verify", { data: { phoneNumber: "01000000001", code: "246810", challengeId: (await challenge.json()).challengeId }, headers: { "X-CSRF-TOKEN": token.token } }); expect(verified.ok(), await verified.text()).toBe(true);
  await page.goto(`/guardian/children/${id(10)}`); await expect(page.getByLabel("نقل مجموعة مجدول")).toContainText("نقل السباحة — موعد مسائي تجريبي");
  const child = await page.request.get(`/api/v1/guardian/children/${id(10)}/profile`).then(r => r.json()); const enrollment = placementEnrollment(child, id(11)); expect(enrollment.upcoming?.[0].id).toBe(sessions.target.id); expect(enrollment.upcoming?.[0].startTime).toBe("17:00:00"); expect(enrollment.transfers).toHaveLength(1);
  await page.screenshot({ path: info.outputPath("family-dated-current-session.png"), fullPage: true, animations: "disabled" });
});
