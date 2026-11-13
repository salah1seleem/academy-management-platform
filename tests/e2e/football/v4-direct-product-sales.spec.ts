import { expect, test } from "@playwright/test";

const sales = "/api/v1/manage/product-sales";
const cash = "/api/v1/finance/cash-registers";
const catalog = "/api/v1/content/catalog";

test("Counter sales recover once and reconcile stock, the shift, and product income", async ({ page }, info) => {
  test.setTimeout(120_000);
  await page.goto("/login");
  await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click();
  await page.waitForURL("**/dashboard");

  async function read(path: string) {
    const response = await page.request.get(path);
    expect(response.ok(), await response.text()).toBe(true);
    return response.json();
  }
  const csrf = (await read("/api/v1/auth/csrf")).token;
  async function request(path: string, data: unknown, method = "POST", key: string = crypto.randomUUID()) {
    return page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": csrf, "Idempotency-Key": key } });
  }
  async function write(path: string, data: unknown, method = "POST") {
    const response = await request(path, data, method);
    expect(response.ok(), await response.text()).toBe(true);
    return response.json();
  }
  const config = "/api/v1/manage/configuration";
  const original = (await read(config)).versions.find((version: { state: string }) => version.state === "Active").settings;
  async function activate(settings: unknown) {
    const draft = await write(config + "/drafts", settings);
    for (const target of ["Validated", "Staged", "Active"]) {
      const current = (await read(config)).versions.find((version: { id: string }) => version.id === draft.id);
      await write(`${config}/${draft.id}/transition`, { target, version: current.version });
    }
  }

  try {
    if (!original.cashEnabled) await activate({ ...original, cashEnabled: true });
    const options = await read(sales + "/options");
    const [branch, other] = options.branches;
    expect(other?.id).toBeTruthy();
    const me = await read("/api/v1/me");
    const products = await read(catalog);
    const sportId = (Array.isArray(products) ? products : products.items)[0].sportId;
    const stamp = crypto.randomUUID().slice(0, 6);
    const productName = `طقم بيع اختبار ${stamp}`;
    const variantName = `طقم مقاسات اختبار ${stamp}`;
    async function createProduct(arabicName: string) {
      const saved = await write(catalog, {
        sportId, arabicName, imageReference: "/demo-assets/catalog-football.svg", displayPrice: 100,
        discountPercentage: 10, currency: "EGP", displayOrder: 0, isActive: true, stockQuantity: 3,
      });
      return typeof saved === "string" ? saved : saved.id;
    }
    const productId = await createProduct(productName);
    const getStock = () => read(`${catalog}/${productId}/inventory`);
    const registerName = `صندوق بيع اختبار ${stamp}`;
    const register = await write(cash, { branchId: branch.id, name: registerName });
    await write(`${cash}/${register.id}/cashiers`, { membershipId: me.membershipId, enabled: true }, "PUT");
    const open = () => write(`${cash}/${register.id}/open`, { openingAmount: "100.00" });
    const close = (session: string, countedAmount: string) => write(`${cash}/sessions/${session}/close`, { countedAmount });
    const range = `allDates=true&branchId=${branch.id}`;
    const otherRange = `allDates=true&branchId=${other.id}`;
    const getFinancial = (filter = range, type = "Product") => read(`/api/v1/reports/financial?${filter}&collectionType=${type}`);
    const getOperating = () => read(`/api/v1/reports/operating-statement?${range}`);
    const [productBefore, subscriptionBefore, otherBefore, operatingBefore] = await Promise.all([
      getFinancial(), getFinancial(range, "Subscription"), getFinancial(otherRange), getOperating(),
    ]);

    const sellPath = (id: string) => `/dashboard/content/catalog/${id}/sell`;
    const review = page.getByRole("button", { name: "مراجعة التحصيل والبيع" });
    const submit = page.getByRole("button", { name: "تسجيل البيع", exact: true });
    const verify = page.getByRole("button", { name: "التحقق من تسجيل البيع" });
    const quantity = page.getByLabel("الكمية", { exact: true });
    await page.goto(sellPath(productId));
    await expect(page.getByRole("heading", { name: productName })).toBeVisible();
    await page.getByLabel("الفرع").selectOption(branch.id);
    await page.getByLabel("طريقة الدفع").selectOption("Online");
    for (const invalid of ["0", "1.5", "4"]) {
      await quantity.fill(invalid);
      await expect(page.getByText("أدخل كمية صحيحة من 1 إلى 3.")).toBeVisible();
      await expect(review).toHaveCount(0);
    }
    const invalid = await page.request.get(`${sales}/quote?productId=${productId}&branchId=${branch.id}&quantity=0`);
    expect(invalid.status()).toBe(422);
    await quantity.fill("2");
    await expect(page.getByText("الإجمالي 180 جنيه")).toBeVisible();
    await expect(page.getByRole("link", { name: "فتح الصناديق والورديات" })).toHaveCount(0);
    await review.click();
    await expect(page.getByRole("dialog")).toContainText(`${productName} · ${branch.arabicName} · الكمية 2`);
    await expect(page.getByRole("dialog")).toContainText("تحويل إلكتروني مستلم");
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(review).toBeFocused();
    expect((await getStock()).stockQuantity).toBe(3);
    expect(await getFinancial()).toEqual(productBefore);

    await page.getByLabel("طريقة الدفع").selectOption("Cash");
    await expect(review).toBeDisabled();
    await expect(page.getByRole("link", { name: "فتح الصناديق والورديات" })).toHaveAttribute("href", "/dashboard/finance/cash-registers");
    const old = await open();
    const refresh = page.getByRole("button", { name: "تحديث المبلغ والورديات" });
    await refresh.click();
    await page.getByLabel("وردية التحصيل").selectOption(old.id);
    await review.click();
    await close(old.id, "100.00");
    await submit.click();
    await expect(page.getByRole("alert").filter({ hasText: "الوردية مقفلة" })).toBeVisible();
    await expect(review).toHaveCount(0);
    expect((await getStock()).stockQuantity).toBe(3);
    const shift = await open();
    await refresh.click();
    await page.getByLabel("وردية التحصيل").selectOption(shift.id);
    await review.click();
    await expect(page.getByRole("dialog")).toContainText(registerName);
    const cancelBackground = await page.getByRole("button", { name: "إلغاء", exact: true }).evaluate(element => getComputedStyle(element).backgroundColor);
    await expect(submit).not.toHaveCSS("background-color", cancelBackground);
    await page.getByRole("dialog").screenshot({ path: info.outputPath("product-confirm-390.png"), animations: "disabled" });
    let saved: { id: string } | undefined;
    let command: { key: string; body: Record<string, unknown> } | undefined;
    await page.route(`**${sales}`, async route => {
      command = { key: route.request().headers()["idempotency-key"], body: route.request().postDataJSON() };
      const response = await route.fetch();
      expect(response.ok(), await response.text()).toBe(true);
      saved = await response.json();
      await route.abort("failed");
    }, { times: 1 });
    await submit.click();
    await expect(page.getByText("نتيجة البيع بانتظار التحقق")).toBeVisible();
    await expect(page.getByRole("alert").filter({ hasText: "تعذر تأكيد تسجيل البيع" })).toBeVisible();
    await expect(quantity).toBeDisabled();
    await expect(page.getByRole("button", { name: "إعادة إرسال الطلب نفسه" })).toHaveCount(0);
    expect((await getStock()).stockQuantity).toBe(1);
    expect(command?.body).toMatchObject({ quantity: 2, expectedAmount: "180.00", cashSessionId: shift.id, paymentMethod: "Cash" });
    await verify.click();
    await expect(page.getByRole("heading", { name: "تم التحصيل وخصم المخزون" })).toBeVisible();
    const saleId = saved!.id;
    await page.route(`**${sales}/${saleId}`, route => route.fulfill({ status: 503, json: {} }), { times: 1 });
    await page.getByRole("link", { name: "تفاصيل العملية" }).click();
    await expect(page.getByRole("alert").filter({ hasText: "تعذر تحميل تفاصيل الطلب" })).toBeVisible();
    await page.getByRole("button", { name: "إعادة تحميل التفاصيل" }).click();
    await expect(page.getByRole("heading", { name: "تفاصيل بيع منتج" })).toBeVisible();
    for (const value of [productName, branch.arabicName, "نقدي", "90 جنيه", "180 جنيه", "تم التحصيل"]) {
      await expect(page.getByText(value, { exact: true })).toBeVisible();
    }
    for (const width of [320, 390, 1440]) {
      await page.setViewportSize({ width, height: 844 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
      await page.screenshot({ path: info.outputPath(`saved-product-${width}.png`), animations: "disabled" });
    }
    const detailPath = `/dashboard/product-sales/${saleId}`;
    const productPath = `/dashboard/content/catalog/${productId}`;
    await expect(page).toHaveURL(detailPath);
    await page.getByRole("link", { name: "تفاصيل المنتج" }).click();
    await expect(page).toHaveURL(productPath);
    await expect(page.getByRole("heading", { name: productName })).toBeVisible();
    await page.goBack({ waitUntil: "domcontentloaded" });
    await expect(page).toHaveURL(detailPath);
    await page.getByRole("link", { name: "عرض وردية التحصيل" }).click({ timeout: 10000 });
    await expect(page.getByRole("heading", { name: "مبيعات المنتجات النقدية" })).toBeVisible();
    await page.getByRole("link", { name: "تفاصيل البيع" }).click();
    await expect(page.getByRole("heading", { name: "تفاصيل بيع منتج" })).toBeVisible();

    const sessionPath = `${cash}/sessions/${shift.id}`;
    const ledger = await read(sessionPath);
    expect(ledger.balance).toBe("280.00");
    expect(ledger.productSales).toHaveLength(1);
    expect(ledger.productSales[0]).toMatchObject({ productSaleId: saleId, amount: "180.00" });
    expect(ledger.receipts).toHaveLength(0);
    const productAfter = await getFinancial();
    expect(productAfter.totalAmount - productBefore.totalAmount).toBe(180);
    expect(productAfter.items.filter((item: { collectionId: string }) => item.collectionId === saleId)).toHaveLength(1);
    expect(await getFinancial(range, "Subscription")).toEqual(subscriptionBefore);
    expect(await getFinancial(otherRange)).toEqual(otherBefore);
    const operatingAfter = await getOperating();
    const productIncome = (r: { income: { key: string; amount: number }[] }) => r.income.find(line => line.key === "product")!.amount;
    expect(productIncome(operatingAfter) - productIncome(operatingBefore)).toBe(180);
    expect(operatingAfter.surplus - operatingBefore.surplus).toBeCloseTo(180, 2);
    expect(operatingAfter.totalExpenses).toBe(operatingBefore.totalExpenses);

    await close(shift.id, "280.00");
    const replay = await request(sales, command!.body, "POST", command!.key);
    expect(await replay.json()).toMatchObject({ id: saleId, replay: true });
    const changed = await request(sales, { ...command!.body, quantity: 1, expectedAmount: "90.00" }, "POST", command!.key);
    expect(changed.status()).toBe(409);
    expect((await read(sessionPath)).productSales).toHaveLength(1);
    expect((await getStock()).stockQuantity).toBe(1);

    const variantProduct = await createProduct(variantName);
    const variantPath = `${catalog}/${variantProduct}/inventory`;
    const inventory = await write(variantPath, { expectedRevision: 0, numberedUniform: true, reconcileExistingStock: true,
      variants: [{ size: "M", shirtNumber: 7, color: "كحلي", quantity: 2 }, { size: "M", shirtNumber: 7, color: "أبيض", quantity: 1 }],
    }, "PUT");
    const selected = inventory.variants.find((variant: { color: string }) => variant.color === "كحلي");
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(sellPath(variantProduct));
    await page.getByLabel("الفرع").selectOption(branch.id);
    await page.getByLabel("اللون والمقاس والرقم").selectOption(selected.id);
    await quantity.fill("2");
    await page.getByLabel("طريقة الدفع").selectOption("Online");
    await review.click();
    await expect(page.getByRole("dialog")).toContainText("كحلي · M · رقم 7");
    const sent: { key: string; body: string | null }[] = [];
    await page.route(`**${sales}`, async route => {
      sent.push({ key: route.request().headers()["idempotency-key"], body: route.request().postData() });
      await route.abort("failed");
    }, { times: 1 });
    await submit.click();
    await expect(page.getByRole("alert").filter({ hasText: "تعذر تأكيد تسجيل البيع" })).toBeVisible();
    await verify.click();
    await expect(page.getByRole("alert").filter({ hasText: "لا يوجد بيع مؤكد" })).toBeVisible();
    expect((await read(variantPath)).stockQuantity).toBe(3);
    await page.route(`**${sales}`, async route => {
      sent.push({ key: route.request().headers()["idempotency-key"], body: route.request().postData() });
      await route.fulfill({ response: await route.fetch() });
    }, { times: 1 });
    await page.getByRole("button", { name: "إعادة إرسال الطلب نفسه" }).click();
    await page.getByRole("link", { name: "تفاصيل العملية" }).click();
    await expect(page.getByText("كحلي · M · رقم 7", { exact: true })).toBeVisible();
    await expect(page.getByText("تحويل إلكتروني", { exact: true })).toBeVisible();
    await expect(page.getByRole("link", { name: "عرض وردية التحصيل" })).toHaveCount(0);
    const afterInventory = await read(variantPath);
    expect(afterInventory.stockQuantity).toBe(1);
    expect(afterInventory.variants.find((variant: { id: string }) => variant.id === selected.id).quantity).toBe(0);
    expect(afterInventory.variants.find((variant: { color: string }) => variant.color === "أبيض").quantity).toBe(1);
    expect(sent).toHaveLength(2);
    expect(sent[0]).toEqual(sent[1]);
    expect((await read(sessionPath)).productSales).toHaveLength(1);
    expect((await getFinancial()).totalAmount - productBefore.totalAmount).toBe(360);
  } finally {
    if (!original.cashEnabled) await activate(original);
  }
});
