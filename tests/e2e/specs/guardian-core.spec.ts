import { expect, Page, test } from "@playwright/test";

async function guardianLogin(page: Page) {
  await page.goto("/login"); await page.getByRole("button", { name: "ولي أمر" }).click(); await page.getByLabel("رقم الهاتف").fill("01000000001"); await page.getByRole("button", { name: "طلب رمز تجريبي" }).click(); await page.getByLabel("رمز التحقق").fill("246810"); await page.getByRole("button", { name: "تحقق ودخول" }).click();
  await page.waitForURL(/\/guardian(?:\?|$)/);
}
async function adminLogin(page: Page) {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("admin.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.waitForURL(/\/dashboard(?:\?|$)/);
}
async function openGuardianRequests(page: Page) {
  const loaded = page.waitForResponse(response => response.url().includes("/api/v1/guardian/enrollment-requests") && response.request().method() === "GET");
  await page.goto("/guardian/enrollment-requests");
  await loaded;
  await expect(page.getByRole("heading", { name: "طلبات الاشتراك" })).toBeVisible();
}

test.describe.serial("Guardian core mobile journeys", () => {
  test("Journey A — polished home, three actions and deduplicated children", async ({ page }) => {
    await guardianLogin(page); await expect(page.getByText("مرحبًا سارة محمود")).toBeVisible(); const actions = page.getByRole("navigation", { name: "إجراءات الاشتراك" }); await expect(actions.getByRole("link", { name: /اشتراك جديد/ })).toBeVisible(); await expect(actions.getByRole("link", { name: /تجديد الاشتراك$/ })).toBeVisible(); await expect(actions.getByRole("link", { name: /تجديد اشتراك لغيره/ })).toBeVisible(); const omar = page.locator("article.guardian-child-card").filter({ hasText: "عمر أحمد محمود" }); await expect(omar).toHaveCount(1); await expect(omar.getByText("كرة القدم", { exact: true }).first()).toBeVisible(); await expect(omar.getByText("السباحة", { exact: true }).first()).toBeVisible();
  });

  test("Journey B — existing child requests a new sport", async ({ page }) => {
    await guardianLogin(page); await page.getByRole("link", { name: /اشتراك جديد/ }).click(); await page.getByRole("button", { name: /إضافة رياضة لطفل موجود/ }).click(); await page.getByLabel("الطفل").selectOption({ label: "مريم أحمد محمود" }); await page.getByLabel("الرياضة").selectOption({ label: "السباحة" }); await page.getByLabel("الفرع المفضل").selectOption({ label: "فرع التجمع الخامس" }); await page.getByRole("button", { name: "إرسال طلب الاشتراك" }).click();
    const requestListHeading = page.getByRole("heading", { name: "طلبات الاشتراك" });
    const alreadySubmitted = page.getByText(/يوجد طلب قائم|مشترك بالفعل/);
    await expect(requestListHeading.or(alreadySubmitted)).toBeVisible({ timeout: 15_000 });
    if (!await requestListHeading.isVisible()) await page.goto("/guardian/enrollment-requests");
    await expect(requestListHeading).toBeVisible();
    const request = page.locator("article").filter({ hasText: "مريم أحمد محمود" }).filter({ hasText: "السباحة" }).filter({ hasText: /قيد الانتظار|مقبول/ }); await expect(request.first()).toBeVisible();
  });

  test("Journey C — admin approves once and Guardian can continue to plans", async ({ page }) => {
    await guardianLogin(page); await openGuardianRequests(page);
    let approved = page.locator("article").filter({ hasText: "مريم أحمد محمود" }).filter({ hasText: "السباحة" }).filter({ hasText: "مقبول" });
    if (await approved.count() === 0) {
      await page.context().clearCookies(); await adminLogin(page); await page.getByRole("button", { name: "فتح القائمة" }).click(); await page.getByRole("button", { name: /اللاعبون/ }).click(); await page.getByRole("link", { name: "طلبات الاشتراك الجديدة" }).click(); const row = page.locator("article").filter({ hasText: "مريم أحمد محمود" }).filter({ hasText: "السباحة" }).filter({ hasText: "قيد الانتظار" }); await row.getByRole("link", { name: "عرض" }).click(); await page.getByLabel("المجموعة").selectOption({ label: "سباحة ناشئين — مجموعة أ" }); await page.getByRole("button", { name: "قبول وإنشاء التسجيل" }).click(); await expect(page.getByRole("heading", { name: "طلبات الاشتراك الجديدة" })).toBeVisible();
      await page.context().clearCookies(); await guardianLogin(page); await openGuardianRequests(page); approved = page.locator("article").filter({ hasText: "مريم أحمد محمود" }).filter({ hasText: "السباحة" }).filter({ hasText: "مقبول" });
    }
    await expect(approved).toHaveCount(1); await approved.getByRole("link", { name: "إكمال الاشتراك / اختيار الباقة" }).click(); await expect(page.getByRole("heading", { name: "اختر الباقة" })).toBeVisible();
  });

  test("Journey D — child profile integrates report, schedule, attendance and subscriptions", async ({ page }) => {
    await guardianLogin(page); const omar = page.locator("article.guardian-child-card").filter({ hasText: "عمر أحمد محمود" }); await omar.getByRole("link", { name: "عرض الملف" }).click(); await expect(page.getByRole("heading", { name: "تقرير اللاعب" })).toBeVisible(); await expect(page.getByRole("heading", { name: "التدريب والمواعيد" })).toBeVisible(); await expect(page.getByRole("heading", { name: "الحضور" })).toBeVisible(); await expect(page.getByRole("heading", { name: "الاشتراكات" })).toBeVisible(); await expect(page.getByText("حاضر", { exact: true }).first()).toBeVisible(); await expect(page.getByRole("link", { name: "عرض التقرير" }).first()).toBeVisible();
  });
});
