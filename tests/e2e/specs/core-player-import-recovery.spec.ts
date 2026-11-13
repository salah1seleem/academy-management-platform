import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import { ownImportWorkbook } from "../support/import-workbook";
import { prepareImportUpload } from "../support/import-upload";

test("Platform imports 48 valid players and adds only the two corrected rows on reupload", async ({ page }, info) => {
  test.setTimeout(100_000);
  await page.goto("/platform/login");
  await page.getByLabel("البريد الإلكتروني").fill("platform.demo@example.test");
  await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByLabel("رمز التحقق").fill("246810");
  await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click();
  await expect(page).toHaveURL(/\/platform$/);
  await expect(page.getByRole("heading", { name: "إدارة المنصة", exact: true })).toBeVisible();

  const seed = crypto.randomUUID();
  const name = `أكاديمية استيراد اختبار ${seed.slice(0, 8)}`;
  const platform = "/api/v1/platform";
  const { token } = await page.request.get(`${platform}/auth/csrf`).then(r => r.json());
  const headers = { "X-CSRF-TOKEN": token };
  const created = await page.request.post(`${platform}/academies`, { headers, data: {
    requestId: seed, slug: `import-qa-${seed.slice(0, 8)}`, arabicName: name, currency: "EGP", timeZone: "Africa/Cairo",
    ownerName: "مالك اختبار الاستيراد", ownerEmail: `import-${seed}@example.test`, ownerPhone: `01097${String(Date.now()).slice(-6)}`,
    initialPassword: "Demo-Only-123!", template: "multi-sport",
  } });
  expect(created.ok(), await created.text()).toBe(true);
  const { id } = await created.json();
  const academy = `${platform}/academies/${id}`;
  const license = await page.request.get(`${academy}/entitlements`).then(r => r.json());
  const features = Object.fromEntries(license.map(({ feature }: {feature:string}) => [feature, feature === "training" || feature === "matches"]));
  expect((await page.request.put(`${academy}/entitlements`, { headers, data: { planCode: "Trial", features } })).ok()).toBe(true);
  await page.reload();
  await page.getByRole("button", { name: new RegExp(name) }).click();
  await page.getByRole("tab", { name: "استيراد البيانات" }).click();
  const savedSample = page.waitForEvent("download");
  await page.getByRole("button", { name: /تنزيل نموذج مكتمل/ }).click();
  const sample = await readFile((await (await savedSample).path())!);
  const mixed = ownImportWorkbook(sample, { PL49: "PL!49", PL50: "PL!50" });
  async function upload(buffer: Buffer, name: string) {
    await prepareImportUpload(page);
    await page.getByLabel("ملف بيانات الأكاديمية").setInputFiles({ name, mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", buffer });
    await page.getByRole("button", { name: "متابعة لفحص الملف", exact: true }).click();
  }
  async function publish(dropResponse = false) {
    await expect(page.getByRole("heading", { name: "اكتملت المعاينة دون أخطاء" })).toBeVisible();
    await page.getByRole("checkbox", { name: /راجعت الأسماء والهواتف/ }).check();
    let acceptedId = "";
    let writes = 0;
    if (dropResponse) {
      await page.route(`**${academy}/import/commit`, async route => {
        writes++;
        const response = await route.fetch();
        expect(response.status()).toBe(202);
        acceptedId = (await response.json()).id;
        await route.abort();
      });
    }
    const accepted = !dropResponse && page.waitForResponse(response => response.url().includes(`${academy}/import/commit`) && response.status() === 202);
    await page.getByRole("button", { name: "اعتماد وبدء الاستيراد" }).click();
    if (accepted) acceptedId = (await (await accepted).json()).id;
    if (dropResponse) {
      const verify = page.getByRole("button", { name: "التحقق من حالة الاعتماد" });
      await expect(verify).toBeEnabled();
      const lookup = page.waitForResponse(response => response.url().includes(`${academy}/import/jobs/by-request?`) && response.ok());
      await verify.click();
      expect((await (await lookup).json()).id).toBe(acceptedId);
      expect(writes).toBe(1);
    }
    await expect(page.getByRole("heading", { name: "اكتمل الاستيراد" })).toBeVisible({ timeout: 35_000 });
    return acceptedId;
  }

  await upload(mixed, "mixed-players.xlsx");
  await expect(page.getByRole("heading", { name: "الملف يحتاج تصحيحًا" })).toBeVisible();
  await expect(page.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toHaveCount(0);
  const problems = page.getByRole("table", { name: "مشكلات الاستيراد" });
  await expect(problems.getByText("اكتب كودًا نصيًا ثابتًا", { exact: false })).toHaveCount(2);
  await expect(problems.getByText(/اللاعبون · الصف 50/)).toBeVisible();
  await expect(problems.getByText(/اللاعبون · الصف 51/)).toBeVisible();
  await expect(page.getByRole("navigation", { name: "صفحات مشكلات الاستيراد" })).toHaveCount(0);
  const issueReview = page.getByRole("region", { name: "مراجعة أخطاء الملف" });
  await issueReview.getByLabel("رقم الصف في Excel").fill("٥١");
  await expect(problems.getByText(/اللاعبون · الصف 50/)).toHaveCount(0);
  await expect(problems.getByText(/اللاعبون · الصف 51/)).toBeVisible();
  const errorDownload = page.waitForEvent("download");
  await page.getByRole("button", { name: "تنزيل تقرير الأخطاء" }).click();
  const report = await readFile((await (await errorDownload).path())!, "utf8");
  expect(report).toContain('"اللاعبون","50","كود اللاعب *"');
  expect(report).toContain('"اللاعبون","51","كود اللاعب *"');
  await issueReview.getByRole("button", { name: "مسح التصفية" }).click();
  await expect(problems.getByText(/اللاعبون · الصف 50/)).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: info.outputPath("mixed-player-report.png"), animations: "disabled" });
  const cleanDownload = page.waitForEvent("download");
  await page.getByRole("button", { name: "تنزيل ملف اللاعبين السليمين" }).click();
  const cleanFile = await cleanDownload;
  await cleanFile.saveAs(info.outputPath("academy-valid-players.xlsx"));
  const clean = await readFile((await cleanFile.path())!);
  const correctionDownload = page.waitForEvent("download");
  await page.getByRole("button", { name: "تنزيل صفوف التصحيح" }).click();
  const correctionFile = await correctionDownload;
  expect(correctionFile.suggestedFilename()).toBe("academy-player-corrections.xlsx");
  await correctionFile.saveAs(info.outputPath("academy-player-corrections.xlsx"));
  const corrected = ownImportWorkbook(await readFile((await correctionFile.path())!), { "PL!49": "PL49", "PL!50": "PL50" });
  await upload(clean, "academy-valid-players.xlsx");
  const cleanBatch = await publish();
  await expect(page.getByRole("status").getByText("التسجيلات الجديدة 48", { exact: true })).toBeVisible();
  await upload(corrected, "corrected-players.xlsx");
  const correctedBatch = await publish(true);
  await expect(page.getByRole("status").getByText("التسجيلات الجديدة 2", { exact: true })).toBeVisible();
  await expect(page.getByText(/تسجيلات سبق استيرادها/)).toHaveCount(0);
  async function records(id: string): Promise<{ id: string; legacyEnrollmentId: string }[]> {
    const response = await page.request.get(`${academy}/import/jobs/${id}/records`);
    expect(response.ok()).toBe(true);
    return response.json();
  }
  const [first, second] = await Promise.all([records(cleanBatch), records(correctedBatch)]);
  expect(first).toHaveLength(48);
  expect(second.map(row => row.legacyEnrollmentId)).toEqual(["EN-PL49", "EN-PL50"]);
  const saved = [...first, ...second];
  expect(saved).toHaveLength(50);
  expect(new Set(saved.map(row => row.id)).size).toBe(50);
  expect(new Set(saved.map(row => row.legacyEnrollmentId)).size).toBe(50);
});
