import { expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

test("Owner reviews a free training waiver, creates no fake receipt and Guardian sees the approved service", async ({ page }, info) => {
  const enrollment = "84000015-0000-0000-0000-000000000006";
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  await page.goto("/dashboard/players/84000015-0000-0000-0000-000000000004"); await page.getByRole("tab", { name: "الاشتراكات والمالية", exact: true }).click(); await page.getByRole("link", { name: "اعتماد تدريب مجاني", exact: true }).click();
  await page.getByLabel("الباقة المعفاة").selectOption("51000000-0000-0000-0000-000000000001"); await page.getByLabel("سبب الإعفاء").fill("منحة تدريب اصطناعية لا تتضمن قبضًا");
  await page.getByRole("button", { name: "مراجعة الإعفاء", exact: true }).click(); await expect(page.getByRole("region", { name: "مراجعة الإعفاء" })).toBeVisible(); await expect(page.getByRole("button", { name: "اعتماد الإعفاء وتفعيل الفترة", exact: true })).toBeDisabled();
  const photos = async (stage: string) => { for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 1000 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`free-training-${stage}-${width}.png`), fullPage: true, animations: "disabled" }); } };
  await photos("quote"); await page.getByRole("checkbox", { name: "أعتمد التدريب المجاني دون قبض أي مبلغ", exact: true }).check();
  const posting = page.waitForResponse(r => r.url().endsWith("/api/v1/finance/free-training/approve") && r.request().method() === "POST"); await page.getByRole("button", { name: "اعتماد الإعفاء وتفعيل الفترة", exact: true }).click(); const reply = await posting; expect(reply.ok(), await reply.text()).toBe(true); const result = await reply.json(); await expect(page.getByRole("status").filter({ hasText: "تم اعتماد الإعفاء وتسجيل فترة التدريب" })).toBeVisible(); await photos("saved");
  const replay = await page.request.fetch(reply.url(), { method: "POST", data: reply.request().postDataJSON(), headers: reply.request().headers() }); expect(replay.ok()).toBe(true); expect((await replay.json()).periodId).toBe(result.periodId);
  await page.getByRole("link", { name: "عرض الاشتراك المعتمد" }).click(); await expect(page.getByText("تدريب مجاني بإعفاء معتمد — لا يوجد تحصيل أو إيصال")).toBeVisible(); await expect(page.getByRole("link", { name: "عرض الإيصال المحفوظ" })).toHaveCount(0);
  const ownerPeriod = await page.request.get(`/api/v1/subscriptions/periods/${result.periodId}`).then(r => r.json()); expect(ownerPeriod.origin).toBe("ApprovedWaiver"); expect(ownerPeriod.receiptId).toBeNull(); expect(ownerPeriod.sportEnrollmentId).toBe(enrollment); expect(Number(ownerPeriod.priceSnapshot)).toBe(0);
  await guardianLogin(page, "01000000015"); const url = `/api/v1/guardian/subscriptions/periods/${result.periodId}/adjustments`; const own = await page.request.get(url); expect(own.ok()).toBe(true); expect((await own.json()).origin).toBe("ApprovedWaiver"); expect(await own.text()).not.toContain("منحة تدريب اصطناعية لا تتضمن قبضًا");
  await guardianLogin(page); expect((await page.request.get(url)).status()).toBe(404);
});
