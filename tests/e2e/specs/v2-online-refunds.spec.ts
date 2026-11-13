import { expect, test } from "@playwright/test";

test("an acknowledged external refund is recovered after a lost response and page reload", async ({ page }) => {
  const player = "36000000-0000-0000-0000-000000000001";
  const api = `/api/v1/people/players/${player}/refunds`;
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click();
  await page.waitForURL("**/dashboard");
  const history = async () => {
    const response = await page.request.get(api);
    expect(response.ok()).toBe(true);
    return response.json();
  };
  const before = await history();
  const source = before.collections.find((item: { paymentMethod: string; available: string }) =>
    item.paymentMethod === "Online" && Number(item.available) >= 1.25);
  expect(source).toBeTruthy();
  let writes = 0;
  let savedId = "";
  await page.route(`**${api}`, async route => {
    if (route.request().method() !== "POST") return route.continue();
    writes++;
    const response = await route.fetch();
    expect(response.ok()).toBe(true);
    savedId = (await response.json()).id;
    await route.abort("connectionreset");
  });
  await page.goto(`/dashboard/players/${player}?tab=subscriptions`);
  const panel = page.getByRole("region", { name: "استرداد اللاعب" });
  await panel.getByText("تسجيل استرداد", { exact: true }).click();
  await panel.getByRole("combobox", { name: /^التحصيل/ }).selectOption(source.id);
  await panel.getByLabel("قيمة الاسترداد").fill("1.25");
  await panel.getByRole("button", { name: "مراجعة الاسترداد", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "مراجعة الاسترداد" });
  await expect(dialog).toContainText("1.25 جنيه");
  await dialog.getByRole("button", { name: "تسجيل التحويل المنفّذ" }).click();
  await expect(panel.getByRole("region", { name: "التحقق من الاسترداد" })).toBeVisible();
  await expect(panel.getByLabel("قيمة الاسترداد")).toHaveCount(0);
  await page.reload();
  await expect(panel.getByRole("status")).toContainText("تم تسجيل الاسترداد بنجاح بقيمة 1.25 جنيه");
  const after = await history();
  expect(writes).toBe(1);
  expect(after.refunds).toHaveLength(before.refunds.length + 1);
  expect(after.refunds.filter((row: { id: string }) => row.id === savedId)).toEqual([
    expect.objectContaining({ amount: 1.25, collectionId: source.id, payoutMethod: "Online", cashSessionId: null }),
  ]);
});
