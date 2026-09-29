import { expect, Page, test } from "@playwright/test";

let originalCheckout = "";
let renewalDetail = "";

async function guardianLogin(page: Page) {
  await page.context().clearCookies(); await page.goto("/login"); await page.getByRole("button", { name: "ولي أمر" }).click();
  await page.getByLabel("رقم الهاتف").fill("01000000001"); await page.getByRole("button", { name: "طلب رمز تجريبي" }).click();
  await page.getByLabel("رمز التحقق").fill("246810"); await page.getByRole("button", { name: "تحقق ودخول" }).click(); await page.waitForURL("**/guardian**");
}

async function staffLogin(page: Page, email = "admin.nogoom@example.test") {
  await page.context().clearCookies(); await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill(email);
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard**");
}

async function startRenewal(page: Page) {
  await guardianLogin(page); const child = page.locator("article.guardian-child-card").filter({ hasText: "عمر أحمد محمود" });
  await child.locator(".subscription-mini p").filter({ hasText: "كرة القدم" }).getByRole("link").click();
  await page.getByText("اشتراك شهري", { exact: true }).click(); await page.getByRole("button", { name: /الدفع الإلكتروني — ادفع الآن/ }).click();
  await page.waitForURL("**/guardian/checkout/**"); const url = page.url(); const paymentId = url.split("/").pop()!;
  const renewalId = await page.evaluate(async id => fetch(`/api/v1/guardian/subscriptions/payments/${id}`).then(r => r.json()).then((x: { renewalId: string }) => x.renewalId), paymentId);
  return { url, renewalId };
}

async function applyDiscount(page: Page, renewalId: string, type: "نسبة مئوية" | "مبلغ ثابت", value: string, reason: string) {
  await staffLogin(page); renewalDetail = `/dashboard/subscriptions/renewals/${renewalId}`; await page.goto(renewalDetail);
  await page.getByRole("button", { name: "تطبيق خصم" }).click(); await page.getByLabel("نوع الخصم").selectOption({ label: type });
  await page.getByLabel("قيمة الخصم").fill(value); await page.getByLabel("سبب الخصم").fill(reason);
  const saved = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith(`/api/v1/subscriptions/renewals/${renewalId}/discount`));
  await page.getByRole("button", { name: "حفظ الخصم" }).click();
  expect((await saved).status()).toBe(200); await expect(page.getByText(reason, { exact: true })).toBeVisible();
}

test.describe.serial("Slice 8C subscription discount journeys", () => {
  test("Journey A — Admin applies 10 percent and final amount updates", async ({ page }) => {
    const renewal = await startRenewal(page); originalCheckout = renewal.url; await applyDiscount(page, renewal.renewalId, "نسبة مئوية", "10", "خصم إخوة تجريبي");
    await expect(page.getByText("10% (-90 EGP)")).toBeVisible(); await expect(page.getByText("810 EGP", { exact: true })).toBeVisible(); await expect(page.getByText("خصم إخوة تجريبي")).toBeVisible();
  });

  test("Journey B — Guardian pays the discounted final amount and sees receipt", async ({ page }) => {
    await guardianLogin(page); await page.goto(originalCheckout); await page.getByRole("link", { name: "فتح طلب الدفع الحالي" }).click();
    await expect(page.getByText("السعر الأصلي")).toBeVisible(); await expect(page.getByText("10% (-90 EGP)")).toBeVisible(); await expect(page.getByText("810 EGP", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "محاكاة نجاح الدفع" }).click(); await expect(page.getByText("تم الدفع وتجديد الاشتراك بنجاح")).toBeVisible();
    await page.getByRole("link", { name: "عرض الإيصال" }).click(); await expect(page.getByText("السعر الأصلي")).toBeVisible(); await expect(page.getByText("10% (-90 جنيه)")).toBeVisible(); await expect(page.getByText("810 جنيه")).toBeVisible();
  });

  test("Journey C — Failed discounted payment has no receipt and retry retains amount", async ({ page }) => {
    const renewal = await startRenewal(page); await applyDiscount(page, renewal.renewalId, "مبلغ ثابت", "150", "خصم حالة تشغيلية");
    await guardianLogin(page); await page.goto(renewal.url); await page.getByRole("link", { name: "فتح طلب الدفع الحالي" }).click(); await expect(page.getByText("750 EGP", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "محاكاة فشل الدفع" }).click(); await expect(page.getByText("فشلت عملية الدفع ولم يتم تجديد الاشتراك.")).toBeVisible(); await expect(page.getByRole("link", { name: "عرض الإيصال" })).toHaveCount(0);
    await page.getByRole("button", { name: "إعادة محاولة الدفع" }).click(); await expect(page.getByText("750 EGP", { exact: true })).toBeVisible();
  });

  test("Journey D — Guardian and Coach cannot mutate discounts", async ({ page }) => {
    await guardianLogin(page); const guardianStatus = await page.evaluate(async path => { const csrf = await fetch("/api/v1/auth/csrf").then(r => r.json()) as { token: string }; return fetch(`${path}/discount`, { method: "POST", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() }, body: JSON.stringify({ type: "Percentage", value: 10, reason: "مرفوض", expectedVersion: 1 }) }).then(r => r.status); }, `/api/v1/subscriptions/renewals/${renewalDetail.split("/").pop()}`); expect(guardianStatus).toBe(403);
    await staffLogin(page, "coach.nogoom@example.test"); const coachStatus = await page.evaluate(async path => { const csrf = await fetch("/api/v1/auth/csrf").then(r => r.json()) as { token: string }; return fetch(`${path}/discount`, { method: "POST", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() }, body: JSON.stringify({ type: "Percentage", value: 10, reason: "مرفوض", expectedVersion: 1 }) }).then(r => r.status); }, `/api/v1/subscriptions/renewals/${renewalDetail.split("/").pop()}`); expect(coachStatus).toBe(403);
  });
});
