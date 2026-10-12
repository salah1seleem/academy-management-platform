import { expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

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
  await page.getByLabel("الباقة", { exact: true }).selectOption({ index: 1 });
  await page.getByRole("button", { name: "حساب المبلغ المستحق" }).click();
  await expect(page.getByText(/المبلغ المطلوب تحصيله/)).toBeVisible();
  await page.getByRole("button", { name: "تسجيل وتأكيد التحصيل" }).click();
  await page.waitForURL("**/dashboard/players");
  await expect(page.getByRole("heading", { name: "قائمة اللاعبين" })).toBeVisible();
  await page.getByLabel("بحث اللاعبين").fill(playerName);
  await expect(page.getByText(playerName, { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await page.getByRole("button", { name: "تسجيل الخروج" }).click();
});

test("Demo Guardian sees linked siblings and no unrelated child", async ({ page }) => {
  await guardianLogin(page);
  await expect(page.getByRole("heading", { name: "الأبناء", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "عمر أحمد محمود" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "مريم أحمد محمود" })).toBeVisible();
  await expect(page.getByText("عمر أحمد حسن", { exact: true })).toHaveCount(0);
});
