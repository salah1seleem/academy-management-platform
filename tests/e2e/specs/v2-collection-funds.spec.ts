import { expect, test } from "@playwright/test";

test("Owner sees only server-authorized refund availability including allocated amounts", async ({ page }, info) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const player = "36000000-0000-0000-0000-000000000001";
  const data = await page.request.get(`/api/v1/people/players/${player}/refunds`).then(r => r.json());
  const available = data.collections.find((c: { available: string; allocated: number }) => Number(c.available) > 0 && c.allocated > 0);
  expect(available).toBeTruthy();
  expect(Number(available.available)).toBe(available.amount - available.refunded - available.allocated);
  await page.goto(`/dashboard/players/${player}?tab=subscriptions`);
  await expect(page.getByRole("heading", { name: "الاسترداد", exact: true })).toBeVisible();
  await expect(page.getByText(/المبالغ المخصصة لمطالبات ليست متاحة/)).toBeVisible();
  await page.getByText("تسجيل استرداد جزئي", { exact: true }).click();
  await page.getByRole("combobox", { name: "التحصيل", exact: true }).selectOption(available.id);
  const amount = page.getByLabel("قيمة الاسترداد", { exact: true });
  await expect(amount).toHaveAttribute("max", available.available);
  await amount.fill((Number(available.available) + 0.01).toFixed(2));
  expect(await amount.evaluate((input: HTMLInputElement) => input.validity.rangeOverflow)).toBe(true);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.getByRole("heading", { name: "الاسترداد", exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath(`refund-availability-${width}.png`) });
  }
  // Read-only browser journey. API integration separately tests actual posting,
  // idempotency and races; this does not alter a posted demo financial document.
  expect(await page.request.get(`/api/v1/people/players/${player}/refunds`).then(r => r.json())).toEqual(data);
});
