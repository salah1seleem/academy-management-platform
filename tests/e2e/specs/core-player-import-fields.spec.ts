import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { afterDays, assertPlacementContrast, assertPlacementViewport, placementRead, placementWrite, setPlacementAppearance } from "../support/placement";
import { mobileDemoToken } from "../support/mobile-demo-session";
import { prepareImportUpload } from "../support/import-upload";
import { columns, fieldCohort, parseCsv, playerRows, repairCohort } from "../support/import-field-cases";
import { editImportCells, isolateImportFieldPlans, readImportCells } from "../support/import-cell-fixture";

test.describe.configure({ retries: 0 });
const root = "/api/v1/people/import";
type Issue = { sheet: string; row: number; column: string; message: string; sheetLabel?: string; columnLabel?: string };
type Preview = { sourceSystem: string | null; cutoffDate: string | null; canApprove: boolean; rowCount: number; playerCount: number; errors: Issue[]; rows: { legacyPlayerId: string; legacyEnrollmentId: string; guardianPhone: string }[]; cleanRows: { playerCount: number; excludedRows: number; relatedRows?: number } };
type RecordRow = { id: string; legacyPlayerId: string; legacyEnrollmentId: string; player: string; startDate: string; endDate: string | null; priceSnapshot: string; currencySnapshot: string; remainingSessions: number | null };
type Schema = { connectedSheets: { name: string; columns: { key: string; label: string; required: boolean }[] }[] };
const hash = (data: Buffer) => createHash("sha256").update(data).digest("hex");
async function financial(api: APIRequestContext, today: string) {
  const scope = new URLSearchParams({ from: afterDays(today, -60), to: today });
  const first = await placementRead<{ totalCount: number; items: { id: string; amount: number }[] }>(api, `/api/v1/reports/receipts?${scope}&pageSize=100&page=1`);
  expect(first.totalCount).toBeGreaterThan(0); const receipts = [...first.items];
  for (let page = 2; page <= Math.ceil(first.totalCount / 100); page++) receipts.push(...(await placementRead<{ items: { id: string; amount: number }[] }>(api, `/api/v1/reports/receipts?${scope}&pageSize=100&page=${page}`)).items);
  expect(receipts).toHaveLength(first.totalCount); expect(new Set(receipts.map(row => row.id)).size).toBe(first.totalCount);
  expect(receipts.every(row => Number.isFinite(row.amount))).toBe(true); expect(receipts.reduce((sum, row) => sum + row.amount, 0)).toBeGreaterThan(0);
  return { receipts: receipts.sort((a, b) => a.id.localeCompare(b.id)), collections: await placementRead(api, "/api/v1/subscriptions/collections"), statement: await placementRead(api, `/api/v1/reports/operating-statement?${scope}`) };
}
function sheetRows(book: Buffer, schema: Schema, sheet: string) {
  const fields = schema.connectedSheets.find(entry => entry.name === sheet)!.columns, cells = readImportCells(book, sheet);
  const numbers = [...new Set([...cells.keys()].map(key => Number(key.replace(/^[A-Z]+/, ""))))].filter(row => row > 1).sort((a, b) => a - b);
  return numbers.map(row => Object.fromEntries(fields.map((field, index) => [field.key, cells.get(`${String.fromCharCode(65 + index)}${row}`)?.value ?? ""])));
}
function expectedEnd(start: string, plan: Record<string, string>) {
  const months = Number(plan.Months), days = Number(plan.Days);
  if (months > 0) {
    const beginning = new Date(`${start}T12:00:00Z`), target = new Date(Date.UTC(beginning.getUTCFullYear(), beginning.getUTCMonth() + months, 1, 12));
    const last = new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0, 12)).getUTCDate(); target.setUTCDate(Math.min(last, beginning.getUTCDate()));
    return new Date(target.valueOf() - 86400000).toISOString().slice(0, 10);
  }
  return days > 0 ? afterDays(start, days - 1) : null;
}
async function download(page: Page, name: RegExp) {
  const pending = page.waitForEvent("download"); await page.getByRole("button", { name }).click(); const file = await pending; const path = await file.path(); expect(path).toBeTruthy(); return readFile(path!);
}
async function choose(page: Page, buffer: Buffer, name: string) {
  await prepareImportUpload(page); const chooser = page.waitForEvent("filechooser"); await page.getByRole("button", { name: "استيراد ملف", exact: true }).click(); await (await chooser).setFiles({ name, mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", buffer });
  await expect(page.getByRole("region", { name: "الملف المختار" })).toContainText(name);
  await expect(page.getByRole("region", { name: "الملف المختار" })).toContainText(`${Math.ceil(buffer.byteLength / 1024)} كيلوبايت`);
}
async function requestPreview(page: Page, apiBase = root) {
  const pending = page.waitForResponse(response => response.url().endsWith(`${apiBase}/preview`) && response.request().method() === "POST");
  await page.getByRole("region", { name: "الملف المختار" }).getByRole("button", { name: /^(متابعة لفحص الملف|إعادة فحص الملف)$/ }).click();
  const response = await pending; expect(response.status(), await response.text()).toBe(200); return response;
}
async function preview(page: Page, apiBase = root) { return (await requestPreview(page, apiBase)).json() as Promise<Preview>; }

async function publish(page: Page, expected: number) {
  await expect(page.getByRole("heading", { name: "اكتملت المعاينة دون أخطاء" })).toBeVisible();
  await page.getByRole("checkbox", { name: /راجعت الأسماء والهواتف/ }).check();
  const pending = page.waitForResponse(response => response.url().endsWith(`${root}/commit`) && response.request().method() === "POST");
  await page.getByRole("button", { name: "اعتماد وبدء الاستيراد" }).click(); const response = await pending; expect(response.status(), await response.text()).toBe(202); const accepted = await response.json();
  await expect(page.getByRole("heading", { name: "اكتمل الاستيراد", exact: true })).toBeVisible({ timeout: 45_000 });
  await expect(page.getByRole("status").getByText(`التسجيلات الجديدة ${expected}`, { exact: true })).toBeVisible();
  const rows = await placementRead<RecordRow[]>(page.request, `${root}/jobs/${accepted.id}/records`); expect(rows).toHaveLength(expected); return { id: accepted.id as string, rows };
}

test("Owner isolates22 corrupt field records, imports28 valid records and then22 repaired records without duplicate identities or financial movement", async ({ page, browser }, info) => {
  test.setTimeout(180_000);
  expect((await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
  const { today } = await placementRead<{ today: string }>(page.request, "/api/v1/academy/calendar");
  const before = await financial(page.request, today), jobsBefore = await placementRead(page.request, `${root}/jobs`);
  const writes: string[] = [], crashes: string[] = [], remoteImages: string[] = [];
  page.on("request", request => { if (request.method() === "POST" && request.url().includes(`${root}/`)) writes.push(new URL(request.url()).pathname); if (request.url().startsWith("https://invalid.example/")) remoteImages.push(request.url()); });
  page.on("pageerror", error => crashes.push(error.message));
  await page.route(`**${root}/schema`, async route => { const real = await route.fetch(); const data = await real.json(); data.connectedSheets.find((sheet: { name: string }) => sheet.name === "اللاعبون").columns = null; await route.fulfill({ response: real, json: data }); });
  await page.goto("/dashboard/import");
  let guide = page.locator('details[aria-label="دليل حقول ملف الأكاديمية"]'); await guide.locator(":scope > summary").click(); await expect(guide.getByRole("alert")).toContainText("تعذرت قراءة دليل الحقول");
  await page.unroute(`**${root}/schema`); await page.reload();
  await expect(page.getByRole("heading", { name: "استيراد بيانات الأكاديمية", exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "بيانات البداية المترابطة لأكاديمية واحدة", exact: true })).toHaveCount(0);
  const schema = await placementRead<Schema>(page.request, `${root}/schema`), fields = schema.connectedSheets.find(sheet => sheet.name === "اللاعبون")!.columns;
  expect(fields.filter(field => field.required)).toHaveLength(11); expect(fields.filter(field => !field.required)).toHaveLength(8);
  const shown: { required: string[]; optional: string[] } = { required: [], optional: [] };
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    for (const mode of ["dark", "light"] as const) {
      await setPlacementAppearance(page, mode); guide = page.locator('details[aria-label="دليل حقول ملف الأكاديمية"]');
      if (!await guide.getAttribute("open").then(value => value !== null)) await guide.locator(":scope > summary").click();
      const playerGuide = guide.locator("details").filter({ has: page.locator("summary").filter({ hasText: /^اللاعبون/ }) });
      await expect(playerGuide).toHaveAttribute("open", "");
      shown.required = await playerGuide.getByRole("heading", { name: /^مطلوب/ }).locator("..").locator("li").allTextContents();
      shown.optional = await playerGuide.getByRole("heading", { name: /^اختياري/ }).locator("..").locator("li").allTextContents();
      expect(shown.required).toEqual(fields.filter(field => field.required).map(field => field.label)); expect(shown.optional).toEqual(fields.filter(field => !field.required).map(field => field.label));
      await expect(playerGuide.locator('bdi[dir="ltr"]')).not.toHaveCount(0); await assertPlacementContrast(playerGuide, "h4, li, summary"); await assertPlacementViewport(page, width);
      await playerGuide.screenshot({ path: info.outputPath(`import-player-fields-${mode}-${width}.png`), animations: "disabled" });
      await guide.locator(":scope > summary").click();
    }
  }
  await guide.locator(":scope > summary").click(); const playerGuide = guide.locator("details").filter({ has: page.locator("summary").filter({ hasText: /^اللاعبون/ }) });
  await playerGuide.locator(":scope > summary").focus(); await playerGuide.locator(":scope > summary").press("Enter"); await expect(playerGuide).not.toHaveAttribute("open", ""); await playerGuide.locator(":scope > summary").press("Enter"); await expect(playerGuide).toHaveAttribute("open", "");
  const guideSheets: { sheet: string; fields: string[] }[] = [];
  await expect(guide.locator("details")).toHaveCount(schema.connectedSheets.length);
  for (const [index, sheet] of schema.connectedSheets.entries()) {
    const section = guide.locator("details").nth(index), summary = section.locator(":scope > summary");
    await expect(summary).toContainText(sheet.name);
    if (await section.getAttribute("open") !== null) await summary.click();
    await expect(section).not.toHaveAttribute("open", "");
    await summary.focus(); await summary.press("Enter"); await expect(section).toHaveAttribute("open", "");
    const fields = sheet.columns.filter(field => field.key !== "ExistingId");
    const actual = await section.locator("li").allTextContents();
    expect(actual).toEqual([...fields.filter(field => field.required), ...fields.filter(field => !field.required)].map(field => field.label));
    guideSheets.push({ sheet: sheet.name, fields: actual });
    await summary.click(); await expect(section).not.toHaveAttribute("open", "");
  }
  await writeFile(info.outputPath("field-guide-sheets.json"), JSON.stringify(guideSheets, null, 2));
  await page.getByRole("link", { name: "العودة إلى الرئيسية", exact: true }).click(); await expect(page).toHaveURL(/\/dashboard$/); await page.getByRole("button", { name: "إدارة الأكاديمية", exact: true }).click(); await page.getByRole("link", { name: "استيراد البيانات", exact: true }).click(); await expect(page).toHaveURL(/\/dashboard\/import$/); expect(writes).toEqual([]);
  const downloaded = await download(page, /تنزيل نموذج مكتمل/), source = isolateImportFieldPlans(downloaded); const sourceHash = hash(downloaded), fixtureHash = hash(source), sourceCells = readImportCells(source, "اللاعبون");
  expect(columns.map(column => sourceCells.get(`${column}1`)!.value)).toEqual(fields.map(field => `${field.label}${field.required ? " *" : ""}`));
  await writeFile(info.outputPath("source-player-fields.xlsx"), source); const cohort = fieldCohort(source); await choose(page, cohort.mixed, "player-field-errors.xlsx"); expect(writes).toEqual([]); expect(await placementRead(page.request, `${root}/jobs`)).toEqual(jobsBefore);
  const mixed = await preview(page); await writeFile(info.outputPath("raw-mixed-preview.json"), JSON.stringify(mixed, null, 2)); expect(mixed.canApprove).toBe(false); expect(mixed.cleanRows).toMatchObject({ playerCount: 28, excludedRows: 22 });
  await expect(page.getByRole("heading", { name: "الملف يحتاج تصحيحًا", exact: true })).toBeVisible(); await expect(page.getByRole("heading", { name: "28 سليمًا · 22 للتصحيح", exact: true })).toBeVisible();
  const reasons: Record<string, string> = { D06: "تاريخ ميلاد صحيح", D07: "تاريخ بداية صحيح", D08: "تسبق تاريخ الميلاد", D09: "اختر ذكر أو أنثى", D10: "اختر ذكر أو أنثى أو محايد", D11: "اكتب كودًا نصيًا ثابتًا" };
  for (const failure of cohort.failures) {
    const reason = failure.id.startsWith("IF-R") ? "حقل مطلوب" : reasons[failure.id.slice(3)] ?? "رقم موبايل مصري صالح";
    expect(mixed.errors.some(issue => issue.sheet === "اللاعبون" && issue.row === failure.issueRow && issue.column === failure.field && issue.message.includes(reason)), failure.id).toBe(true);
  }
  expect([...new Set(mixed.errors.filter(issue => issue.sheet === "اللاعبون").map(issue => issue.row))].sort((a, b) => a - b)).toEqual(Array.from({ length: 22 }, (_, i) => i + 2));
  const issues = page.getByRole("region", { name: "مراجعة أخطاء الملف" }); await issues.getByLabel("رقم الصف في Excel").fill("١٣");
  const table = page.getByRole("table", { name: "مشكلات الاستيراد" }); await expect(table).toContainText("الصف 13");
  const csv = parseCsv((await download(page, /تنزيل تقرير الأخطاء/)).toString());
  expect(csv).toHaveLength(mixed.errors.length + 1);
  const filteredCount = mixed.errors.filter(issue => issue.row === 13).length;
  await expect(issues.getByRole("status").locator("span").filter({ hasText: /^المشكلات المعروضة/ }).locator("strong")).toHaveText(String(filteredCount));
  for (const issue of mixed.errors) expect(csv).toContainEqual([issue.sheetLabel ?? issue.sheet, String(issue.row), issue.columnLabel ?? issue.column, issue.message]);
  await issues.getByRole("button", { name: "مسح التصفية" }).click();
  await expect(issues.getByRole("status").locator("span").filter({ hasText: /^المشكلات المعروضة/ }).locator("strong")).toHaveText(String(mixed.errors.length));
  const affectedRows = new Set(mixed.errors.filter(issue => issue.row > 0).map(issue => `${issue.sheet}:${issue.row}`)).size;
  await expect(issues.getByRole("status").locator("span").filter({ hasText: /^الصفوف المتأثرة/ }).locator("strong")).toHaveText(String(affectedRows));
  const reviewMetrics: { width: number; mode: string; height: number; fontSizes: number[] }[] = [];
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    for (const mode of ["dark", "light"] as const) {
      await setPlacementAppearance(page, mode); await assertPlacementViewport(page, width);
      const colorSamples = await table.locator('[role="cell"], [role="cell"] small').evaluateAll(nodes => nodes.map(node => {
        const row = node.closest("article"), style = getComputedStyle(node);
        return { text: node.textContent, color: style.color, background: row ? getComputedStyle(row).backgroundColor : "", hovered: row?.matches(":hover") };
      }));
      await writeFile(info.outputPath(`import-error-colors-${mode}-${width}.json`), JSON.stringify(colorSamples, null, 2));
      try { await assertPlacementContrast(issues, '[role="cell"], [role="cell"] small, label, button, p, h3, h4'); }
      catch (error) { await issues.screenshot({ path: info.outputPath(`import-contrast-failure-${mode}-${width}.png`), animations: "disabled" }); throw error; }
      await expect(table.locator('bdi[dir="ltr"]')).not.toHaveCount(0);
      const measured = await table.locator(".admin-data-row").first().evaluate(row => ({ height: row.getBoundingClientRect().height, fontSizes: [...row.querySelectorAll('[role="cell"], small')].map(node => parseFloat(getComputedStyle(node).fontSize)) }));
      if (width === 390) { expect(measured.height).toBeLessThanOrEqual(140); expect(measured.fontSizes.every(size => size >= 14)).toBe(true); }
      reviewMetrics.push({ width, mode, ...measured });
      await issues.screenshot({ path: info.outputPath(`import-errors-${mode}-${width}.png`), animations: "disabled" });
    }
  }
  await page.setViewportSize({ width: 390, height: 900 }); await assertPlacementViewport(page, 390);
  const pagination = issues.getByRole("navigation", { name: "صفحات مشكلات الاستيراد" });
  expect(mixed.errors.length).toBeGreaterThan(25);
  async function expectIssuePage(expected: Issue[]) {
    const rows = table.locator(".admin-data-row"); await expect(rows).toHaveCount(expected.length);
    for (const [index, issue] of expected.entries()) {
      await expect(rows.nth(index).getByRole("cell").first()).toContainText(`${issue.sheetLabel ?? issue.sheet} · الصف ${issue.row}`);
      await expect(rows.nth(index).getByRole("cell").last()).toHaveText(issue.message);
    }
  }
  await expect(pagination.getByRole("button", { name: "الصفحة السابقة" })).toBeDisabled();
  await expectIssuePage(mixed.errors.slice(0, 25)); await pagination.getByRole("button", { name: "الصفحة التالية" }).click(); await expectIssuePage(mixed.errors.slice(25));
  await expect(pagination.getByRole("button", { name: "الصفحة التالية" })).toBeDisabled();
  await pagination.getByRole("button", { name: "الصفحة السابقة" }).click(); await expectIssuePage(mixed.errors.slice(0, 25));
  await pagination.getByLabel("صفوف الصفحة").selectOption("10"); await expectIssuePage(mixed.errors.slice(0, 10));
  await pagination.getByRole("button", { name: "الصفحة التالية" }).click(); await expectIssuePage(mixed.errors.slice(10, 20));
  await pagination.getByLabel("صفوف الصفحة").selectOption("50"); await expectIssuePage(mixed.errors);
  await expect(pagination.getByRole("button", { name: "الصفحة التالية" })).toBeDisabled(); await expect(pagination.getByRole("button", { name: "الصفحة السابقة" })).toBeDisabled();
  await pagination.getByLabel("صفوف الصفحة").selectOption("25"); await expectIssuePage(mixed.errors.slice(0, 25)); await assertPlacementViewport(page, 390);
  await writeFile(info.outputPath("issue-review-metrics.json"), JSON.stringify({ reviewMetrics, totalIssues: mixed.errors.length, paginationSizes: [25, 10, 50, 25] }, null, 2));
  const clean = await download(page, /تنزيل ملف اللاعبين السليمين/), correction = await download(page, /تنزيل صفوف التصحيح/);
  const cleanRows = playerRows(clean), correctionRows = playerRows(correction), expectedClean = cohort.originals.slice(22).map(row => row.cells.A);
  expect(cleanRows.map(row => row.cells.A)).toEqual(expectedClean); expect(correctionRows.map(row => row.cells)).toEqual(playerRows(cohort.mixed).slice(0, 22).map(row => row.cells));
  expect(hash(downloaded)).toBe(sourceHash); expect(hash(source)).toBe(fixtureHash); expect(await placementRead(page.request, `${root}/jobs`)).toEqual(jobsBefore); expect(await financial(page.request, today)).toEqual(before); expect(writes.filter(path => path.endsWith("/commit"))).toHaveLength(0);
  await writeFile(info.outputPath("field-preview-evidence.json"), JSON.stringify({ sourceHash, fixtureHash, fields, cases: cohort.failures, mixed, cleanPlayers: cleanRows, correctionPlayers: correctionRows, financialBefore: before }, null, 2));
  await choose(page, clean, "valid-player-fields.xlsx"); const validPreview = await preview(page); expect(validPreview.canApprove).toBe(true); expect(validPreview.playerCount).toBe(28); expect(validPreview.errors).toEqual([]);
  const first = await publish(page, 28); expect(first.rows.map(row => row.legacyPlayerId).sort()).toEqual([...expectedClean].sort());
  const fixed = repairCohort(correction, source); await choose(page, fixed, "corrected-player-fields.xlsx"); const repairedPreview = await preview(page); expect(repairedPreview.canApprove).toBe(true); expect(repairedPreview.playerCount).toBe(22); expect(repairedPreview.errors).toEqual([]);
  const second = await publish(page, 22), saved = [...first.rows, ...second.rows].sort((a, b) => a.legacyPlayerId.localeCompare(b.legacyPlayerId));
  expect(saved).toHaveLength(50); expect(new Set(saved.map(row => row.id)).size).toBe(50); expect(new Set(saved.map(row => row.legacyEnrollmentId)).size).toBe(50); expect(saved.map(row => row.legacyPlayerId)).toEqual(cohort.originals.map(row => row.cells.A));
  const identities: { legacyPlayerId: string; player: string; guardian: string; normalizedPhone: string }[] = [], plans = sheetRows(source, schema, "الباقات"), attendance = sheetRows(source, schema, "حضور اختياري");
  for (const row of saved) {
    const original = cohort.originals.find(source => source.cells.A === row.legacyPlayerId)!;
    expect(row.player).toBe(`${original.cells.B} ${original.cells.C}`); expect(row.startDate).toBe(original.cells.L); expect(row.currencySnapshot).toBe(original.cells.Q);
    const plan = plans.find(plan => plan.Code === original.cells.K)!; expect(plan).toBeTruthy();
    const transferredNet = row.legacyPlayerId === "PL23" ? plan.Price : original.cells.P || plan.Price;
    expect(Number(row.priceSnapshot)).toBe(Number(transferredNet)); expect(row.endDate).toBe(original.cells.M || expectedEnd(original.cells.L, plan));
    if (plan.Type !== "مدة") {
      const used = attendance.filter(entry => entry.Enrollment === row.legacyEnrollmentId && ["حاضر", "متأخر"].includes(entry.State)).length;
      const expected = original.cells.O ? Number(original.cells.O) : Number(original.cells.N || plan.Sessions) - used; expect(row.remainingSessions).toBe(expected);
    }
    const player = await placementRead<{ dateOfBirth: string; guardians: { id: string }[] }>(page.request, `/api/v1/people/players/${row.id}`); expect(player.dateOfBirth).toBe(original.cells.E); expect(player.guardians).toHaveLength(1);
    const guardian = await placementRead<{ contactPhone: string }>(page.request, `/api/v1/people/guardians/${player.guardians[0].id}`); expect(guardian.contactPhone).toBe(`+20${original.cells.D.slice(1)}`); identities.push({ legacyPlayerId: row.legacyPlayerId, player: row.id, guardian: player.guardians[0].id, normalizedPhone: guardian.contactPhone });
  }
  const expectedHouseholds = new Set(cohort.originals.map(row => row.cells.D)); expect(new Set(identities.map(row => row.guardian)).size).toBe(expectedHouseholds.size);
  for (const phone of expectedHouseholds) expect(new Set(identities.filter(row => row.normalizedPhone === `+20${phone.slice(1)}`).map(row => row.guardian)).size).toBe(1);
  const denials: { role: string; resource: string; status: number; body: string }[] = [];
  for (const role of ["visitor", "coach", "other-academy"] as const) {
    const context = await browser.newContext({ baseURL: info.project.use.baseURL }); try { let headers: Record<string, string> | undefined;
      if (role === "coach") headers = { Authorization: `Bearer ${await mobileDemoToken(context.request, "01000000020", "رفض استيراد من مدرب اصطناعي")}` };
      else if (role === "other-academy") expect((await placementWrite(context.request, "/api/v1/auth/login", { email: "owner.future@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
      for (const suffix of ["", "/records"]) {
        const response = await context.request.get(`${root}/jobs/${second.id}${suffix}`, { headers });
        expect(response.status()).toBe(role === "visitor" ? 401 : role === "coach" ? 403 : 404);
        const text = await response.text(), body = text ? JSON.stringify(JSON.parse(text)) : "";
        for (const record of saved) expect(body).not.toContain(record.player);
        expect(body).not.toContain("corrected-player-fields.xlsx");
        denials.push({ role, resource: suffix || "/job", status: response.status(), body });
      }
    } finally { await context.close(); }
  }
  const after = await financial(page.request, today); expect(after).toEqual(before); expect(crashes).toEqual([]); expect(remoteImages).toEqual([]); expect(writes.filter(path => path.endsWith("/commit"))).toHaveLength(2);
  await writeFile(info.outputPath("field-import-reconciliation.json"), JSON.stringify({ sourceHash, fixtureHash, first, second, identities, denials, before, after, writes, receiptCount: after.receipts.length, receiptTotal: after.receipts.reduce((sum, row) => sum + row.amount, 0) }, null, 2));
  await page.screenshot({ path: info.outputPath("field-correction-completed.png"), fullPage: true, animations: "disabled" });
});

test("Platform import recovers malformed issue and split data and JSON without a commit or financial change", async ({ page, browser }, info) => {
  test.setTimeout(100_000);
  const owner = await browser.newContext({ baseURL: info.project.use.baseURL });
  try {
    expect((await placementWrite(owner.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
    const { today } = await placementRead<{ today: string }>(owner.request, "/api/v1/academy/calendar"), before = await financial(owner.request, today);
    await page.goto("/platform/login"); await page.getByLabel("البريد الإلكتروني").fill("platform.demo@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByLabel("رمز التحقق").fill("246810"); await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click(); await expect(page).toHaveURL(/\/platform$/);
    const academy = page.getByRole("button", { name: /أكاديمية النجوم الرياضية/ }); await expect(academy).toHaveCount(1); await academy.click();
    const schemaResponse = page.waitForResponse(response => /\/platform\/academies\/[^/]+\/import\/schema$/.test(response.url())); await page.getByRole("tab", { name: "استيراد البيانات", exact: true }).click(); const apiBase = new URL((await schemaResponse).url()).pathname.replace(/\/schema$/, "");
    await prepareImportUpload(page); const source = await download(page, /تنزيل نموذج مكتمل/); await writeFile(info.outputPath("source-platform-fields.xlsx"), source); const cohort = fieldCohort(source); await choose(page, cohort.mixed, "read-recovery-fields.xlsx");
    const jobs = await placementRead(page.request, `${apiBase}/jobs`); let mode = "issues"; const writes: string[] = [], crashes: string[] = [], reads: { mode: string; status: number; errors?: number }[] = [];
    page.on("request", request => { if (request.method() === "POST" && request.url().includes(`${apiBase}/`)) writes.push(new URL(request.url()).pathname); }); page.on("pageerror", error => crashes.push(error.message));
    await page.route(`**${apiBase}/preview`, async route => {
      const real = await route.fetch(); expect(real.status()).toBe(200); const body = await real.json(); reads.push({ mode, status: real.status(), errors: body.errors?.length });
      if (mode === "issues") await route.fulfill({ response: real, json: { ...body, errors: [null] } });
      else if (mode === "split") await route.fulfill({ response: real, json: { ...body, cleanRows: { playerCount: -1, excludedRows: 1 } } });
      else if (mode === "json") await route.fulfill({ response: real, body: "{invalid-json", contentType: "application/json" });
      else await route.fulfill({ response: real });
    });
    for (const fault of ["issues", "split", "json"]) {
      mode = fault; await requestPreview(page, apiBase); await expect(page.getByRole("main").getByRole("alert")).toContainText(/تعذر/); await expect(page.getByRole("region", { name: "نتيجة معاينة الاستيراد" })).toHaveCount(0); await expect(page.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toHaveCount(0);
      mode = "healthy"; const real = await preview(page, apiBase); await expect(page.getByRole("heading", { name: real.canApprove ? "اكتملت المعاينة دون أخطاء" : "الملف يحتاج تصحيحًا", exact: true })).toBeVisible(); await expect(page.getByRole("main").getByRole("alert")).toHaveCount(0);
    }
    expect(writes).toHaveLength(6); expect(writes.every(path => path.endsWith("/preview"))).toBe(true); expect(crashes).toEqual([]); expect(await placementRead(page.request, `${apiBase}/jobs`)).toEqual(jobs); const after = await financial(owner.request, today); expect(after).toEqual(before);
    await writeFile(info.outputPath("platform-preview-recovery.json"), JSON.stringify({ sourceHash: hash(source), reads, writes, before, after }, null, 2)); await page.screenshot({ path: info.outputPath("platform-import-read-recovered.png"), fullPage: true, animations: "disabled" });
  } finally { await owner.close(); }
});


test("Other academy owner sees real metadata and header rejection details without an import or collection", async ({ page }, info) => {
  test.setTimeout(90_000);
  expect((await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.future@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
  await page.goto("/dashboard/import");
  await expect(page.getByRole("heading", { name: "استيراد بيانات الأكاديمية", exact: true })).toBeVisible();
  const jobs = await placementRead(page.request, `${root}/jobs`);
  const collections = await placementRead(page.request, "/api/v1/subscriptions/collections");
  const source = await download(page, /تنزيل نموذج مكتمل/);
  const flatResponse = await page.request.get(`${root}/template`); expect(flatResponse.status()).toBe(200);
  const flatSource = await flatResponse.body(), metadata = readImportCells(flatSource, "Metadata");
  const metadataCell = (label: string) => {
    const key = [...metadata].find(([cell, data]) => /^A\d+$/.test(cell) && data.value === label)?.[0];
    expect(key, label).toBeTruthy(); return key!.replace(/^A/, "B");
  };
  const metadataFile = editImportCells(flatSource, ["SourceSystem", "CutoffDate"].map(label => ({ sheet: "Metadata", cell: metadataCell(label), value: "" })));
  const writes: string[] = [], crashes: string[] = [];
  page.on("request", request => { if (request.method() === "POST" && request.url().includes(`${root}/`)) writes.push(new URL(request.url()).pathname); });
  page.on("pageerror", error => crashes.push(error.message));
  await choose(page, metadataFile, "missing-import-metadata.xlsx"); const metadataPreview = await preview(page);
  await writeFile(info.outputPath("real-metadata-rejection.json"), JSON.stringify(metadataPreview, null, 2));
  expect(metadataPreview.canApprove).toBe(false); expect(metadataPreview.sourceSystem ?? "").toBe(""); expect(metadataPreview.cutoffDate).toBeNull();
  if (new Set(metadataPreview.errors.map(issue => issue.sheet)).size > 1) await page.getByRole("region", { name: "مراجعة أخطاء الملف" }).getByLabel("الورقة", { exact: true }).selectOption("Metadata");
  for (const column of ["SourceSystem", "CutoffDate"]) {
    const issue = metadataPreview.errors.find(issue => issue.sheet === "Metadata" && issue.column === column); expect(issue, column).toBeTruthy();
    await expect(page.getByRole("table", { name: "مشكلات الاستيراد" })).toContainText(issue!.message);
  }
  await expect(page.getByRole("heading", { name: "الملف يحتاج تصحيحًا", exact: true })).toBeVisible();
  await expect(page.getByRole("main").getByRole("alert")).toHaveCount(0); await expect(page.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toHaveCount(0);
  const flat = editImportCells(flatSource, [{ sheet: "Enrollments", cell: "A1", value: "عنوان غير مطابق" }]);
  await choose(page, flat, "legacy-invalid-header.xlsx"); const legacyPreview = await preview(page);
  await writeFile(info.outputPath("real-empty-column-rejection.json"), JSON.stringify(legacyPreview, null, 2));
  const headerIssue = legacyPreview.errors.find(issue => issue.sheet === "Enrollments" && issue.row === 1 && issue.column === "");
  expect(headerIssue).toBeTruthy(); expect(legacyPreview.canApprove).toBe(false);
  await expect(page.getByRole("table", { name: "مشكلات الاستيراد" })).toContainText(headerIssue!.message);
  await expect(page.getByRole("table", { name: "مشكلات الاستيراد" })).toContainText("بنية الملف"); await expect(page.getByRole("main").getByRole("alert")).toHaveCount(0);
  await page.screenshot({ path: info.outputPath("real-header-rejection-details.png"), fullPage: true, animations: "disabled" });
  const brokenHeader = editImportCells(source, [{ sheet: "اللاعبون", cell: "B1", value: "عنوان غير مطابق" }]);
  await choose(page, brokenHeader, "current-invalid-header.xlsx");
  const rejected = page.waitForResponse(response => response.url().endsWith(`${root}/preview`) && response.request().method() === "POST");
  await page.getByRole("region", { name: "الملف المختار" }).getByRole("button", { name: /^(متابعة لفحص الملف|إعادة فحص الملف)$/ }).click();
  const response = await rejected; expect(response.status()).toBe(422); const problem = await response.json(); expect(problem.title).toContain("عناوين ورقة اللاعبون");
  await expect(page.getByRole("main").getByRole("alert")).toContainText(problem.title); await expect(page.getByRole("region", { name: "الملف المختار" })).toContainText("current-invalid-header.xlsx");
  await expect(page.getByRole("region", { name: "نتيجة معاينة الاستيراد" })).toHaveCount(0); await expect(page.getByRole("button", { name: "اعتماد وبدء الاستيراد" })).toHaveCount(0);
  expect(writes).toHaveLength(3); expect(writes.every(path => path.endsWith("/preview"))).toBe(true); expect(crashes).toEqual([]);
  expect(await placementRead(page.request, `${root}/jobs`)).toEqual(jobs); expect(await placementRead(page.request, "/api/v1/subscriptions/collections")).toEqual(collections);
  await writeFile(info.outputPath("real-import-header-problem.json"), JSON.stringify({ problem, writes, jobsBefore: jobs, collectionsBefore: collections }, null, 2));
});
