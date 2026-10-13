import { expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

test("Owner collects at another branch without moving the child; guardian receipt and financial filters agree", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard"); page.on("dialog", d => d.accept());
  const root = "/api/v1/finance/cash-registers", enrollmentId = "84000018-0000-0000-0000-000000000002", planId = "84000018-0000-0000-0000-000000000004";
  const homeBranchId = "31000000-0000-0000-0000-000000000001", collectingBranchId = "31000000-0000-0000-0000-000000000002";
  const options = await page.request.get(`${root}/options`).then(r => r.json()), me = await page.request.get("/api/v1/me").then(r => r.json());
  const member = options.members.find((m: { name: string }) => m.name === me.displayName); expect(member).toBeTruthy();
  async function write(path: string, data: unknown, method = "POST") {
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
    const response = await page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() } }); expect(response.ok(), await response.text()).toBe(true); return response.json();
  }
  const register = await write(root, { branchId: collectingBranchId, name: `تحصيل فرع آخر اصطناعي ${Date.now()}` });
  await write(`${root}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT"); const shift = await write(`${root}/${register.id}/open`, { openingAmount: "0.00" });
  const reports = `/api/v1/reports/financial?allDates=true&collectionType=Subscription&search=CB-DEMO-1&branchId=`;
  const before = await page.request.get(reports + collectingBranchId).then(r => r.json()), atHomeBefore = await page.request.get(reports + homeBranchId).then(r => r.json());
  await page.goto(`/dashboard/finance/cash-registers/sessions/${shift.id}`);
  await page.getByLabel("البحث باللاعب أو كوده").fill("CB-DEMO-1"); await page.getByRole("button", { name: "بحث عن اللاعب", exact: true }).click();
  await page.getByRole("combobox", { name: "تسجيل اللاعب", exact: true }).selectOption(enrollmentId); await page.getByRole("combobox", { name: "باقة التدريب", exact: true }).selectOption(planId);
  await page.getByRole("button", { name: "مراجعة مبلغ القبض", exact: true }).click(); const review = page.getByLabel("مراجعة القبض", { exact: true }); await expect(review).toBeVisible();
  await expect(review.getByText("فرع التسجيل", { exact: true })).toBeVisible(); await expect(review.getByText("فرع التحصيل", { exact: true })).toBeVisible(); await expect(review.getByRole("note")).toContainText("يبقى تدريب اللاعب ومجموعته في فرع التسجيل");
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 }); await review.scrollIntoViewIfNeeded(); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`cross-branch-review-${width}.png`), animations: "disabled" });
  }
  await page.getByRole("button", { name: "تأكيد استلام النقدية وإصدار الإيصال", exact: true }).click(); await expect(page.getByText("تم القبض وتحديث رصيد الوردية والاشتراك.", { exact: true })).toBeVisible();
  const saved = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(saved.receipts).toHaveLength(1); const receiptId = saved.receipts[0].receiptId;
  const receipt = await page.request.get(`/api/v1/subscriptions/receipts/${receiptId}`).then(r => r.json()); expect(receipt.enrollmentBranch).not.toBe(receipt.collectionBranch);
  const after = await page.request.get(reports + collectingBranchId).then(r => r.json()); expect(after.totalAmount - before.totalAmount).toBe(receipt.amount);
  expect((await page.request.get(reports + homeBranchId).then(r => r.json())).totalAmount).toBe(atHomeBefore.totalAmount);
  await page.getByLabel("المبلغ الموجود فعليًا", { exact: true }).fill(saved.balance); await page.getByRole("button", { name: "تثبيت المطابقة وإقفال الوردية", exact: true }).click(); await page.waitForURL("**/dashboard/finance/cash-registers");
  await guardianLogin(page); await page.goto(`/guardian/receipts/${receiptId}`); await expect(page.locator(".printable-receipt").getByRole("heading", { name: "إيصال دفع", exact: true })).toBeVisible();
  await expect(page.getByText(receipt.enrollmentBranch, { exact: true })).toBeVisible(); await expect(page.getByText(receipt.collectionBranch, { exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 1000 }); await page.screenshot({ path: info.outputPath("cross-branch-guardian-receipt.png"), animations: "disabled" });
});
