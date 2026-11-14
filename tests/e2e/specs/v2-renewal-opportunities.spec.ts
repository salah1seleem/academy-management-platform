import { expect, test } from "@playwright/test";

test("FIN-01 forecast drills down to 20 expired paid players and exports 12700 without debt", async ({ page }, info) => {
  test.setTimeout(90_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "تسجيل الدخول" }).click(); await page.waitForURL("**/dashboard");
  const branch = "a2030100-0001-0000-0000-000000000001";
  await page.goto(`/dashboard/finance/renewals?branchId=${branch}`);
  await expect(page.getByRole("heading", { name: "فرص التجديد", exact: true })).toBeVisible();
  await expect(page.getByRole("combobox", { name: "الفرع", exact: true })).toHaveValue(branch);
  const read = await page.request.get(`/api/v1/finance/renewal-opportunities?branchId=${branch}`);
  expect(read.status()).toBe(200);
  const report = await read.json();
  expect(report).toMatchObject({count: 20, potential: "12700.00", currency: "EGP"});
  expect(report.items).toHaveLength(20);
  expect(report.items.every((row: { branchId: string }) => row.branchId === branch)).toBe(true);
  await expect(page.getByText("تسجيلات مؤهلة للتجديد", { exact: true }).locator("..")).toContainText("20");
  await expect(page.getByText("12,700 جنيه", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "ملف اللاعب والتجديد" })).toHaveCount(20);
  const csvLink = page.getByRole("link", { name: /تصدير CSV/ }); const csv = await page.request.get((await csvLink.getAttribute("href"))!); expect(csv.status()).toBe(200); expect((await csv.text()).split('\n')).toHaveLength(21);
  const debt = await (await page.request.get(`/api/v1/finance/receivables?branchId=${branch}`)).json(); expect(debt.overdue).toBe("0.00"); expect(debt.count).toBe(0);
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await page.evaluate(() => window.scrollTo(0, 0)); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`renewal-opportunities-${width}.png`), fullPage: false, animations: "disabled" }); }
  await page.getByLabel("البحث باللاعب").fill("DEMO-FIN01-01"); await page.getByRole("button", { name: "تحديث الفرص" }).click(); await expect(page.getByRole("link", { name: "ملف اللاعب والتجديد" })).toHaveCount(1);
  await page.getByRole("button", { name: "مسح الفلاتر" }).click(); await expect(page.getByRole("combobox", { name: "الفرع", exact: true })).toHaveValue("");
});
