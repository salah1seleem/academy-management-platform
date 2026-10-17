import { APIRequestContext, expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

test("Role-aware written help searches, drills down and revokes without changing financial results", async ({ page, browser }, info) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 390, height: 844 }); await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const base = new URL(page.url()).origin, owner = page.request;
  const platformContext = await browser.newContext({ baseURL: base }), platform = platformContext.request;
  async function write(api: APIRequestContext, path: string, data: unknown, elevated = false, put = false) {
    const csrf = await (await api.get(elevated ? "/api/v1/platform/auth/csrf" : "/api/v1/auth/csrf")).json();
    const r = await api.fetch(path, { method: put ? "PUT" : "POST", data, headers: { "X-CSRF-TOKEN": csrf.token } }); expect(r.ok(), `${path} ${r.status()}`).toBe(true); return r;
  }
  const academy = "30000000-0000-0000-0000-000000000003";
  await write(platform, "/api/v1/platform/auth/login", { email: "platform.demo@example.test", password: "Demo-Only-123!", code: "246810" }, true);
  const licenses: { feature: string; enabled: boolean; planCode: string }[] = await (await platform.get(`/api/v1/platform/academies/${academy}/entitlements`)).json();
  const features = Object.fromEntries(licenses.map(x => [x.feature, x.enabled]));
  const config = await (await owner.get("/api/v1/manage/configuration/")).json();
  const original = config.versions.find((x: { state: string }) => x.state === "Active").settings;
  const enabled = { ...original, helpAudiences: {}, features: { ...original.features, learning_hub: true } };
  async function activate(settings: unknown) {
    const draft = await (await write(owner, "/api/v1/manage/configuration/drafts", settings)).json();
    for (const target of ["Validated", "Staged", "Active"]) {
      const current = await (await owner.get("/api/v1/manage/configuration/")).json();
      const row = current.versions.find((x: { id: string }) => x.id === draft.id);
      await write(owner, `/api/v1/manage/configuration/${draft.id}/transition`, { target, version: row.version });
    }
  }
  const report = async () => { const r = await owner.get("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"); expect(r.ok()).toBe(true); return r.json(); };
  const before = await report();
  const familyContext = await browser.newContext({ baseURL: base, viewport: { width: 390, height: 844 } }); const guardian = await familyContext.newPage();
  try {
    await write(platform, `/api/v1/platform/academies/${academy}/entitlements`, { planCode: "Premium", features: { ...features, learning_hub: true } }, true, true); await activate(enabled);
    await page.reload(); await page.getByRole("button", { name: "فتح القائمة" }).click(); await page.getByRole("link", { name: "دليل الاستخدام", exact: true }).click();
    await expect(page.getByRole("button", { name: "فتح القائمة" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "كيف نساعدك اليوم؟" })).toBeVisible();
    await expect(page.getByRole("link", { name: /مراجعة حارات السباحة/ })).toHaveCount(0);
    await page.getByLabel("البحث في الأدلة").fill("إِنْشَاء بَاقَة"); await page.getByRole("button", { name: "بحث", exact: true }).click(); await expect(page.locator(".help-card")).toHaveCount(1);
    await page.getByRole("link", { name: /إنشاء باقة/ }).click(); await expect(page.getByRole("list").filter({ hasText: "افتح الباقات" })).toBeVisible();
    await expect(page.getByText(/الدليل المكتوب متاح دائمًا/)).toBeVisible(); await expect(page.locator("video")).toHaveCount(0); await page.screenshot({ path: info.outputPath("owner-help-reading-390.png"), fullPage: true, animations: "disabled" });
    await page.getByRole("link", { name: "كل الأدلة" }).click(); await page.setViewportSize({ width: 1440, height: 1000 }); await expect(page.locator(".help-card").first()).toBeVisible(); await page.screenshot({ path: info.outputPath("owner-help-catalog-1440.png"), animations: "disabled" });
    await guardianLogin(guardian, "01099900101"); await guardian.getByRole("link", { name: "دليل الاستخدام", exact: true }).click();
    await expect(guardian.locator(".help-card")).toHaveCount(2); await expect(guardian.getByRole("link", { name: /قراءة لوحة المالك/ })).toHaveCount(0);
    await guardian.getByRole("link", { name: /تجديد اشتراك أبنائك/ }).click(); await expect(guardian.getByRole("list").filter({ hasText: "رفع الإيصال ليس قبولًا نهائيًا" })).toBeVisible();
    await expect.poll(() => guardian.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); await guardian.screenshot({ path: info.outputPath("guardian-help-reading-390.png"), fullPage: true, animations: "disabled" });
    expect((await guardian.request.get("/api/v1/help/lessons/owner-dashboard")).status()).toBe(404);
    const adminContext = await browser.newContext({ baseURL: base }); const admin = await adminContext.newPage();
    try {
      await admin.goto("/login"); await admin.getByLabel("البريد الإلكتروني").fill("admin.football@example.test"); await admin.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await admin.getByRole("button", { name: "دخول آمن" }).click(); await admin.waitForURL("**/dashboard"); await admin.goto("/dashboard/help");
      await expect(admin.getByRole("link", { name: /حضور اللاعبين/ })).toBeVisible(); await expect(admin.getByRole("link", { name: /قراءة لوحة المالك/ })).toHaveCount(0);
    } finally { await adminContext.close(); }
    // Owner changes the real audience through the versioned settings UI.
    await page.goto('/dashboard/settings');
    await page.getByRole('checkbox',{name:'تجديد اشتراك أبنائك — ولي الأمر',exact:true}).uncheck();
    async function saveAudienceDraft() {
      const saved=page.waitForResponse(r=>r.url().endsWith('/api/v1/manage/configuration/drafts')&&r.request().method()==='POST');
      await page.getByRole('button',{name:'حفظ إصدار مسودة',exact:true}).click();expect((await saved).ok()).toBe(true);
      const config=await (await owner.get('/api/v1/manage/configuration/')).json();const draft=config.versions.find((v:{state:string})=>v.state==='Draft');expect(draft).toBeTruthy();return draft;
    }
    async function approveAudienceDraft(draft:{revision:number}) {
      const row=page.locator('.linked-list article').filter({hasText:`الإصدار ${draft.revision} ·`});
      await row.getByRole('button',{name:'التحقق',exact:true}).click();await row.getByRole('button',{name:'تجهيز التفعيل',exact:true}).click();await row.getByRole('button',{name:'تفعيل الآن',exact:true}).click();
      const dialog=page.getByRole('dialog');await expect(dialog).toContainText('تغيير جمهور دليل الاستخدام');await dialog.getByRole('button',{name:'تأكيد الاعتماد',exact:true}).click();await expect(dialog).not.toBeVisible();
      await expect(row).toContainText('سارية');
    }
    const hidden=await saveAudienceDraft();expect((await guardian.request.get('/api/v1/help/lessons/family-renewal')).status()).toBe(200);await approveAudienceDraft(hidden);
    await guardian.reload();await expect(guardian.getByRole('alert').filter({hasText:'غير متاح'})).toBeVisible();await guardian.goto('/guardian/help');await expect(guardian.locator('.help-card')).toHaveCount(1);
    await guardian.screenshot({path:info.outputPath('guardian-help-audience-390.png'),fullPage:true,animations:'disabled'});
    await guardian.goto('/guardian/renew-family');await expect(guardian.getByRole('heading',{name:'راجع تجديد أبنائك معًا'})).toBeVisible();await expect(guardian.getByRole('link',{name:'شرح هذه الخطوة'})).toHaveCount(0);
    const familySection=page.getByRole('heading',{name:'تجديد اشتراك أبنائك',exact:true}).locator('..');await familySection.screenshot({path:info.outputPath('owner-help-audience-hidden.png'),animations:'disabled'});
    await familySection.getByRole('button',{name:'استعادة جمهور المنتج',exact:true}).click();await approveAudienceDraft(await saveAudienceDraft());
    await guardian.reload();await guardian.getByRole('link',{name:'شرح هذه الخطوة',exact:true}).click();await expect(guardian).toHaveURL(/\/guardian\/help\/family-renewal$/);await expect(guardian.getByRole('heading',{name:'تجديد اشتراك أبنائك',exact:true})).toBeVisible();
    await page.goto('/dashboard/subscriptions/plans');await page.getByRole('link',{name:'شرح هذه الخطوة',exact:true}).click();await expect(page).toHaveURL(/\/dashboard\/help\/packages$/);
    await activate({ ...original, features: { ...original.features, learning_hub: false } }); await guardian.reload(); await expect(guardian.getByRole("alert").filter({ hasText: "غير متاح" })).toBeVisible(); await expect(guardian.getByRole("list")).toHaveCount(0); expect(await report()).toEqual(before);
  } finally {
    await activate(original);
    await write(platform, `/api/v1/platform/academies/${academy}/entitlements`, { planCode: licenses[0].planCode, features }, true, true);
    // Explicit isolated QA fixture only, so the subsequent real native journey can use the same guides.
    if (process.env.ACADEMY_QA_KEEP_HELP_ENABLED === "true") { await write(platform, `/api/v1/platform/academies/${academy}/entitlements`, { planCode: "Premium", features: { ...features, learning_hub: true } }, true, true); await activate(enabled); }
    await familyContext.close(); await platformContext.close();
  }
});
