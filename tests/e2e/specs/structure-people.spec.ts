import { expect, test } from "@playwright/test";

test("Demo Admin registers a real player and enrollment from mobile", async ({ page }) => {
  const suffix = Date.now().toString().slice(-6);
  const playerName = `يوسف اختبار ${suffix}`;
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("admin.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click();
  await page.getByRole("navigation", { name: "الإجراءات التشغيلية السريعة" }).getByRole("link", { name: /^تسجيل لاعب/ }).click();
  await expect(page.getByRole("heading", { name: "تسجيل لاعب جديد" })).toBeVisible();
  await page.getByLabel("اسم اللاعب بالعربية").fill(playerName);
  await page.getByLabel("تاريخ الميلاد").fill("2016-05-10");
  await page.getByLabel("اسم ولي الأمر").fill(`وليد اختبار ${suffix}`);
  await page.getByLabel("رقم الهاتف").fill(`0101${suffix}`.padEnd(11, "7"));
  await page.getByLabel("الفرع").selectOption({ label: "فرع مدينة نصر" });
  await page.getByLabel("الرياضة").selectOption({ label: "كرة القدم" });
  await page.getByLabel("الفئة العمرية").selectOption({ label: "تحت 10 سنوات" });
  await page.getByLabel("المجموعة").selectOption({ label: "ناشئين 2016 — مجموعة أ" });
  await page.getByRole("button", { name: "حفظ اللاعب" }).click();
  await expect(page.getByRole("heading", { name: "قائمة اللاعبين" })).toBeVisible();
  await page.getByLabel("بحث اللاعبين").fill(playerName);
  await expect(page.getByText(playerName, { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await page.getByRole("button", { name: "تسجيل الخروج" }).click();
});

test("Demo Guardian sees linked siblings and no unrelated child", async ({ page }) => {
  await page.goto("/login");
  await page.getByRole("button", { name: "ولي أمر" }).click();
  await page.getByLabel("رقم الهاتف").fill("01000000001");
  await page.getByRole("button", { name: "طلب رمز تجريبي" }).click();
  await page.getByLabel("رمز التحقق").fill("246810");
  await page.getByRole("button", { name: "تحقق ودخول" }).click();
  await expect(page.getByRole("heading", { name: "الأبناء", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "عمر أحمد محمود" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "مريم أحمد محمود" })).toBeVisible();
  await expect(page.getByText("عمر أحمد حسن", { exact: true })).toHaveCount(0);
});
