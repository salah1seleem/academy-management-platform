import { prepareImportUpload } from "../support/import-upload";
import { expect, test } from "@playwright/test";
import { ownImportWorkbook } from "../support/import-workbook";

test("A saved historical import can be found after losing approval, without another publish", async ({ page }, info) => {
  test.setTimeout(110_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "تسجيل الدخول" }).click(); await page.waitForURL("**/dashboard");
  const csrf = await page.request.get("/api/v1/auth/csrf").then(r => r.json());
  async function group(sportId: string) {
    const branchId = sportId.endsWith("2") ? "31000000-0000-0000-0000-000000000002" : "31000000-0000-0000-0000-000000000001";
    const r = await page.request.post("/api/v1/manage/groups", { headers: { "X-CSRF-TOKEN": csrf.token }, data: { arabicName: `مجموعة استيراد اصطناعية ${crypto.randomUUID()}`, branchId, sportId, ageCategoryId: "33000000-0000-0000-0000-000000000002", capacity: 2 } });
    expect(r.ok(), await r.text()).toBe(true); return await r.json() as string;
  }
  const football = await group("32000000-0000-0000-0000-000000000001"), swimming = await group("32000000-0000-0000-0000-000000000002");
  const sample = await page.request.get("/api/v1/people/import/demo-sample"); expect(sample.status()).toBe(200);
  const buffer = ownImportWorkbook(await sample.body(), { "34000000-0000-0000-0000-000000000001": football, "34000000-0000-0000-0000-000000000002": swimming, "demo-historical-v2": `e2e-${crypto.randomUUID()}`, "01088888201": `010${Date.now().toString().slice(-8)}` });
  await page.goto("/dashboard/import"); await prepareImportUpload(page); await page.getByLabel("ملف بيانات الأكاديمية").setInputFiles({ name: "synthetic-demo.xlsx", mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", buffer }); await page.getByRole("button",{name:"متابعة لفحص الملف",exact:true}).click();
  await expect(page.getByRole("heading", { name: "اكتملت المعاينة دون أخطاء" })).toBeVisible(); await expect(page.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toBeDisabled();
  await page.getByRole("checkbox", { name: /راجعت الأسماء والهواتف/ }).check(); await page.getByText("أرصدة من قالب سابق", { exact: true }).click(); await page.getByRole("checkbox", { name: /راجعت الأرصدة الافتتاحية/ }).check();
  const collectionTotal = async () => {
    const response = await page.request.get("/api/v1/subscriptions/collections");
    expect(response.ok()).toBe(true);
    return (await response.json()).total as number;
  };
  const baseline = await collectionTotal();
  const approve = page.getByRole("button", { name: "اعتماد وبدء الاستيراد" });
  const switchToDark = page.getByRole("button", { name: "تفعيل المظهر الداكن" });
  if (await switchToDark.isVisible()) await switchToDark.click();
  async function reviewApproval(theme: string, width: number) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    const ratio = await page.locator(".import-approval").evaluate(card => {
      const label = card.querySelector(".checkbox-line")!;
      const rgb = (color: string) => color.match(/[\d.]+/g)!.slice(0, 3).map(Number);
      const luminance = (color: string) => rgb(color).map(channel => channel / 255)
        .map(channel => channel <= 0.04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4)
        .reduce((sum, channel, i) => sum + channel * [.2126, .7152, .0722][i], 0);
      const fg = luminance(getComputedStyle(label).color);
      const bg = luminance(getComputedStyle(card).backgroundColor);
      return (Math.max(fg, bg) + .05) / (Math.min(fg, bg) + .05);
    });
    expect(ratio).toBeGreaterThanOrEqual(4.5);
    console.log(`approval contrast ${theme} ${width}px ${ratio.toFixed(2)}:1`);
    await page.locator(".import-approval").screenshot({ path: info.outputPath(`import-approval-${theme}-${width}.png`), animations: "disabled" });
  }
  for (const width of [320, 390, 1440]) await reviewApproval("dark", width);
  await page.getByRole("button", { name: "تفعيل المظهر الفاتح" }).click();
  await reviewApproval("light", 390);

  let job!: { id: string };
  let key = "";
  let approvals = 0;
  await page.route("**/people/import/commit", async route => {
    approvals++;
    key = route.request().headers()["idempotency-key"];
    const saved = await route.fetch();
    expect(saved.status()).toBe(202);
    job = await saved.json();
    await route.abort();
  });
  await approve.click();
  const verify = page.getByRole("button", { name: "التحقق من حالة الاعتماد" });
  await expect(verify).toBeEnabled();
  await expect(page.getByLabel("ملف بيانات الأكاديمية")).toBeDisabled();
  await expect(page.getByRole("checkbox", { name: /راجعت الأسماء والهواتف/ })).toBeDisabled();
  await expect(page.getByRole("button", { name: "إعادة فحص الملف" })).toBeDisabled();
  expect(approvals).toBe(1);

  const byRequest = "**/people/import/jobs/by-request?*";
  await page.route(byRequest, route => route.fulfill({ status: 404 }));
  await verify.click();
  await expect(page.getByText(/لم تظهر دفعة مرتبطة بهذا الطلب بعد/)).toBeVisible();
  await expect(page.getByRole("button", { name: "إعادة إرسال الطلب نفسه" })).toBeEnabled();
  expect(approvals).toBe(1);
  await verify.screenshot({ path: info.outputPath("approval-verification-action-390.png") });
  await page.unroute(byRequest);

  let detailReads = 0;
  await page.route("**/people/import/jobs/*/records", route =>
    ++detailReads === 1 ? route.fulfill({ status: 503 }) : route.continue());
  const restored = page.waitForResponse(response => response.url().includes("/jobs/by-request?") && response.ok());
  await verify.click();
  const read = await restored;
  expect(new URL(read.url()).searchParams.get("requestKey")).toBe(key);
  expect((await read.json()).id).toBe(job.id);
  await expect(page.getByRole("heading", { name: "اكتمل الاستيراد" })).toBeVisible({ timeout: 45_000 });
  await page.getByRole("button", { name: "إعادة تحميل تفاصيل الدفعة" }).click();
  await expect(page.getByRole("link", { name: /آدم مثال الاستيراد/ }).first()).toBeVisible();
  expect(approvals).toBe(1);
  expect(detailReads).toBe(2);
  expect(await collectionTotal()).toBe(baseline);
  const status = await (await page.request.get(`/api/v1/people/import/jobs/${job.id}`)).json(); expect(status.status).toBe("Completed"); expect(status.publishedRows + status.replayedRows).toBe(3); expect(status.stagedFile).toBeUndefined();
  await page.reload(); await expect(page.getByRole("heading", { name: "اكتمل الاستيراد" })).toBeVisible(); expect(approvals).toBe(1); await expect(page.getByRole("link", { name: /ليلى مثال الاستيراد/ })).toBeVisible();
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await page.screenshot({ path: info.outputPath(`import-published-${width}.png`), animations: "disabled" }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
  await page.getByRole("link", { name: /ليلى مثال الاستيراد/ }).click(); await expect(page.getByRole("region", { name: "هوية اللاعب" }).getByText("ليلى مثال الاستيراد", { exact: true })).toBeVisible();
});
