import { expect, test, type Page } from "@playwright/test";
import { guardianLogin } from "../support/auth";

const enrollment = "84000015-0000-0000-0000-000000000006";
const player = "84000015-0000-0000-0000-000000000004";
const monthly = "51000000-0000-0000-0000-000000000001";
const waiverApi = "/api/v1/finance/free-training";
const attemptPath = waiverApi + "/" + enrollment + "/attempt";
const reason = "منحة تدريب اصطناعية";
const approve = "اعتماد الإعفاء";
const confirm = "أعتمد التدريب المجاني دون قبض أي مبلغ";
const review = "مراجعة الإعفاء";
const check = "التحقق من النتيجة";
const periodLink = "عرض الاشتراك المعتمد";
const action = (page: Page, name: string) => page.getByRole("button", { name, exact: true });

async function openWaiver(page: Page) {
  await page.goto("/login");
  await page.waitForLoadState("networkidle");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await action(page, "تسجيل الدخول").click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await page.goto("/dashboard/players/" + player);
  await page.getByRole("tab", { name: "الاشتراكات والمالية", exact: true }).click();
  await page.getByRole("link", { name: "اعتماد تدريب مجاني", exact: true }).click();
  await page.getByRole("combobox", { name: "الباقة المعفاة" }).selectOption(monthly);
  await page.getByRole("textbox", { name: "سبب الإعفاء" }).fill(reason);
}

async function reviewWaiver(page: Page) {
  await action(page, review).click();
  await expect(page.getByRole("region", { name: review })).toBeVisible();
  await expect(action(page, approve)).toBeDisabled();
}

test("An owner can edit a stale waiver and must confirm the fresh quote", async ({ page }) => {
  await openWaiver(page);
  await reviewWaiver(page);
  await page.route("**" + waiverApi + "/approve", async route => {
    const response = await route.fetch({
      postData: { ...route.request().postDataJSON(), quoteHash: "0".repeat(64) },
    });
    expect(response.status()).toBe(409);
    await route.fulfill({ response });
  });
  await page.getByRole("checkbox", { name: confirm }).check();
  await action(page, approve).click();
  await expect(page.getByRole("alert").filter({ hasText: "حدّث المراجعة قبل الاعتماد" })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "سبب الإعفاء" })).toHaveValue(reason);
  await expect(page.getByRole("combobox", { name: "الباقة المعفاة" })).toHaveValue(monthly);
  await reviewWaiver(page);
  await expect(page.getByRole("checkbox", { name: confirm })).not.toBeChecked();
});

test("A saved waiver survives a lost response and reload, with no receipt and private owner notes", async ({ page }, info) => {
  test.setTimeout(75_000);
  await openWaiver(page);
  await reviewWaiver(page);
  const photos = async (stage: string) => {
    for (const width of [320, 390, 1440]) {
      await page.setViewportSize({ width, height: 1000 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
      await page.screenshot({ path: info.outputPath("free-training-" + stage + "-" + width + ".png"), fullPage: true, animations: "disabled" });
    }
  };
  await photos("quote");

  let committed: { periodId: string } | undefined;
  let sent = 0;
  let key: string | undefined;
  await page.route("**" + waiverApi + "/approve", async route => {
    sent++;
    key = route.request().headers()["idempotency-key"];
    const response = await route.fetch();
    expect(response.ok(), await response.text()).toBe(true);
    committed = await response.json();
    await route.abort("connectionreset");
  });
  await page.route("**" + attemptPath, async route => {
    expect(route.request().headers()["idempotency-key"]).toBe(key);
    await route.fulfill({ status: 503 });
  });
  await page.getByRole("checkbox", { name: confirm }).check();
  await action(page, approve).click();
  await expect(action(page, check)).toBeVisible();
  await page.reload();
  await expect(action(page, check)).toBeVisible();
  await page.unroute("**" + attemptPath);
  const readBack = page.waitForResponse(response => new URL(response.url()).pathname === attemptPath);
  await action(page, check).click();
  const response = await readBack;
  expect(response.ok()).toBe(true);
  expect(response.request().headers()["idempotency-key"]).toBe(key);
  expect(committed).toBeDefined();
  const result = await response.json();
  expect(result).toMatchObject({ periodId: committed?.periodId, sportEnrollmentId: enrollment, playerId: player, replay: true });
  expect(sent).toBe(1);

  await expect(page.getByRole("link", { name: periodLink })).toBeVisible();
  await photos("saved");
  await page.getByRole("link", { name: periodLink }).click();
  await expect(page.getByText("تدريب مجاني بإعفاء معتمد — لا يوجد تحصيل أو إيصال")).toBeVisible();
  await expect(page.getByText(reason, { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: "عرض الإيصال المحفوظ" })).toHaveCount(0);
  const ownerPeriod = await page.request.get("/api/v1/subscriptions/periods/" + result.periodId);
  expect(ownerPeriod.ok()).toBe(true);
  const period = await ownerPeriod.json();
  expect(period).toMatchObject({
    origin: "ApprovedWaiver", receiptId: null, sportEnrollmentId: enrollment, waiver: { reason },
  });
  expect(Number(period.priceSnapshot)).toBe(0);

  await guardianLogin(page, "01000000015");
  const guardianApi = "/api/v1/guardian/subscriptions/periods/" + result.periodId + "/adjustments";
  const own = await page.request.get(guardianApi);
  expect(own.ok()).toBe(true);
  expect(await own.json()).toMatchObject({ origin: "ApprovedWaiver" });
  expect(await own.text()).not.toContain(reason);
  await guardianLogin(page);
  expect((await page.request.get(guardianApi)).status()).toBe(404);
});
