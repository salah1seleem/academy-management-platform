import { expect, test } from "@playwright/test";

test("Owner creates a cashier shift, transfers conserved float and closes with explicit reconciliation", async ({ page }, info) => {
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const root = "/api/v1/finance/cash-registers";
  const options = await page.request.get(`${root}/options`).then(r => r.json());
  const me = await page.request.get("/api/v1/me").then(r => r.json());
  const member = options.members.find((m: { name: string }) => m.name === me.displayName) ?? options.members[0];
  expect(member).toBeTruthy();
  const collections = await page.request.get("/api/v1/finance/receivables").then(r => r.json());
  page.on("dialog", d => d.accept());
  await page.goto("/dashboard/finance/cash-registers");
  await expect(page.getByRole("heading", { name: "الصناديق والعهد", exact: true })).toBeVisible();
  const stamp = Date.now(); const names = [`عهدة مصدر اختبار ${stamp}`, `عهدة مستلم اختبار ${stamp}`];
  const sessions: string[] = [];
  for (const [i, name] of names.entries()) {
    const create = page.locator("details").filter({ has: page.locator("summary", { hasText: /^إضافة صندوق فرع$/ }) });
    if (!await create.evaluate(e => e.hasAttribute("open"))) await create.locator("summary").click();
    await create.getByLabel("اسم الصندوق", { exact: true }).fill(name);
    await create.getByRole("combobox", { name: "الفرع", exact: true }).selectOption(options.branches[0].id);
    await create.getByRole("button", { name: "إنشاء الصندوق" }).click();
    const card = page.getByRole("article", { name: `صندوق ${name}`, exact: true });
    await expect(card).toBeVisible(); await card.getByText("صلاحيات أمناء الصندوق", { exact: true }).click();
    await card.getByRole("combobox", { name: "أمين الصندوق", exact: true }).selectOption(member.id);
    await card.getByRole("button", { name: "حفظ تفويض الأمين" }).click();
    await expect(card.getByText("فتح وردية", { exact: true })).toBeVisible(); await card.getByText("فتح وردية", { exact: true }).click();
    await card.getByLabel("العهدة الافتتاحية", { exact: true }).fill(i === 0 ? "200.00" : "50.00");
    await card.getByRole("button", { name: "تأكيد فتح الوردية" }).click();
    await expect(card.getByText("وردية مفتوحة", { exact: true })).toBeVisible();
    const href = await card.getByRole("link", { name: "تفاصيل الوردية والمطابقة" }).getAttribute("href"); sessions.push(href!.split("/").at(-1)!);
  }
  const transfer = page.locator("details").filter({ has: page.locator("summary", { hasText: /تحويل عهدة بين صندوقين/ }) });
  await transfer.locator("summary").click();
  await transfer.getByRole("combobox", { name: "الصندوق المصدر", exact: true }).selectOption(sessions[0]);
  await transfer.getByRole("combobox", { name: "الصندوق المستلم", exact: true }).selectOption(sessions[1]);
  await transfer.getByLabel("المبلغ المحول", { exact: true }).fill("100.00");
  await transfer.getByRole("button", { name: "تأكيد التحويل", exact: true }).click();
  await expect.poll(async () => page.request.get(`${root}/sessions/${sessions[0]}`).then(r => r.json()).then(d => d.balance)).toBe("100.00");
  expect(await page.request.get(`${root}/sessions/${sessions[1]}`).then(r => r.json()).then(d => d.balance)).toBe("150.00");
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.getByRole("article", { name: `صندوق ${names[0]}`, exact: true }).scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath(`cash-register-${width}.png`) });
  }
  await page.goto(`/dashboard/finance/cash-registers/sessions/${sessions[0]}`);
  await page.getByLabel("المبلغ الموجود فعليًا", { exact: true }).fill("90.00");
  await page.getByRole("button", { name: "تثبيت المطابقة وإقفال الوردية" }).click();
  await page.waitForURL("**/dashboard/finance/cash-registers");
  await page.goto(`/dashboard/finance/cash-registers/sessions/${sessions[0]}`);
  await expect(page.getByText("فرق المطابقة", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "تثبيت المطابقة وإقفال الوردية" })).toHaveCount(0);
  const detail = await page.request.get(`${root}/sessions/${sessions[0]}`).then(r => r.json()); expect(detail.variance).toBe("-10.00"); expect(detail.movements).toHaveLength(1);
  // Opening float and transfer never become subscription income or receivables.
  const after = await page.request.get("/api/v1/finance/receivables").then(r => r.json()); expect(after.outstanding).toBe(collections.outstanding);
  await page.screenshot({ path: info.outputPath("cash-reconciled.png") });
});
