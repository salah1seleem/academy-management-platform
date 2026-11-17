import { expect, test, type Page } from "@playwright/test";

const tabLabel = "أقسام إعدادات الأكاديمية";

async function expectSelectedVisible(page: Page, name: string) {
  const selected = page.getByRole("tab", { name, exact: true });
  await expect(selected).toHaveAttribute("aria-selected", "true");
  await expect.poll(() => selected.evaluate(element => {
    const bounds = element.closest('[role="tablist"]')!.getBoundingClientRect();
    const target = element.getBoundingClientRect();
    return target.left >= bounds.left - 1 && target.right <= bounds.right + 1;
  })).toBe(true);
  await expect(page.getByRole("tabpanel", { name, exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
}

test.beforeEach(async ({ page }) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
});

test("The finance deep link remains readable across viewport changes without moving the page or overriding manual tab scrolling", async ({ page }, info) => {
  const writes: string[] = [];
  page.on("request", request => { if (["POST", "PUT", "DELETE"].includes(request.method())) writes.push(request.url()); });
  await page.setViewportSize({ width: 320, height: 900 });
  await page.goto("/dashboard/settings?tab=finance&scope=synthetic-navigation");
  await expect(page.getByRole("tab", { name: "إعدادات متقدمة", exact: true })).toBeVisible();
  for (const width of [320, 390, 1440, 320]) {
    await page.setViewportSize({ width, height: 900 });
    if (width === 390) await page.reload();
    await expectSelectedVisible(page, "المالية والتحصيل");
    expect(await page.evaluate(() => scrollY)).toBe(0);
    await page.screenshot({ path: info.outputPath(`finance-tab-${width}.png`), fullPage: true, animations: "disabled" });
  }
  const bar = page.getByRole("tablist", { name: tabLabel });
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  const manual = await bar.evaluate(element => { element.scrollLeft = 0; return element.scrollLeft; });
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  expect(await bar.evaluate(element => element.scrollLeft)).toBe(manual);
  expect(writes).toEqual([]);
});

test("Late permissions reveal the end tab and RTL keyboard navigation preserves the route filters", async ({ page }, info) => {
  let release = () => {};
  const permissionsReady = new Promise<void>(resolve => { release = resolve; });
  await page.route("**/api/v1/my-permissions", async route => { await permissionsReady; await route.continue(); });
  await page.setViewportSize({ width: 320, height: 900 });
  try {
    await page.goto("/dashboard/settings?tab=advanced&branchId=synthetic-branch&search=synthetic");
    await expect(page.getByRole("heading", { name: "إعدادات الأكاديمية", exact: true })).toBeVisible();
    await expect(page.getByRole("tab", { name: "إعدادات متقدمة", exact: true })).toHaveCount(0);
  } finally {
    release();
  }
  await expectSelectedVisible(page, "إعدادات متقدمة");
  await page.reload();
  await expectSelectedVisible(page, "إعدادات متقدمة");
  const destinations = [
    ["Home", "بيانات الأكاديمية", "academy"],
    ["ArrowRight", "إعدادات متقدمة", "advanced"],
    ["ArrowLeft", "بيانات الأكاديمية", "academy"],
    ["End", "إعدادات متقدمة", "advanced"],
  ];
  await page.getByRole("tab", { name: "إعدادات متقدمة", exact: true }).focus();
  for (const [key, name, id] of destinations) {
    await page.keyboard.press(key);
    await expectSelectedVisible(page, name);
    await expect(page.getByRole("tab", { name, exact: true })).toBeFocused();
    const query = new URL(page.url()).searchParams;
    expect(query.get("tab")).toBe(id);
    expect(query.get("branchId")).toBe("synthetic-branch");
    expect(query.get("search")).toBe("synthetic");
  }
  await page.screenshot({ path: info.outputPath("advanced-tab-keyboard-320.png"), fullPage: true, animations: "disabled" });
});

test("A forbidden tab query falls back to an available section without mounting configuration controls", async ({ page }) => {
  const configurationReads: string[] = [];
  page.on("request", request => { if (request.url().includes("/configuration")) configurationReads.push(request.url()); });
  await page.route("**/api/v1/my-permissions", route => route.fulfill({ json: [] }));
  await page.goto("/dashboard/settings?tab=advanced");
  await expectSelectedVisible(page, "بيانات الأكاديمية");
  await expect(page.getByRole("tab", { name: "إعدادات متقدمة", exact: true })).toHaveCount(0);
  await expect(page.getByRole("tab", { name: "معايير التقييم", exact: true })).toHaveCount(0);
  expect(configurationReads).toEqual([]);
});

test("The second academy keeps its own settings identity when an unknown tab falls back", async ({ page, browser }, info) => {
  const firstBrand = await page.request.get("/api/v1/academy/experience").then(response => response.json());
  const other = await browser.newContext({ baseURL: info.project.use.baseURL });
  try {
    const csrf = await other.request.get("/api/v1/auth/csrf").then(response => response.json());
    expect((await other.request.post("/api/v1/auth/login", {
      headers: { "X-CSRF-TOKEN": csrf.token }, data: { email: "owner.future@example.test", password: "Demo-Only-123!" },
    })).status()).toBe(200);
    const otherPage = await other.newPage();
    const before = await other.request.get("/api/v1/academy/experience").then(response => response.json());
    expect(before.displayName).not.toBe(firstBrand.displayName);
    await otherPage.goto("/dashboard/settings?tab=unknown");
    await expectSelectedVisible(otherPage, "بيانات الأكاديمية");
    await expect(otherPage.getByRole("heading", { name: before.displayName, exact: true })).toBeVisible();
    expect(await other.request.get("/api/v1/academy/experience").then(response => response.json())).toEqual(before);
    expect(await page.request.get("/api/v1/academy/experience").then(response => response.json())).toEqual(firstBrand);
  } finally {
    await other.close();
  }
});
