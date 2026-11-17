import { expect, test } from "@playwright/test";
import { guardianLogin } from "../support/auth";

const endpoint = "/api/v1/payment-settings";
const formName = "إعداد حسابات التحويل";
const saveName = "حفظ حسابات التحويل";
const savedMessage = "تم حفظ حسابات التحويل وتأكيد قراءتها.";

test.beforeEach(async ({ page }) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard$/, { timeout: 20_000 });
});

test("Transfer settings keep the owner form readable and editable after validation rejection", async ({ page }, info) => {
  let writes = 0;
  let reject = true;
  await page.route(`**${endpoint}`, async route => {
    if (route.request().method() !== "PUT") return route.continue();
    writes++;
    expect(reject).toBe(true);
    await route.fulfill({ status: 422, json: { message: "راجع حساب التحويل المدخل." } });
  });
  await page.goto("/dashboard/settings?tab=finance");
  const form = page.getByRole("form", { name: formName });
  const account = form.getByRole("textbox", { name: "حساب إنستا باي" });
  await expect(account).toBeVisible();
  await form.getByRole("checkbox").check();
  await account.fill("");
  await form.getByRole("textbox", { name: "رقم المحفظة" }).fill("");
  await form.getByRole("button", { name: saveName }).click();
  await expect(page.getByRole("tabpanel", { name: "المالية والتحصيل" }).getByRole("alert")).toContainText("أدخل حساب إنستا باي أو رقم المحفظة");
  expect(writes).toBe(0);

  await account.fill("synthetic-transfer@example.test");
  await form.getByRole("button", { name: saveName }).click();
  await expect(page.getByRole("tabpanel", { name: "المالية والتحصيل" }).getByRole("alert")).toHaveText("راجع حساب التحويل المدخل.");
  await expect(account).toBeEnabled();
  await expect(account).toHaveValue("synthetic-transfer@example.test");
  await expect(page.getByRole("button", { name: "إعادة التحقق من الحفظ" })).toHaveCount(0);
  expect(writes).toBe(1);
  reject = false;

  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    const checkbox = form.getByRole("checkbox");
    await checkbox.focus();
    const checked = await checkbox.isChecked();
    await page.keyboard.press("Space");
    expect(await checkbox.isChecked()).toBe(!checked);
    await expect(checkbox).toBeFocused();
    expect(await checkbox.locator("..").evaluate(element => element.getBoundingClientRect().height)).toBeGreaterThanOrEqual(44);
    expect(await form.locator("fieldset").evaluate(element => getComputedStyle(element).borderTopWidth)).toBe("0px");
    expect(await form.evaluate(element => getComputedStyle(element).borderTopWidth)).toBe("0px");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`transfer-settings-${width}.png`), fullPage: true, animations: "disabled" });
  }
  expect(writes).toBe(1);
});

test("Transfer settings reject broken reads and recover without technical messages or invented defaults", async ({ page }) => {
  let broken = true;
  await page.route(`**${endpoint}`, async route => broken
    ? route.fulfill({ status: 200, json: { configured: false, walletNumber: 123 } })
    : route.continue());
  await page.goto("/dashboard/settings?tab=finance");
  await expect(page.getByRole("tabpanel", { name: "المالية والتحصيل" }).getByRole("alert")).toHaveText("تعذر تحميل إعدادات التحويل.");
  await expect(page.getByRole("form", { name: formName })).toHaveCount(0);
  broken = false;
  await page.getByRole("button", { name: "إعادة تحميل إعدادات التحويل" }).click();
  await expect(page.getByRole("textbox", { name: "حساب إنستا باي" })).toBeVisible();
  await page.route(`**${endpoint}`, route => route.abort());
  await page.reload();
  await expect(page.getByRole("tabpanel", { name: "المالية والتحصيل" }).getByRole("alert")).toHaveText("تعذر تحميل إعدادات التحويل.");
  await expect(page.getByText(/Failed to fetch|NetworkError/)).toHaveCount(0);
});

test("A revoked transfer write removes the form and retains the Arabic access message", async ({ page }) => {
  await page.goto("/dashboard/settings?tab=finance");
  const form = page.getByRole("form", { name: formName });
  await expect(form.getByRole("textbox", { name: "حساب إنستا باي" })).toBeVisible();
  await form.getByRole("checkbox").uncheck();
  await page.route(`**${endpoint}`, route => route.request().method() === "PUT"
    ? route.fulfill({ status: 403, json: { message: "إعدادات التحويل غير متاحة لحسابك." } })
    : route.continue());
  await form.getByRole("button", { name: saveName }).click();
  await expect(page.getByRole("tabpanel", { name: "المالية والتحصيل" }).getByRole("alert")).toHaveText("إعدادات التحويل غير متاحة لحسابك.");
  await expect(form).toHaveCount(0);
  await expect(page.getByRole("button", { name: "إعادة التحقق من الحفظ" })).toHaveCount(0);
});

test("The family reads confirmed transfer settings while a lost save response is recovered without another write", async ({ page, browser }, info) => {
  test.setTimeout(90_000);
  const originalResponse = await page.request.get(endpoint);
  expect(originalResponse.status()).toBe(200);
  const original = await originalResponse.json();
  const before = await page.request.get("/api/v1/subscriptions/collections").then(response => response.json());
  const familyContext = await browser.newContext({ baseURL: info.project.use.baseURL });
  const otherContext = await browser.newContext({ baseURL: info.project.use.baseURL });
  let writes = 0;
  let loseResponse = true;
  try {
    const otherToken = await otherContext.request.get("/api/v1/auth/csrf").then(response => response.json());
    const otherLogin = await otherContext.request.post("/api/v1/auth/login", {
      headers: { "X-CSRF-TOKEN": otherToken.token }, data: { email: "owner.future@example.test", password: "Demo-Only-123!" },
    });
    expect(otherLogin.status()).toBe(200);
    const otherBefore = await otherContext.request.get(endpoint).then(response => response.json());
    const family = await familyContext.newPage();
    await guardianLogin(family);
    const familyReceiptsBefore = await family.request.get("/api/v1/guardian/subscriptions/receipts").then(response => response.json());

    await page.goto("/dashboard/settings?tab=finance");
    const form = page.getByRole("form", { name: formName });
    await form.getByRole("textbox", { name: "حساب إنستا باي" }).fill("  qa-transfer@example.test  ");
    await form.getByRole("textbox", { name: "رقم المحفظة" }).fill("");
    await form.getByRole("textbox", { name: "اسم المستلم" }).fill("  مستلم اختبار اصطناعي  ");
    await form.getByRole("checkbox").check();
    await page.route(`**${endpoint}`, async route => {
      if (route.request().method() !== "PUT") return route.continue();
      writes++;
      const response = await route.fetch();
      expect(response.status()).toBe(204);
      if (loseResponse) return route.abort();
      await route.fulfill({ response });
    });
    await form.getByRole("button", { name: saveName }).click();
    const retry = page.getByRole("button", { name: "إعادة التحقق من الحفظ" });
    await expect(retry).toBeVisible();
    await expect(form.getByRole("textbox", { name: "حساب إنستا باي" })).toBeDisabled();
    await expect(page.getByText(savedMessage, { exact: true })).toHaveCount(0);
    await retry.click();
    await expect(page.getByText(savedMessage, { exact: true })).toBeVisible();
    expect(writes).toBe(1);
    const enabled = await family.request.get(endpoint).then(response => response.json());
    expect(enabled).toMatchObject({ instaPayAddress: "qa-transfer@example.test", recipientName: "مستلم اختبار اصطناعي", isActive: true, configured: true });

    loseResponse = false;
    await form.getByRole("checkbox").uncheck();
    await form.getByRole("button", { name: saveName }).click();
    await expect(page.getByText(savedMessage, { exact: true })).toBeVisible();
    await expect(form.getByRole("checkbox")).not.toBeChecked();
    expect(writes).toBe(2);
    expect(await family.request.get(endpoint).then(response => response.json())).toMatchObject({ instaPayAddress: "qa-transfer@example.test", isActive: false, configured: false });
    expect(await otherContext.request.get(endpoint).then(response => response.json())).toEqual(otherBefore);
    expect(await page.request.get("/api/v1/subscriptions/collections").then(response => response.json())).toEqual(before);
    expect(await family.request.get("/api/v1/guardian/subscriptions/receipts").then(response => response.json())).toEqual(familyReceiptsBefore);
    const familyToken = await family.request.get("/api/v1/auth/csrf").then(response => response.json());
    expect((await family.request.put(endpoint, { headers: { "X-CSRF-TOKEN": familyToken.token }, data: original })).status()).toBe(403);
  } finally {
    await page.unroute(`**${endpoint}`);
    const csrf = await page.request.get("/api/v1/auth/csrf").then(response => response.json());
    expect((await page.request.put(endpoint, { headers: { "X-CSRF-TOKEN": csrf.token }, data: original })).status()).toBe(204);
    expect(await page.request.get(endpoint).then(response => response.json())).toEqual(original);
    await familyContext.close();
    await otherContext.close();
  }
});
