import { expect, test } from "@playwright/test";
import { createHmac } from "node:crypto";
import { guardianLogin } from "../support/auth";

test("Owner follows a durable signed mismatch; a correct event confirms once without deleting the discrepancy", async ({ page }, info) => {
  const root = "/api/v1/finance/provider-quarantine", issueId = "85000010-0000-0000-0000-000000000003", paymentId = "85000010-0000-0000-0000-000000000002";
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "تسجيل الدخول" }).click(); await page.waitForURL("**/dashboard");
  await page.goto("/dashboard/finance/provider-quarantine"); await page.getByRole("button", { name: "مسح الفلاتر" }).click();
  await expect(page.getByLabel("من تاريخ")).toHaveValue(""); await expect(page.getByLabel("إلى تاريخ")).toHaveValue("");
  await page.goto(`/dashboard/finance/provider-quarantine/${issueId}`); await expect(page.getByRole("heading", { name: "تفاصيل اختلاف الدفع" })).toBeVisible();
  await expect(page.getByText("925 جنيه", { exact: true })).toBeVisible(); await expect(page.getByText("900 جنيه", { exact: true })).toBeVisible();
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 1000 }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`provider-quarantine-${width}.png`), animations: "disabled" }); }
  expect(await page.locator("main").getByRole("button", { name: /^(?!تفعيل المظهر ).*(?:قبول|اعتماد|تفعيل)/ }).count()).toBe(0);
  const event = { academyId: "10000000-0000-0000-0000-000000000001", paymentRequestId: paymentId, providerReference: "demo-quarantine-request", providerEventId: `e2e-correct-${Date.now()}`, status: "succeeded", amount: 900, currency: "EGP", signature: "" };
  const payload = [event.academyId, event.paymentRequestId, event.providerReference, event.providerEventId, event.status, event.amount.toFixed(2), event.currency].join("|");
  event.signature = createHmac("sha256", "Demo-Test-Signing-Key-Only-123456").update(payload).digest("hex").toUpperCase();
  const responses = await Promise.all([page.request.post("/api/v1/payments/internal-test/events", { data: event }), page.request.post("/api/v1/payments/internal-test/events", { data: event })]); for (const response of responses) expect(response.ok(), await response.text()).toBe(true);
  await page.reload();
  const issue = page.getByRole("region", { name: "ملخص اختلاف الدفع", exact: true });
  await expect(issue.getByText("تم تأكيد الطلب", { exact: true })).toBeVisible();
  await expect(issue.getByRole("heading", { name: "المبلغ مختلف", exact: true })).toBeVisible();
  await expect(issue.getByText("925 جنيه", { exact: true })).toBeVisible();
  const receipt = await page.request.get(`${root}/${issueId}`).then(r => r.json());
  expect(receipt.receiptId).toBeTruthy();
  const issued = issue.getByRole("link", { name: "عرض إيصال التحصيل", exact: true });
  await expect(issued).toHaveAttribute("href", `/dashboard/subscriptions/receipts/${receipt.receiptId}`);
  await issued.click();
  await expect(page.getByRole("article", { name: "إيصال دفع", exact: true })).toBeVisible();
  const guardian = await page.context().browser()!.newContext(); const guardianPage = await guardian.newPage(); await guardianLogin(guardianPage);
  expect((await guardianPage.request.get(`${root}/${issueId}`)).status()).toBe(403); await guardian.close();
});
