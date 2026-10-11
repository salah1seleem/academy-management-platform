import { expect, test } from "@playwright/test";

test("Owner posts an opening balance, corrects it without cash and drills into immutable history", async ({ page }, info) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const options = await (await page.request.get("/api/v1/finance/receivables/options")).json();
  const enrollment = options.enrollments[0]; expect(enrollment).toBeTruthy();
  await page.goto("/dashboard/finance/receivables");
  await page.getByRole("button", { name: "إضافة مطالبة", exact: true }).click();
  await page.getByRole("combobox", { name: "تسجيل اللاعب", exact: true }).selectOption(enrollment.id);
  await page.getByLabel("المبلغ بالجنيه").fill("25.50"); await page.getByLabel("تاريخ الاستحقاق").fill("2026-09-20");
  const description = `رصيد افتتاحي تحقق ${Date.now()}`;
  await page.getByLabel("الوصف", { exact: true }).fill(description);
  await page.getByRole("combobox", { name: "المصدر", exact: true }).selectOption("OpeningBalance");
  const post = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/finance/receivables"));
  await page.getByRole("button", { name: "تثبيت المطالبة", exact: true }).click(); const saved = await post; expect(saved.status()).toBe(201);
  const detailsLink = page.getByRole("row").filter({ hasText: description }).getByRole("link", { name: "التفاصيل والحركات" });
  await expect(detailsLink).toBeVisible(); const id = (await detailsLink.getAttribute("href"))!.split("/").pop()!;
  await page.goto(`/dashboard/finance/receivables/${id}`);
  await expect(page.getByRole("heading", { name: description })).toBeVisible();
  await expect(page.getByText(/مستند مثبت لا يُعدل ولا يُحذف/)).toBeVisible();
  await page.getByLabel("المبلغ", { exact: true }).fill("5.00"); await page.getByLabel("سبب الحركة").fill("تصحيح موثق لمثال اصطناعي");
  await page.getByRole("button", { name: "تثبيت الحركة", exact: true }).click(); await page.waitForURL("**/finance/receivables");
  const detail = await (await page.request.get(`/api/v1/finance/receivables/${id}`)).json();
  expect(detail.document.amount).toBe("25.50"); expect(detail.movements).toHaveLength(1); expect(detail.movements[0].amount).toBe("5.00"); expect(detail.movements[0].collectionId).toBeNull();
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(page.getByRole("heading", { name: "المطالبات والأرصدة", exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`receivables-${width}.png`), fullPage: true });
  }
  await page.getByRole("button", { name: /المتأخر المستحق/ }).click();
  await expect(page.getByLabel("حالة الرصيد")).toHaveValue("overdue"); await page.getByRole("button", { name: "مسح الفلتر" }).click(); await expect(page.getByLabel("حالة الرصيد")).toHaveValue("");
  const link = page.getByRole("link", { name: /تصدير CSV/ }); await expect(link).toHaveAttribute("href", /asOf=/);
  const csv = await page.request.get((await link.getAttribute("href"))!); expect(csv.status()).toBe(200); expect(await csv.text()).toContain("20.50");
});
