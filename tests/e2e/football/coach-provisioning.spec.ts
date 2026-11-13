import { expect, test } from "@playwright/test";

for (const width of [1440, 390]) test(`Football Demo and create coach ${width}`, async ({ page }, info) => {
  await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click(); await expect(page).toHaveURL(/dashboard/);
  const sports = await page.request.get("/api/v1/manage/structure/sports");
  expect((await sports.json()).map((s: { arabicName: string }) => s.arabicName)).toEqual(["كرة القدم"]);
  await page.goto("/dashboard/academy/coaches");
  await page.getByRole("link", { name: "إضافة مدرب", exact: true }).click();
  await expect(page.getByRole("heading", { name: "إضافة مدرب", exact: true })).toBeVisible();
  await expect(page.getByLabel("براعم 2018 — مجموعة أ")).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBeTruthy();
  const screenshot = info.outputPath(`create-coach-${width}.png`);
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled" }); await info.attach("Coach create form", { path: screenshot, contentType: "image/png" });
  const phone = `011${String(Date.now()).slice(-8)}`;
  const coachName = `مدرب رحلة عرض ${width}`;
  const groupName = "براعم 2018 — مجموعة أ";
  await page.getByLabel("الاسم بالعربية").fill(coachName);
  await page.getByLabel(groupName).check();
  const mobile = page.getByLabel("رقم الهاتف المصري");
  const save = page.getByRole("button", { name: "حفظ المدرب", exact: true });
  const coachPath = "/api/v1/manage/coaches";
  const csrf = await page.request.get("/api/v1/auth/csrf").then(response => response.json());
  let writes = 0;
  page.on("request", request => {
    if (request.method() === "POST" && new URL(request.url()).pathname === coachPath) writes++;
  });
  for (const invalid of [phone.slice(1), `${phone}1`, `013${phone.slice(3)}`, `+971${phone.slice(1)}`]) {
    await mobile.fill(invalid);
    await save.click();
    await expect(page.getByRole("alert").filter({ hasText: "اكتب رقم موبايل مصري صحيح." })).toBeVisible();
    await expect(mobile).toBeFocused();
    await expect(mobile).toHaveAttribute("aria-invalid", "true");
    expect(writes).toBe(0);
    const rejected = await page.request.post(coachPath, {
      headers: { "X-CSRF-TOKEN": csrf.token },
      data: { displayName: coachName, phoneNumber: invalid, isActive: true },
    });
    expect(rejected.status()).toBe(400);
  }
  await mobile.fill(width === 390 ? `+20 ${phone}` : phone);
  const created = page.waitForResponse(response => response.url().endsWith(coachPath) && response.request().method() === "POST");
  await save.click();
  const response = await created; expect(response.status()).toBe(201);
  expect(writes).toBe(1);
  await expect(page).toHaveURL(/\/academy\/coaches$/);
  const stored = await page.request.get("/api/v1/manage/structure/coaches");
  expect(stored.status()).toBe(200);
  const matches = (await stored.json() as { id: string; arabicName: string }[])
    .filter(coach => coach.arabicName === coachName);
  expect(matches).toHaveLength(1);
  const details = `/dashboard/academy/coaches/${matches[0].id}`;
  await page.getByLabel("بحث المدربين").fill(coachName);
  const profile = page.getByRole("link", { name: "عرض المجموعات", exact: true });
  await expect(profile).toHaveAttribute("href", details);
  await profile.click();
  await expect(page).toHaveURL(url => url.pathname === details);
  await expect(page.getByRole("heading", { name: coachName, exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: groupName, exact: true })).toBeVisible();

  await page.context().clearCookies();
  const challenge = await page.request.post("/api/v1/mobile/auth/otp/request", { data: { phoneNumber: phone } });
  expect(challenge.status()).toBe(202);
  const { challengeId } = await challenge.json();
  const verified = await page.request.post("/api/v1/mobile/auth/otp/verify", {
    data: { phoneNumber: phone, challengeId, code: "246810", deviceName: "Coach web flow E2E" },
  });
  expect(verified.status()).toBe(200);
  const credentials = await verified.json();
  const headers = { Authorization: `Bearer ${credentials.accessToken}` };
  const groups = await page.request.get("/api/v1/coach/groups", { headers });
  expect(groups.status()).toBe(200);
  expect((await groups.json() as { arabicName: string }[]).map(group => group.arabicName)).toEqual([groupName]);
  const denied = await page.request.get("/api/v1/reports/owner-summary", { headers });
  expect(denied.status()).toBe(403);
});
