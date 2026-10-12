import { expect, test } from "@playwright/test";

test("Cash merchandise sale posts stock and owned shift once, with precise review and a drill-down", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard"); page.on("dialog", d => d.accept());
  async function write(path: string, data: unknown, method = "POST") {
    const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
    const response = await page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": crypto.randomUUID() } });
    expect(response.ok(), await response.text()).toBe(true); return response.json();
  }
  const branchId = "31000000-0000-0000-0000-000000000001", sportId = "32000000-0000-0000-0000-000000000001", root = "/api/v1/finance/cash-registers";
  const productName = `طقم بيع نقدي اصطناعي ${Date.now()}`;
  const product = await write("/api/v1/content/catalog", { sportId, arabicName: productName, imageReference: "/demo-assets/catalog-football.svg", displayPrice: 100, currency: "EGP", discountPercentage: 10, displayOrder: 0, isActive: true, stockQuantity: 3 });
  const productId = typeof product === "string" ? product : product.id;
  const me = await page.request.get("/api/v1/me").then(r => r.json()); const options = await page.request.get(`${root}/options`).then(r => r.json());
  const member = options.members.find((m: { name: string }) => m.name === me.displayName); expect(member).toBeTruthy();
  const register = await write(root, { branchId, name: `صندوق متجر اصطناعي ${Date.now()}` });
  await write(`${root}/${register.id}/cashiers`, { membershipId: member.id, enabled: true }, "PUT"); const shift = await write(`${root}/${register.id}/open`, { openingAmount: "100.00" });
  await page.goto(`/dashboard/content/catalog/${productId}/sell`); await expect(page.getByRole("heading", { name: productName })).toBeVisible();
  await page.getByLabel("الفرع").selectOption(branchId); await page.getByLabel("الكمية").fill("2");
  await expect(page.getByLabel("طريقة الدفع")).toHaveValue(""); await page.getByLabel("طريقة الدفع").selectOption("Cash");
  const submit = page.getByRole("button", { name: "تأكيد التحصيل والبيع" }); await expect(submit).toBeDisabled();
  await page.getByLabel("وردية التحصيل").selectOption(shift.id);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 }); await page.getByRole("region", { name: "مراجعة تحصيل المنتج" }).scrollIntoViewIfNeeded();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`cash-products-${width}.png`), animations: "disabled" });
  }
  const saved = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/manage/product-sales")); await submit.click(); const response = await saved;
  expect(response.ok()).toBe(true); await expect(page.getByRole("heading", { name: "تم التحصيل وخصم المخزون" })).toBeVisible();
  expect(response.request().postDataJSON()).toMatchObject({ cashSessionId: shift.id, paymentMethod: "Cash", expectedAmount: "180.00" }); await expect(page.getByLabel("الفرع")).toBeDisabled();
  const replay = await page.request.fetch(response.url(), { method: "POST", data: response.request().postDataJSON(), headers: response.request().headers() }); expect(replay.ok()).toBe(true); const sale = await replay.json(); expect(sale.replay).toBe(true);
  const detail = await page.request.get(`${root}/sessions/${shift.id}`).then(r => r.json()); expect(detail.balance).toBe("280.00"); expect(detail.productSales).toHaveLength(1); expect(detail.receipts).toHaveLength(0); expect(detail.productSales[0].productSaleId).toBe(sale.id);
  const stored = await page.request.get(`/api/v1/content/catalog/${productId}`).then(r => r.json()); expect(stored.stockQuantity).toBe(1);
  await page.goto(`/dashboard/finance/cash-registers/sessions/${shift.id}`); await expect(page.getByRole("heading", { name: "مبيعات المنتجات النقدية" })).toBeVisible();
  await page.getByRole("link", { name: "تفاصيل البيع" }).click(); await expect(page.getByText(productName)).toBeVisible();
  const report = await page.request.get(`/api/v1/reports/financial?allDates=true&collectionType=Product&branchId=${branchId}`).then(r => r.json());
  expect(report.items.some((row: { collectionId: string }) => row.collectionId === sale.id)).toBe(true);
});
