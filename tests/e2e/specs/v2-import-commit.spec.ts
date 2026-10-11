import { expect, test } from "@playwright/test";

test("explicit historical import completes in background and survives page reload without fake payment", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const sample = await page.request.get("/api/v1/people/import/demo-sample"); expect(sample.status()).toBe(200);
  await page.goto("/dashboard/import"); await page.getByLabel("ملف بيانات الأكاديمية").setInputFiles({ name: "synthetic-demo.xlsx", mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", buffer: await sample.body() });
  await expect(page.getByRole("heading", { name: "اكتملت المعاينة دون أخطاء" })).toBeVisible(); await expect(page.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toBeDisabled();
  await page.getByRole("checkbox", { name: /راجعت علاقات أولياء الأمور/ }).check(); await page.getByRole("checkbox", { name: /راجعت الأرصدة الافتتاحية/ }).check();
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`import-approval-${width}.png`), animations: "disabled" }); }
  const posted = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/people/import/commit")); await page.getByRole("button", { name: "اعتماد وبدء الاستيراد" }).click(); const response = await posted; expect([200, 202]).toContain(response.status()); const job = await response.json();
  await expect(page.getByRole("heading", { name: "اكتمل الاستيراد" })).toBeVisible({ timeout: 45_000 }); await expect(page.getByRole("link", { name: /آدم مثال الاستيراد/ }).first()).toBeVisible();
  const status = await (await page.request.get(`/api/v1/people/import/jobs/${job.id}`)).json(); expect(status.status).toBe("Completed"); expect(status.publishedRows + status.replayedRows).toBe(3); expect(status.stagedFile).toBeUndefined();
  await page.reload(); await expect(page.getByRole("heading", { name: "اكتمل الاستيراد" })).toBeVisible(); await expect(page.getByRole("link", { name: /ليلى مثال الاستيراد/ })).toBeVisible();
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await page.screenshot({ path: info.outputPath(`import-published-${width}.png`), animations: "disabled" }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
  await page.getByRole("link", { name: /ليلى مثال الاستيراد/ }).click(); await expect(page.getByRole("region", { name: "هوية اللاعب" }).getByText("ليلى مثال الاستيراد", { exact: true })).toBeVisible();
});
