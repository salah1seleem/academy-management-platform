import { expect, test } from "@playwright/test";

test("FIN-01 forecast drills down to 20 expired paid players and exports 12700 without debt", async ({ page }, info) => {
  test.setTimeout(90_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const branch = "a2030100-0001-0000-0000-000000000001";
  const summary = page.waitForResponse(r => r.url().includes("/reports/owner-summary?") && r.url().includes(branch));
  const options=await (await page.request.get("/api/v1/manage/structure/options")).json();
  await page.getByRole("navigation",{name:"الفروع",exact:true}).getByRole("button",{name:options.branches.find((b:{id:string})=>b.id===branch).arabicName,exact:true}).click(); expect((await summary).status()).toBe(200);
  const card = page.getByRole("region", { name: "مؤشرات الأكاديمية" }).getByRole("link",{name:/أعضاء يحتاجون متابعة/}); await expect(card).toHaveAttribute("href",new RegExp(`/dashboard/finance/renewals\\?.*branchId=${branch}`)); await expect(card).toContainText("20");
  await card.click();
  await expect(page.getByRole("heading", { name: "فرص التجديد", exact: true })).toBeVisible(); await expect(page.getByText("20", { exact: true }).first()).toBeVisible(); await expect(page.getByText("12,700 جنيه", { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "ملف اللاعب والتجديد" })).toHaveCount(20);
  const csvLink = page.getByRole("link", { name: /تصدير CSV/ }); const csv = await page.request.get((await csvLink.getAttribute("href"))!); expect(csv.status()).toBe(200); expect((await csv.text()).split('\n')).toHaveLength(21);
  const debt = await (await page.request.get(`/api/v1/finance/receivables?branchId=${branch}`)).json(); expect(debt.overdue).toBe("0.00"); expect(debt.count).toBe(0);
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await page.evaluate(() => window.scrollTo(0, 0)); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`renewal-opportunities-${width}.png`), fullPage: false, animations: "disabled" }); }
  await page.getByLabel("البحث باللاعب").fill("DEMO-FIN01-01"); await page.getByRole("button", { name: "تحديث الفرص" }).click(); await expect(page.getByRole("link", { name: "ملف اللاعب والتجديد" })).toHaveCount(1);
  await page.getByRole("button", { name: "مسح الفلاتر" }).click(); await expect(page.getByRole("combobox", { name: "الفرع", exact: true })).toHaveValue("");
});
