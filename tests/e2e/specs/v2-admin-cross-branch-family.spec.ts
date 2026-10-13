import { expect, test } from "@playwright/test";

test("Branch Admin collects a two-branch family once and recovers a lost response after reload", async ({ page }, info) => {
  const root = "/api/v1/finance/cash-registers";
  const collectingBranch = "31000000-0000-0000-0000-000000000002";
  async function login(email: string) {
    await page.context().clearCookies();
    await page.goto("/login");
    await page.getByLabel("البريد الإلكتروني").fill(email);
    await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
    await page.getByRole("button", { name: "دخول آمن" }).click();
    await page.waitForURL("**/dashboard**");
  }
  async function write(path: string, data: unknown, method = "POST") {
    const { token } = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
    const response = await page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": token, "Idempotency-Key": crypto.randomUUID() } });
    expect(response.ok(), await response.text()).toBe(true);
    return response.status() === 204 ? null : response.json();
  }
  await login("owner.nogoom@example.test");
  const email = `family.cross.${Date.now()}@example.test`;
  const member = await write("/api/v1/manage/staff", { name: "إداري أسرة فروع اصطناعي", email, phone: `010${Date.now().toString().slice(-8)}`, temporaryPassword: "Demo-Only-123!", role: "AcademyAdmin", jobTitle: "تحصيل نقدي" });
  const access = await page.request.get(`/api/v1/manage/staff/${member.id}/access`).then(r => r.json());
  await write(`/api/v1/manage/staff/${member.id}/access`, { allBranches: false, branches: [collectingBranch], actions: access.modules.map((module: string) => ({ module, read: ["finance", "players", "subscriptions", "payments"].includes(module), add: ["finance", "subscriptions", "payments"].includes(module), edit: false, delete: false })) }, "PUT");
  const register = await write(root, { branchId: collectingBranch, name: `أسرة فروع ${Date.now()}` });
  await write(`${root}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT");
  await login(email);
  const shift = await write(`${root}/${register.id}/open`, { openingAmount: "0" });
  expect((await page.request.get("/api/v1/people/players/84000019-0000-0000-0001-000000000001")).status()).toBe(404);
  const familyPage = `/dashboard/finance/cash-registers/sessions/${shift.id}/family`;
  await page.goto(familyPage);
  await page.getByRole("checkbox", { name: "تحصيل أسرة لديها أبناء في فروع أخرى" }).check();
  await page.getByLabel("اسم ولي الأمر أو رقم هاتفه").fill("01000000019");
  await page.getByRole("button", { name: "البحث عن الأسرة" }).click();
  await page.getByRole("button", { name: "اختيار أسرة قبض فروع اصطناعية" }).click();
  const checkboxes = page.getByRole("checkbox", { name: /تجديد .* قبض الفروع/ });
  await expect(checkboxes).toHaveCount(2);
  const labels = ["آدم قبض الفروع الاصطناعي", "لينا قبض الفروع الاصطناعية"];
  for (const player of labels) {
    await page.getByRole("checkbox", { name: `تجديد ${player} · كرة القدم` }).check();
    await page.getByRole("combobox", { name: `الباقة لـ${player} · كرة القدم` }).selectOption("51000000-0000-0000-0000-000000000001");
  }
  await page.getByRole("button", { name: "مراجعة إجمالي 2 تسجيل" }).click();
  await expect(page.getByRole("region", { name: "تحصيل الأسرة النقدي" })).toBeVisible();
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`family-cross-${width}.png`), animations: "disabled" });
  }
  let sent = 0;
  await page.route("**/cross-branch/families/collect", async route => {
    sent++;
    const response = await route.fetch();
    expect(response.ok(), await response.text()).toBe(true);
    await route.abort("failed");
  });
  await page.getByRole("checkbox", { name: "استلمت إجمالي الأسرة نقدًا وراجعت الباقات" }).check();
  await page.getByRole("button", { name: "تحصيل وتجديد كل التسجيلات" }).click();
  await expect(page.getByText("تم تحصيل وتجديد الأسرة", { exact: true })).toBeVisible();
  expect(sent).toBe(1);
  await page.reload();
  const detail = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json());
  expect(detail.receipts).toHaveLength(2);
  for (const line of detail.receipts) {
    const receipt = await page.request.get(`/api/v1/subscriptions/receipts/${line.receiptId}`).then(r => r.json());
    expect(receipt.collectionBranch).toBe("فرع التجمع الخامس");
  }
  expect((await page.request.get("/api/v1/people/players/84000019-0000-0000-0001-000000000001")).status()).toBe(404);
});
