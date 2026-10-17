import { expect, test, type APIRequestContext } from "@playwright/test";
import { resolve } from "node:path";
import { guardianLogin } from "../support/auth";

test("Platform uploads, previews, publishes and archives real private video; Guardian plays only the live permitted revision", async ({ page, browser }, info) => {
  test.setTimeout(120_000);
  const database = /(?:^|;)Database=([^;]+)/i.exec(process.env.ConnectionStrings__Default ?? "")?.[1];
  expect(database?.startsWith("academy_v3_help_video_") || process.env.CI === "true" && database === "academy_e2e").toBe(true);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/platform/login"); await page.getByLabel("البريد الإلكتروني").fill("platform.demo@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByLabel("رمز التحقق").fill("246810"); await page.getByRole("button", { name: "دخول المنصة", exact: true }).click(); await page.waitForURL("**/platform");
  await page.getByRole("link", { name: "مكتبة شروحات الفيديو" }).click();
  const baseURL = new URL(page.url()).origin;
  const ownerContext = await browser.newContext({ baseURL }), ownerPage = await ownerContext.newPage(), owner = ownerPage.request;
  const guardianContext = await browser.newContext({ baseURL, viewport: { width: 390, height: 844 } }), guardian = await guardianContext.newPage();
  await ownerPage.goto("/login"); await ownerPage.getByLabel("البريد الإلكتروني").fill("owner.football@example.test"); await ownerPage.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await ownerPage.getByRole("button", { name: "دخول آمن", exact: true }).click(); await ownerPage.waitForURL("**/dashboard");
  const academy = "30000000-0000-0000-0000-000000000003", platform = page.request;
  async function write(c: APIRequestContext, path: string, data: unknown, elevated = false, put = false) {
    const csrf = await (await c.get(elevated ? "/api/v1/platform/auth/csrf" : "/api/v1/auth/csrf")).json();
    const r = await c.fetch(path, { method: put ? "PUT" : "POST", data, headers: { "X-CSRF-TOKEN": csrf.token } }); expect(r.ok(), `${path}: ${r.status()}`).toBe(true); return r;
  }
  const licenses = await (await platform.get(`/api/v1/platform/academies/${academy}/entitlements`)).json();
  const features = Object.fromEntries(licenses.map((x: { feature: string; enabled: boolean }) => [x.feature, x.enabled]));
  const original = (await (await owner.get("/api/v1/manage/configuration/")).json()).versions.find((x: { state: string }) => x.state === "Active").settings;
  const enabled = { ...original, helpAudiences: {}, features: { ...original.features, learning_hub: true } };
  async function activate(settings: unknown) {
    const d = await (await write(owner, "/api/v1/manage/configuration/drafts", settings)).json();
    for (const target of ["Validated", "Staged", "Active"]) {
      const current = (await (await owner.get("/api/v1/manage/configuration/")).json()).versions.find((x: { id: string }) => x.id === d.id);
      await write(owner, `/api/v1/manage/configuration/${d.id}/transition`, { target, version: current.version });
    }
  }
  const report = async () => (await owner.get("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28")).json(); const before = await report();
  let currentId: string | null = null;
  async function createAndUpload() {
    await page.getByLabel("الدليل", { exact: true }).selectOption("family-renewal");
    const existing = (await (await platform.get("/api/v1/platform/help-videos")).json()).revisions.filter((x: { lessonId: string; state: string }) => x.lessonId === "family-renewal" && x.state === "Draft");
    for (const row of existing) {
      expect(row.title).toBe("شرح اصطناعي — تجديد الأسرة");
      await page.locator(".help-revisions button").filter({ hasText: `إصدار ${row.revision}` }).click();
      await transition("أرشفة الإصدار"); await expect(page.getByRole("status")).toContainText("تمت الأرشفة");
    }
    await page.getByRole("button", { name: "مسودة إصدار جديد", exact: true }).click();
    await page.getByLabel("عنوان الفيديو", { exact: true }).fill("شرح اصطناعي — تجديد الأسرة");
    await page.getByLabel("الوصف", { exact: true }).fill("ألوان اختبار للتحقق من التشغيل، وليست درسًا إنتاجيًا أو تصويرًا لأطفال.");
    await page.getByLabel("بديل نصي مختصر للمحتوى").fill("راجع الباقة والأبناء والإجمالي وارفع الإيصال، ثم انتظر مراجعة الإدارة.");
    const saved = page.waitForResponse(r => r.url().endsWith("/api/v1/platform/help-videos") && r.request().method() === "POST");
    await page.getByRole("button", { name: "حفظ المسودة", exact: true }).click(); const response = await saved; expect(response.ok()).toBe(true); currentId = (await response.json()).id;
    await expect(page.getByRole("button", { name: "نشر الإصدار", exact: true })).toBeDisabled();
    await page.getByLabel(/الفيديو · MP4/).setInputFiles(resolve(__dirname, "../../fixtures/help/synthetic-guide.mp4"));
    await page.getByRole("button", { name: "رفع الفيديو وفحصه", exact: true }).click(); await expect(page.getByText("فيديو محفوظ · 2 ثانية", { exact: true })).toBeVisible();
    await page.getByLabel(/صورة مصغرة · PNG/).setInputFiles(resolve(__dirname, "../../fixtures/help/synthetic-thumbnail.png"));
    await page.getByRole("button", { name: "رفع الصورة وفحصها", exact: true }).click(); await expect(page.getByText("الصورة المصغرة محفوظة", { exact: true })).toBeVisible();
  }
  async function transition(name: string) { page.once("dialog", d => d.accept()); await page.getByRole("button", { name, exact: true }).click(); }
  try {
    await write(platform, `/api/v1/platform/academies/${academy}/entitlements`, { planCode: "Premium", features: { ...features, learning_hub: true } }, true, true); await activate(enabled);
    await guardianLogin(guardian, "01099900101");
    await createAndUpload();
    await guardian.goto("/guardian/help/family-renewal"); await expect(guardian.locator("video")).toHaveCount(0);
    await page.getByRole("button", { name: "معاينة الفيديو الخاص", exact: true }).click();
    await expect.poll(() => page.locator("video").evaluate((v: HTMLVideoElement) => v.readyState)).toBeGreaterThanOrEqual(1);
    await page.locator("video").evaluate((v: HTMLVideoElement) => v.play()); await expect.poll(() => page.locator("video").evaluate((v: HTMLVideoElement) => v.currentTime)).toBeGreaterThan(0);
    const selectedRevision = page.locator(".help-revisions button[aria-pressed=true]");
    await expect(selectedRevision).toHaveCSS("color", "rgb(97, 17, 46)");
    await page.setViewportSize({ width: 1280, height: 1000 }); await page.screenshot({ path: info.outputPath("platform-help-draft-preview-1280.png"), fullPage: true, animations: "disabled" }); await page.setViewportSize({ width: 390, height: 844 });
    await transition("نشر الإصدار"); await expect(page.getByRole("status")).toContainText("تم نشر الفيديو");
    await guardian.reload(); const video = guardian.locator("video"); await expect(video).toHaveAttribute("src", `/api/v1/help/lessons/family-renewal/media/${currentId}/video`);
    await expect.poll(() => video.evaluate((v: HTMLVideoElement) => v.duration)).toBe(2);
    await video.evaluate((v: HTMLVideoElement) => v.play()); await expect.poll(() => video.evaluate((v: HTMLVideoElement) => v.currentTime)).toBeGreaterThan(0);
    await expect(guardian.getByRole("list")).toContainText("رفع الإيصال ليس قبولًا نهائيًا");
    expect(await guardian.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await video.evaluate(v => v.scrollIntoView({ block: "center", behavior: "instant" }));
    await expect.poll(async () => { const box = await video.boundingBox(); return box ? box.y >= 0 && box.y + box.height < 740 : false; }).toBe(true);
    await guardian.screenshot({ path: info.outputPath("guardian-help-video-playing-390.png"), animations: "disabled" });
    const oldPath = `/api/v1/help/lessons/family-renewal/media/${currentId}/video`;
    await transition("أرشفة الإصدار"); await expect(page.getByRole("status")).toContainText("تمت الأرشفة"); expect((await guardian.request.get(oldPath)).status()).toBe(404);
    await guardian.reload(); await expect(guardian.locator("video")).toHaveCount(0); await expect(guardian.getByRole("list")).toBeVisible();
    await guardian.screenshot({ path: info.outputPath("guardian-help-video-archived-390.png"), fullPage: true, animations: "disabled" });
    expect(await report()).toEqual(before);
    if (process.env.ACADEMY_QA_KEEP_HELP_ENABLED === "true") { await createAndUpload(); await transition("نشر الإصدار"); await expect(page.getByRole("status")).toContainText("تم نشر الفيديو"); }
  } finally {
    if (process.env.ACADEMY_QA_KEEP_HELP_ENABLED !== "true") {
      if (currentId) { const rows = (await (await platform.get("/api/v1/platform/help-videos/")).json()).revisions; const row = rows.find((x: { id: string }) => x.id === currentId); if (row && row.state !== "Archived") await write(platform, `/api/v1/platform/help-videos/${currentId}/transition`, { version: row.editVersion, target: "Archived" }, true); }
      await activate(original); await write(platform, `/api/v1/platform/academies/${academy}/entitlements`, { planCode: licenses[0].planCode, features }, true, true);
    }
    await ownerContext.close(); await guardianContext.close();
  }
});
