import { expect, test, type Locator } from "@playwright/test";

test("Owner previews and activates a version; disabled feature rejects API; reactivation restores access", async ({ page }) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL("**/dashboard");
  type Version = { id: string; revision: number; state: string; version: number; settings: { features: Record<string, boolean> } };
  const versions = async (): Promise<Version[]> => (await page.request.get("/api/v1/manage/configuration").then(r => r.json())).versions;
  const initial = (await versions()).find(v => v.state === "Active")!;
  const restoredSettings = { ...initial.settings, features: { ...initial.settings.features, nutrition: true } };
  async function restoreNutrition() {
    if ((await versions()).find(v => v.state === "Active")?.settings.features.nutrition) return;
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
    const headers = { "X-CSRF-TOKEN": csrf.token };
    const saved = await page.request.post("/api/v1/manage/configuration/drafts", { headers, data: restoredSettings });
    expect(saved.ok(), await saved.text()).toBe(true); const { id } = await saved.json();
    for (const target of ["Validated", "Staged", "Active"]) {
      const current = (await versions()).find(v => v.id === id)!;
      const changed = await page.request.post(`/api/v1/manage/configuration/${id}/transition`, { headers, data: { target, version: current.version } });
      expect(changed.ok(), await changed.text()).toBe(true);
    }
  }
  // Recover only this synthetic test's feature toggle from an interrupted run.
  // Use the audited/versioned workflow, preserving unrelated settings.
  await restoreNutrition();
  await page.goto("/dashboard/settings");
  const panel = page.locator("section").filter({ has: page.getByRole("heading", { name: "إصدارات إعدادات التشغيل", exact: true }) });
  await expect(panel.getByRole("checkbox", { name: /إعلانات المجتمع/ })).toBeDisabled();
  async function saveDraft(enabled: boolean) {
    const response = page.waitForResponse(r => r.url().endsWith("/api/v1/manage/configuration/drafts") && r.request().method() === "POST");
    await panel.getByRole("button", { name: "حفظ إصدار مسودة" }).click();
    expect((await response).ok()).toBe(true);
    // Read the authoritative draft separately, without waiting on a proxied
    // browser response body that may still be streaming.
    const draft = (await versions()).filter(v => v.state === "Draft" && v.settings.features.nutrition === enabled).sort((a, b) => b.revision - a.revision)[0];
    expect(draft).toBeTruthy();
    return panel.locator("article").filter({ hasText: new RegExp(`الإصدار ${draft.revision} ·`) });
  }
  async function activate(draft: Locator) {
    await draft.getByRole("button", { name: "التحقق", exact: true }).click();
    await draft.getByRole("button", { name: "تجهيز التفعيل", exact: true }).click();
    await draft.getByRole("button", { name: "تفعيل الآن", exact: true }).click();
    await page.getByRole("dialog").getByRole("button", { name: "تأكيد الاعتماد" }).click();
    await expect(page.getByRole("dialog")).toHaveCount(0);
  }
  try {
    await panel.getByRole("checkbox", { name: "التغذية", exact: true }).uncheck();
    const draft = await saveDraft(false); await expect(draft).toContainText("التغذية: إيقاف");
    expect((await page.request.get("/api/v1/content/nutrition/")).status()).toBe(200);
    await activate(draft);
    expect((await page.request.get("/api/v1/content/nutrition/")).status()).toBe(403);
    await panel.getByRole("checkbox", { name: "التغذية", exact: true }).check();
    await activate(await saveDraft(true));
    expect((await page.request.get("/api/v1/content/nutrition/")).status()).toBe(200);
  } finally {
    await restoreNutrition();
  }
});
