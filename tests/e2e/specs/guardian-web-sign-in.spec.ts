import { expect, test, type Page } from "@playwright/test";

const entry = "/guardian/sign-in";
const otp = "/api/v1/auth/guardian/otp";
const requestButton = "طلب رمز التحقق";

async function requestCode(page: Page, value: string, name = requestButton) {
  if (name === requestButton) await page.getByLabel("رقم الهاتف").fill(value);
  const pending = page.waitForResponse(response => response.url().endsWith(otp + "/request"));
  await page.getByRole("button", { name, exact: true }).click();
  const response = await pending;
  expect(response.status()).toBe(202);
  expect(response.request().postDataJSON()).toEqual({ phoneNumber: "+201000000001" });
  return (await response.json()).challengeId as string;
}

test("GS-01 GS-02: guardian entry from the website and a direct visit", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("link", { name: "دخول ولي الأمر", exact: true }).click({ timeout: 15_000 });
  await expect(page).toHaveURL(entry);
  await expect(page.getByRole("heading", { name: "دخول ولي الأمر" })).toBeVisible();
  await expect(page.getByLabel("رقم الهاتف")).toBeVisible();
  await page.getByRole("link", { name: "دخول المالك والإداري" }).click();
  await expect(page.getByLabel("كلمة المرور")).toBeVisible();
  await page.goto("/guardian");
  await expect(page).toHaveURL(entry);
  await expect(page.getByRole("heading", { name: "الأبناء", exact: true })).toHaveCount(0);
  expect((await page.request.get("/api/v1/guardian/home")).status()).toBe(401);
});

test("GS-03 GS-04 GS-05 GS-07 GS-08 GS-09 GS-18 GS-19 GS-22: local phone, retry, household and logout", async ({ page }) => {
  await page.goto(entry);
  const phone = page.getByLabel("رقم الهاتف");
  const send = page.getByRole("button", { name: requestButton, exact: true });
  let writes = 0;
  page.on("request", request => {
    if (request.method() === "POST" && request.url().includes(otp)) writes++;
  });
  for (const invalid of ["", "abc", "0100000", "010000000011", "01300000001"]) {
    await phone.fill(invalid);
    await send.click();
    await expect(phone).toBeFocused();
    await expect(phone).toHaveAttribute("aria-invalid", "true");
    await expect(phone).toHaveValue(invalid);
  }
  expect(writes).toBe(0);
  await phone.fill("٠١٠٠٠٠٠٠٠٠١");
  const requested = page.waitForResponse(response => response.url().endsWith(otp + "/request"));
  await send.click();
  const first = await requested;
  expect(first.status()).toBe(202);
  expect(first.request().postDataJSON()).toEqual({ phoneNumber: "+201000000001" });
  const code = page.getByLabel("رمز التحقق", { exact: true });
  const verify = page.getByRole("button", { name: "تأكيد الدخول" });
  await expect(code).toBeFocused();
  for (const invalid of ["", "123", "123ab6"]) {
    await code.fill(invalid);
    await verify.click();
    await expect(code).toHaveAccessibleDescription("اكتب رمز التحقق من ٦ أرقام.");
    await expect(code).toBeFocused();
  }
  expect(writes).toBe(1);
  await code.fill("111111");
  await verify.click();
  await expect(page.locator("main").getByRole("alert")).toContainText("رمز التحقق غير صحيح أو انتهت صلاحيته");
  expect((await page.request.get("/api/v1/me")).status()).toBe(401);
  await code.fill("٢٤٦٨١٠");
  await code.press("Enter");
  await expect(page).toHaveURL("/guardian");
  await expect(page.getByRole("heading", { name: "الأبناء", exact: true })).toBeVisible();
  const me = await (await page.request.get("/api/v1/me")).json();
  expect(me.role).toBe("Guardian");
  const home = await (await page.request.get("/api/v1/guardian/home")).json();
  expect(home.children.length).toBeGreaterThan(0);
  for (const child of home.children) {
    await expect(page.locator(`#children a[href="/guardian/children/${child.id}"]`).first()).toBeVisible();
  }
  const profiles = "/api/v1/guardian/children";
  expect((await page.request.get(`${profiles}/${home.children[0].id}/profile`)).status()).toBe(200);
  for (const id of ["36000000-0000-0000-0000-000000000003", "36000000-0000-0000-0000-000000000004"]) {
    expect((await page.request.get(`${profiles}/${id}/profile`)).status()).toBe(404);
  }
  const { token } = await (await page.request.get("/api/v1/auth/csrf")).json();
  const foreignAcademy = await page.request.post("/api/v1/session/academy", {
    headers: { "X-CSRF-TOKEN": token }, data: { academyId: "20000000-0000-0000-0000-000000000002" },
  });
  expect(foreignAcademy.status()).toBe(403);
  expect(await (await page.request.get("/api/v1/me")).json()).toEqual(me);
  expect((await page.request.get("/api/v1/reports/owner-summary")).status()).toBe(403);
  const childRoute = `/guardian/children/${home.children[0].id}`;
  const childHeading = page.getByRole("heading", { name: home.children[0].arabicName, exact: true });
  await page.goto(childRoute);
  await expect(childHeading).toBeVisible();
  await page.goto("/guardian");
  const logout = page.getByRole("button", { name: "تسجيل الخروج" });
  await page.route("**/api/v1/auth/logout", route => route.fulfill({ status: 503 }), { times: 1 });
  await logout.click();
  await expect(page.locator("main").getByRole("alert").filter({ hasText: "تعذر تسجيل الخروج" })).toBeVisible();
  expect((await page.request.get("/api/v1/me")).status()).toBe(200);
  await logout.click();
  await expect(page).toHaveURL(entry);
  await page.goBack();
  await expect(page).toHaveURL(childRoute);
  await expect(childHeading).toHaveCount(0);
  await page.goto("/guardian");
  await expect(page).toHaveURL(entry);
  await expect(page.getByRole("heading", { name: "الأبناء", exact: true })).toHaveCount(0);
  expect((await page.request.get("/api/v1/guardian/home")).status()).toBe(401);
  expect((await page.request.get(`${profiles}/${home.children[0].id}/profile`)).status()).toBe(401);
});

test("GS-06 GS-10 GS-21: edit the international number and renew a challenge", async ({ page }) => {
  await page.goto(entry);
  const first = await requestCode(page, "+201000000001");
  const code = page.getByLabel("رمز التحقق", { exact: true });
  await code.fill("111111");
  await page.getByRole("button", { name: "تأكيد الدخول" }).click();
  await expect(page.locator("main").getByRole("alert")).toContainText("رمز التحقق غير صحيح");
  const renewed = await requestCode(page, "", "طلب رمز جديد");
  expect(renewed).not.toBe(first);
  await expect(code).toBeEmpty();
  await expect(code).toBeFocused();
  await page.getByRole("button", { name: "تعديل الرقم" }).click();
  const phone = page.getByLabel("رقم الهاتف");
  await expect(phone).toBeFocused();
  await expect(phone).toHaveValue("+201000000001");
  expect(await requestCode(page, "+2001000000001")).not.toBe(renewed);
});

test("GS-14: signed-in owner keeps the original account until explicitly signing out", async ({ page }) => {
  const { token } = await (await page.request.get("/api/v1/auth/csrf")).json();
  const login = await page.request.post("/api/v1/auth/login", {
    headers: { "X-CSRF-TOKEN": token },
    data: { email: "owner.nogoom@example.test", password: "Demo-Only-123!" },
  });
  expect(login.ok()).toBe(true);
  const before = await (await page.request.get("/api/v1/me")).json();
  await page.goto(entry);
  const trigger = async () => {
    await page.getByLabel("رقم الهاتف").fill("01000000001");
    await page.getByRole("button", { name: requestButton }).click();
    await expect(page.getByRole("heading", { name: "حساب مفتوح بالفعل" })).toBeFocused();
  };
  await trigger();
  expect(await (await page.request.get("/api/v1/me")).json()).toEqual(before);
  await page.getByRole("link", { name: "متابعة بالحساب الحالي" }).click();
  await expect(page).toHaveURL("/dashboard");
  expect(await (await page.request.get("/api/v1/me")).json()).toEqual(before);
  await page.goto(entry);
  await trigger();
  await page.getByRole("button", { name: "تسجيل الخروج لتبديل الحساب" }).click();
  await expect(page.getByLabel("رقم الهاتف")).toBeVisible();
  expect((await page.request.get("/api/v1/me")).status()).toBe(401);
  await requestCode(page, "01000000001");
});

test("GS-15 GS-16 GS-17 GS-20: availability, network retries, and compact RTL layouts", async ({ page }, info) => {
  const availability = `**${otp}/availability`;
  await page.route(availability, route => route.fulfill({ status: 503 }), { times: 1 });
  await page.goto(entry);
  await expect(page.locator("main").getByRole("alert")).toHaveText("تعذر تحميل طريقة الدخول.");
  await page.getByRole("button", { name: "إعادة المحاولة" }).click();
  const phone = page.getByLabel("رقم الهاتف");
  await expect(phone).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  await expect(phone).toHaveAttribute("dir", "ltr");
  await expect(phone).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(page.getByRole("button", { name: requestButton })).toBeFocused();
  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 844 });
    const send = page.getByRole("button", { name: requestButton });
    expect((await send.boundingBox())?.height).toBeGreaterThanOrEqual(44);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    if (width !== 320) await page.screenshot({ path: info.outputPath(`phone-${width}.png`) });
  }
  await page.setViewportSize({ width: 390, height: 844 });
  const send = page.getByRole("button", { name: requestButton });
  let release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  await page.route(`**${otp}/request`, async route => {
    await held;
    await route.fulfill({ status: 503 });
  }, { times: 1 });
  await phone.fill("01000000001");
  await send.click();
  try {
    await expect(page.getByRole("button", { name: "جارٍ التحقق…" })).toBeDisabled();
    await expect(phone).toHaveAttribute("readonly", "");
  } finally {
    release();
  }
  await expect(page.locator("main").getByRole("alert")).toContainText("تعذر طلب رمز التحقق");
  await expect(phone).toHaveValue("01000000001");
  await requestCode(page, "01000000001");
  const code = page.getByLabel("رمز التحقق", { exact: true });
  await expect(code).toHaveAttribute("dir", "ltr");
  const confirm = page.getByRole("button", { name: "تأكيد الدخول" });
  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 844 });
    expect((await confirm.boundingBox())?.height).toBeGreaterThanOrEqual(44);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    if (width !== 320) await page.screenshot({ path: info.outputPath(`code-${width}.png`) });
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.route(`**${otp}/verify`, route => route.fulfill({ status: 503 }), { times: 1 });
  await code.fill("246810");
  await confirm.click();
  await expect(page.locator("main").getByRole("alert")).toContainText("تعذر التحقق الآن");
  await expect(code).toHaveValue("246810");
  await expect(confirm).toBeEnabled();
  await confirm.click();
  await expect(page).toHaveURL("/guardian");
  expect((await page.request.post(otp + "/request", { data: { phoneNumber: "01000000001" } })).status()).toBe(400);
  await page.route(availability, route => route.fulfill({ json: { enabled: false } }), { times: 1 });
  await page.goto(entry);
  await expect(page.getByText("استخدم تطبيق الأكاديمية لتسجيل الدخول برقم الهاتف.")).toBeVisible();
  await expect(phone).toHaveCount(0);
  await expect(page.getByRole("link", { name: "دخول المالك والإداري" })).toBeVisible();
  await page.screenshot({ path: info.outputPath("unavailable-390.png") });
});
