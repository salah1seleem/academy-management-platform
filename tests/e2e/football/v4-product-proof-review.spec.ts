import { Buffer } from "node:buffer";
import { type APIRequestContext, expect, test } from "@playwright/test";

const sales = "/api/v1/manage/product-sales";
const own = "/api/v1/guardian/product-sales";
const image = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=", "base64");

test("Product proof replacement, lost approval and rejection keep the right sale and stock", async ({ page, playwright }, info) => {
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
  const ownerRequest = (path: string, data: unknown, method = "POST") =>
    page.request.fetch(path, { method, data, headers: { "X-CSRF-TOKEN": csrf, "Idempotency-Key": crypto.randomUUID() } });
  async function write(path: string, data: unknown, method = "POST") {
    const response = await ownerRequest(path, data, method);
    expect(response.ok(), await response.text()).toBe(true);
    return response.json();
  }
  const guardian = await playwright.request.newContext({ baseURL: info.project.use.baseURL });
  async function family(phoneNumber: string) {
    const challenge = await guardian.post("/api/v1/mobile/auth/otp/request", { data: { phoneNumber } });
    expect(challenge.status(), await challenge.text()).toBe(202);
    const verified = await guardian.post("/api/v1/mobile/auth/otp/verify", { data: {
      phoneNumber, challengeId: (await challenge.json()).challengeId, code: "246810", deviceName: "مراجعة إثبات منتج اصطناعي",
    } });
    expect(verified.ok(), await verified.text()).toBe(true);
    return { Authorization: `Bearer ${(await verified.json()).accessToken}` };
  }

  try {
    const auth = await family("01099900102");
    const foreign = await family("01099900103");
    const enrollments = await guardian.get("/api/v1/guardian/subscriptions/enrollments", { headers: auth }).then(response => response.json());
    const child = enrollments[0];
    expect(child?.id).toBeTruthy();
    const name = `طقم إثبات اختبار ${crypto.randomUUID().slice(0, 6)}`;
    const catalog = "/api/v1/content/catalog";
    const created = await write(catalog, { sportId: child.sportId, arabicName: name, imageReference: "/demo-assets/catalog-football.svg",
      displayPrice: 100, discountPercentage: 10, currency: "EGP", displayOrder: 0, isActive: true, stockQuantity: 2,
    });
    const productId = typeof created === "string" ? created : created.id;
    const inventoryPath = `${catalog}/${productId}/inventory`;
    const initialInventory = await write(inventoryPath, { expectedRevision: 0, numberedUniform: true, reconcileExistingStock: true,
      variants: [{ size: "M", shirtNumber: 7, color: "كحلي", quantity: 1 }, { size: "M", shirtNumber: 8, color: "كحلي", quantity: 1 }],
    }, "PUT");
    const selected = initialInventory.variants.find((row: { shirtNumber: number }) => row.shirtNumber === 7);
    async function createOrder() {
      const response = await guardian.post(own, { headers: { ...auth, "Idempotency-Key": crypto.randomUUID() },
        data: { productId, quantity: 1, playerId: child.playerId, sportEnrollmentId: child.id, branchId: child.branchId, variantId: selected.id, expectedAmount: "90.00" },
      });
      expect(response.ok(), await response.text()).toBe(true);
      return (await response.json()).id as string;
    }
    async function ownOrder(id: string) {
      const response = await guardian.get(`${own}/${id}`, { headers: auth });
      expect(response.ok(), await response.text()).toBe(true);
      return response.json();
    }
    async function upload(headers: typeof auth, client: APIRequestContext = guardian) {
      const response = await client.post("/api/v1/assets/upload", { headers, multipart: {
        purpose: "Proof", file: { name: "synthetic-payment-proof.png", mimeType: "image/png", buffer: image },
      } });
      expect(response.status(), await response.text()).toBe(201);
      return (await response.json()).id as string;
    }
    const attach = (order: string, assetId: string) => guardian.post(`${own}/${order}/proof`, { headers: auth, data: { assetId } });
    const reviewUrl = (order: string) => `${sales}/${order}/review`;
    const openOrder = (order: string) => page.goto(`/dashboard/product-sales/${order}`);
    const report = (type: string) => read(`/api/v1/reports/financial?allDates=true&branchId=${child.branchId}&collectionType=${type}`);
    const before = await report("Product");
    const subscriptions = await report("Subscription");
    const acceptOrder = await createOrder();
    const rejectOrder = await createOrder();
    const competingOrder = await createOrder();
    const accept = page.getByRole("button", { name: "قبول وخصم المخزون" });
    const reject = page.getByRole("button", { name: "رفض الطلب", exact: true });
    const confirm = page.getByRole("button", { name: "تأكيد المراجعة" });
    const verify = page.getByRole("button", { name: "التحقق من القرار" });

    await openOrder(acceptOrder);
    await expect(page.getByText("بانتظار إثبات الدفع", { exact: true })).toBeVisible();
    await expect(accept).toHaveCount(0);
    await expect(reject).toHaveCount(0);
    for (const decision of [true, false]) expect((await ownerRequest(reviewUrl(acceptOrder), { accept: decision })).status()).toBe(409);
    expect((await ownOrder(acceptOrder)).hasProof).toBe(false);
    const invalidUpload = await guardian.post("/api/v1/assets/upload", { headers: auth, multipart: {
      purpose: "Proof", file: { name: "not-an-image.png", mimeType: "image/png", buffer: Buffer.from("unsupported synthetic data") },
    } });
    expect(invalidUpload.status()).toBe(400);
    const foreignAsset = await upload(foreign);
    expect((await attach(acceptOrder, foreignAsset)).status()).toBe(404);
    expect((await guardian.get(`${own}/${acceptOrder}`, { headers: foreign })).status()).toBe(404);
    const first = await upload(auth);
    for (let n = 0; n < 2; n++) expect((await attach(acceptOrder, first)).ok()).toBe(true);
    expect((await ownOrder(acceptOrder))).toMatchObject({ hasProof: true, status: "Pending", amount: 90 });
    expect(await read(inventoryPath)).toEqual(initialInventory);
    expect(await report("Product")).toEqual(before);
    expect([403, 404]).toContain((await guardian.get(`/api/v1/assets/${first}`, { headers: foreign })).status());

    await page.reload();
    const proof = page.getByRole("link", { name: "فتح إثبات الدفع" });
    await expect(proof).toHaveAttribute("href", `/api/v1/assets/${first}`);
    const asset = await page.request.get(`/api/v1/assets/${first}`);
    expect(asset.ok()).toBe(true);
    expect(asset.headers()["content-type"]).toContain("image/png");
    const proofPage = page.context().waitForEvent("page", { timeout: 10000 });
    await proof.click();
    const opened = await proofPage;
    await expect(opened).toHaveURL(new RegExp(`/api/v1/assets/${first}$`));
    await expect(opened.locator("img")).toBeVisible();
    await opened.close();
    for (const width of [320, 390, 1440]) {
      await page.setViewportSize({ width, height: 844 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
      await page.screenshot({ path: info.outputPath(`review-proof-${width}.png`), fullPage: true, animations: "disabled" });
    }
    await page.setViewportSize({ width: 390, height: 844 });
    await accept.click();
    await expect(page.getByRole("dialog")).toContainText(`${name} · 90 جنيه`);
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(accept).toBeFocused();
    expect((await ownOrder(acceptOrder)).status).toBe("Pending");

    await accept.click();
    const replacement = await upload(auth);
    expect((await attach(acceptOrder, replacement)).ok()).toBe(true);
    await confirm.click();
    await expect(page.getByRole("alert").filter({ hasText: "تغيّر إثبات الدفع" })).toBeVisible();
    await expect(proof).toHaveAttribute("href", `/api/v1/assets/${replacement}`);
    expect(await read(inventoryPath)).toEqual(initialInventory);
    expect(await report("Product")).toEqual(before);

    const acceptReason = "تحويل اصطناعي مطابق";
    await page.getByLabel("ملاحظة لولي الأمر (اختياري)").fill(acceptReason);
    const commands: unknown[] = [];
    await page.route(`**${reviewUrl(acceptOrder)}`, async route => {
      commands.push(route.request().postDataJSON());
      const response = await route.fetch();
      expect(response.ok(), await response.text()).toBe(true);
      await route.abort("failed");
    }, { times: 1 });
    await accept.click();
    await confirm.click();
    await expect(page.getByRole("alert").filter({ hasText: "تعذر تأكيد حفظ المراجعة" })).toBeVisible();
    await expect(reject).toHaveCount(0);
    await verify.click();
    await expect(page.getByText("ظهر القرار المحفوظ في الطلب.")).toBeVisible();
    await expect(page.getByText(acceptReason, { exact: true })).toBeVisible();
    expect(commands).toEqual([{ accept: true, reason: acceptReason, expectedProofAssetId: replacement }]);
    expect((await ownOrder(acceptOrder))).toMatchObject({ status: "Confirmed", reviewReason: acceptReason, hasProof: true, amount: 90 });
    const after = await read(inventoryPath);
    expect(after.stockQuantity).toBe(1);
    expect(after.variants.find((row: { shirtNumber: number }) => row.shirtNumber === 7).quantity).toBe(0);
    expect(after.variants.find((row: { shirtNumber: number }) => row.shirtNumber === 8).quantity).toBe(1);
    expect((await report("Product")).totalAmount - before.totalAmount).toBe(90);

    const competingProof = await upload(auth);
    expect((await attach(competingOrder, competingProof)).ok()).toBe(true);
    const unavailable = await ownerRequest(reviewUrl(competingOrder), { accept: true, expectedProofAssetId: competingProof });
    expect(unavailable.status()).toBe(409);
    expect((await ownOrder(competingOrder))).toMatchObject({ status: "Pending", variantId: selected.id, amount: 90 });

    const rejectionProof = await upload(auth);
    expect((await attach(rejectOrder, rejectionProof)).ok()).toBe(true);
    await openOrder(rejectOrder);
    const rejection = "الإثبات التجريبي لا يطابق الطلب";
    await page.getByLabel("ملاحظة لولي الأمر (اختياري)").fill(rejection);
    const rejectedBodies: unknown[] = [];
    await page.route(`**${reviewUrl(rejectOrder)}`, async route => {
      rejectedBodies.push(route.request().postDataJSON());
      await route.abort("failed");
    }, { times: 1 });
    await reject.click();
    await confirm.click();
    await expect(page.getByRole("alert").filter({ hasText: "تعذر تأكيد حفظ المراجعة" })).toBeVisible();
    await verify.click();
    const retry = page.getByRole("button", { name: "إعادة إرسال نفس القرار" });
    await expect(retry).toBeVisible();
    await page.route(`**${sales}/${rejectOrder}`, route => route.fulfill({ status: 503, json: {} }), { times: 1 });
    await page.route(`**${reviewUrl(rejectOrder)}`, async route => {
      rejectedBodies.push(route.request().postDataJSON());
      await route.fulfill({ response: await route.fetch() });
    }, { times: 1 });
    await retry.click();
    await expect(page.getByRole("alert").filter({ hasText: "تعذر مطابقة القرار" })).toBeVisible();
    await expect(retry).toHaveCount(0);
    await verify.click();
    await expect(page.getByText("ظهر القرار المحفوظ في الطلب.")).toBeVisible();
    expect(rejectedBodies).toHaveLength(2);
    expect(rejectedBodies[0]).toEqual(rejectedBodies[1]);
    expect((await ownOrder(rejectOrder))).toMatchObject({ status: "Rejected", reviewReason: rejection, hasProof: true });
    expect((await ownerRequest(reviewUrl(rejectOrder), { accept: true, expectedProofAssetId: rejectionProof })).status()).toBe(409);
    expect(await read(inventoryPath)).toEqual(after);
    expect((await report("Product")).totalAmount - before.totalAmount).toBe(90);
    expect(await report("Subscription")).toEqual(subscriptions);
  } finally { await guardian.dispose(); }
});
