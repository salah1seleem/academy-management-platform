import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { mobileDemoToken } from "../support/mobile-demo-session";
import { signInCompensationGuardian, type CompensationGrant } from "../support/compensation";
import { afterDays, assertPlacementContrast, assertPlacementViewport, ensurePlacementSession, placementEnrollment, placementId as id, placementRead, placementWrite, serviceIdentity, setPlacementAppearance, type PlacementPlayer } from "../support/placement";
import { formatDateAr } from "../../../apps/web/src/lib/formatters";

test.describe.configure({ mode: "serial", retries: 0 });
test.beforeEach(async ({ page }) => { expect((await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).status()).toBe(200); });
const enrollment = id(101), player = id(100), group = id(90), groupPath = `/api/v1/manage/structure/groups/${group}`;
const playerURL = `/dashboard/players/${player}?tab=training`, grantRoot = `/api/v1/people/enrollments/${enrollment}`;
type GroupRecord = { id: string; arabicName: string; englishName?: string | null; branchId: string; sportId: string; ageCategoryId: string; capacity: number };
type Receipt = { id: string; amount: number };
type Session = { id: string; trainingGroupId: string; sessionDate: string; startTime: string; endTime: string; status: string };
type Review = { fingerprint: string; units: number; available: number; expectedPlayers: number; capacity: number; target: { id: string; date: string; start: string; end: string } };
const groupBody = (record: GroupRecord, capacity: number) => ({ arabicName: record.arabicName, englishName: record.englishName ?? null, branchId: record.branchId, sportId: record.sportId, ageCategoryId: record.ageCategoryId, capacity });
async function put(api: APIRequestContext, path: string, data: unknown) { const csrf = await placementRead<{ token: string }>(api, "/api/v1/auth/csrf"); return api.put(path, { headers: { "X-CSRF-TOKEN": csrf.token }, data }); }
async function grants(api: APIRequestContext) { return placementRead<CompensationGrant[]>(api, `${grantRoot}/compensations`); }
async function financial(api: APIRequestContext, today: string) {
  const query = new URLSearchParams({ from: afterDays(today, -60), to: today, pageSize: "100" });
  const first = await placementRead<{ totalCount: number; items: Receipt[] }>(api, `/api/v1/reports/receipts?${query}&page=1`);
  expect(first.totalCount).toBeGreaterThan(0); const rows = [...first.items];
  for (let page = 2; page <= Math.ceil(first.totalCount / 100); page++) rows.push(...(await placementRead<{ items: Receipt[] }>(api, `/api/v1/reports/receipts?${query}&page=${page}`)).items);
  expect(rows).toHaveLength(first.totalCount); expect(new Set(rows.map(r => r.id)).size).toBe(first.totalCount);
  return { receipts: rows.sort((a, b) => a.id.localeCompare(b.id)), collections: await placementRead(api, "/api/v1/subscriptions/collections") };
}
async function identity(api: APIRequestContext) { const value = await placementRead<PlacementPlayer>(api, `/api/v1/people/players/${player}`); expect(placementEnrollment(value, enrollment).trainingGroupId).toBe(id(70)); return serviceIdentity(value); }
async function historical(api: APIRequestContext) {
  const rows = (await Promise.all([70, 90].map(n => placementRead<Session[]>(api, `/api/v1/attendance/sessions?groupId=${id(n)}&from=2026-09-28&to=2026-11-15`)))).flat();
  return rows.filter(s => [id(71), id(91)].includes(s.id)).map(({ id, trainingGroupId, sessionDate, startTime, endTime, status }) => ({ id, trainingGroupId, sessionDate, startTime, endTime, status })).sort((a, b) => a.id.localeCompare(b.id));
}
async function openPanel(page: Page) { await page.goto(playerURL); const panel = page.locator("details.placement-level").filter({ has: page.getByText("التعويضات والحصص البديلة", { exact: true }) }); await panel.locator("summary").click(); return panel; }
async function setCapacity(page: Page, value: number, previous: GroupRecord) {
  await page.goto(`/dashboard/academy/groups/${group}/edit`);
  await expect(page.getByRole("textbox", { name: "الاسم بالعربية", exact: true })).toHaveValue(previous.arabicName);
  await expect(page.getByRole("textbox", { name: "الاسم بالإنجليزية", exact: true })).toHaveAttribute("dir", "ltr");
  for (const [label, key] of [["الفرع", "branchId"], ["الرياضة", "sportId"], ["الفئة العمرية", "ageCategoryId"]] as const) await expect(page.getByRole("combobox", { name: label, exact: true })).toHaveValue(previous[key]);
  await page.getByRole("spinbutton", { name: "السعة", exact: true }).fill(String(value));
  const response = page.waitForResponse(r => r.url().endsWith(groupPath) && r.request().method() === "PUT");
  await page.getByRole("button", { name: "حفظ التعديلات", exact: true }).click(); const saved = await response; expect(saved.status(), await saved.text()).toBe(200);
  await expect(page).toHaveURL(/\/dashboard\/academy\/groups$/); expect(await placementRead(page.request, groupPath)).toEqual({ ...previous, capacity: value });
}
const fact = (review: ReturnType<Page["getByRole"]>, label: string) => review.getByText(label, { exact: true }).locator("..").locator("dd");

test("Minutes, a real capacity change, lost release acknowledgement and Guardian refresh preserve the exact receipt ledger", async ({ page, browser }, info) => {
  test.setTimeout(90_000);
  const { today } = await placementRead<{ today: string }>(page.request, "/api/v1/academy/calendar");
  const childBefore = await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${player}`); expect(placementEnrollment(childBefore, enrollment).trainingGroupId).toBeNull();
  const source = await ensurePlacementSession(page.request, id(70), today, "19:00", "20:00");
  const preview = await placementWrite(page.request, `/api/v1/manage/groups/${id(70)}/placement/preview`, { enrollmentIds: [enrollment] }); expect(preview.status()).toBe(200);
  const allocation = await preview.json(); expect(allocation.conflicts).toEqual([]);
  expect((await placementWrite(page.request, `/api/v1/manage/groups/${id(70)}/placement`, { enrollmentIds: [enrollment], fingerprint: allocation.fingerprint })).status()).toBe(200);
  expect((await put(page.request, `/api/v1/attendance/sessions/${source.id}/status`, { status: "Cancelled" })).status()).toBe(204);
  const targets: Session[] = [];
  for (const day of [9, 10, 11]) targets.push(await ensurePlacementSession(page.request, group, afterDays(today, day), "11:00", "11:30"));
  const original = await placementRead<GroupRecord>(page.request, groupPath); expect(original.capacity).toBe(2);
  const money = await financial(page.request, today), service = await identity(page.request), history = await historical(page.request);
  expect(await grants(page.request)).toEqual([]);
  const panel = await openPanel(page), review = page.getByRole("region", { name: "معاينة التعويض", exact: true });
  await page.getByRole("combobox", { name: "الحصة الأصلية", exact: true }).selectOption(source.id);
  await page.getByLabel("صالح إلى", { exact: true }).fill(afterDays(today, 90));
  let response = page.waitForResponse(r => r.url().endsWith(`${grantRoot}/compensation/preview`));
  await page.getByRole("button", { name: "معاينة استحقاق التعويض" }).click(); let result = await response;
  expect(result.status()).toBe(200); expect((await result.json()).units).toBe(1); await expect(fact(review, "التعويض")).toHaveText("حصة واحدة");
  await page.getByRole("combobox", { name: "وحدة التعويض", exact: true }).selectOption("Minute"); await expect(review).toHaveCount(0);
  response = page.waitForResponse(r => r.url().endsWith(`${grantRoot}/compensation/preview`)); await page.getByRole("button", { name: "معاينة استحقاق التعويض" }).click(); result = await response;
  expect(result.status()).toBe(200); expect((await result.json()).units).toBe(60); await expect(fact(review, "التعويض")).toHaveText("60 دقيقة");
  await page.getByRole("button", { name: "تأكيد استحقاق التعويض" }).click(); await expect(panel.getByText("تم اعتماد التعويض دون تحصيل أو تمديد اشتراك.", { exact: true })).toBeVisible();
  const [grant] = await grants(page.request); expect(grant).toMatchObject({ units: 60, allocated: 0, consumed: 0, validFrom: today, validTo: afterDays(today, 90) });
  const bookingPath = `/api/v1/people/compensations/${grant.id}/booking`;
  const reviews: Review[] = [], writes: { status: number; key: string; fingerprint: string }[] = [];
  page.on("response", r => { if (r.url().endsWith(bookingPath) && r.request().method() === "POST") writes.push({ status: r.status(), key: r.request().headers()["idempotency-key"], fingerprint: r.request().postDataJSON().fingerprint }); });
  const bookPreview = async (target: Session, available: number) => {
    await panel.getByRole("combobox", { name: "التعويض", exact: true }).selectOption(grant.id);
    await panel.getByRole("combobox", { name: "الحصة التعويضية", exact: true }).selectOption(target.id);
    const before = await placementRead<{ players: unknown[] }>(page.request, `/api/v1/attendance/sessions/${target.id}/players`);
    const pending = page.waitForResponse(r => r.url().endsWith(`${bookingPath}/preview`)); await panel.getByRole("button", { name: "معاينة حجز التعويض" }).click(); const answer = await pending;
    expect(answer.status(), await answer.text()).toBe(200); const data: Review = await answer.json();
    const current = await placementRead<GroupRecord>(page.request, groupPath);
    expect(data).toMatchObject({ units: 30, available, expectedPlayers: before.players.length + 1, capacity: current.capacity, target: { id: target.id, date: target.sessionDate, start: "11:00:00", end: "11:30:00" } });
    await expect(fact(review, "يُحجز من التعويض")).toHaveText("30 دقيقة"); await expect(fact(review, "المتاح قبل الحجز")).toHaveText(`${available} دقيقة`);
    await expect(fact(review, "العدد بعد الحجز")).toHaveText(`${data.expectedPlayers} من ${data.capacity}`); await expect(review).toContainText(formatDateAr(target.sessionDate)); reviews.push(data); return data;
  };
  const firstReview = await bookPreview(targets[0], 60);
  const editor = await page.context().newPage(); let family: Awaited<ReturnType<typeof browser.newContext>> | undefined;
  try {
    await setCapacity(editor, 1, original);
    const denied = page.waitForResponse(r => r.url().endsWith(bookingPath) && r.request().method() === "POST"); await review.getByRole("button", { name: "تأكيد حجز التعويض" }).click(); expect((await denied).status()).toBe(409);
    await expect(panel.getByRole("alert")).toContainText("تغيرت الحصة أو الرصيد"); await expect(review).toHaveCount(0);
    expect((await grants(page.request))[0].bookings).toEqual([]); expect(await financial(page.request, today)).toEqual(money);
    const fresh = await bookPreview(targets[0], 60); expect(fresh.fingerprint).not.toBe(firstReview.fingerprint); expect(fresh.capacity).toBe(1);
    for (const width of [390, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      for (const mode of ["dark", "light"] as const) { await setPlacementAppearance(page, mode); await review.screenshot({ path: info.outputPath(`minute-review-${mode}-${width}.png`), animations: "disabled" }); await assertPlacementViewport(page, width); await assertPlacementContrast(review, "h3, dt, dd, p"); }
    }
    await review.getByRole("button", { name: "تأكيد حجز التعويض" }).click(); await expect(panel.getByText("تم حجز الحصة التعويضية وتحديث مواعيد ولي الأمر.", { exact: true })).toBeVisible();
    expect(writes.slice(0, 2).map(w => w.status)).toEqual([409, 200]); expect(writes[1].key).toBe(writes[0].key); expect(writes[1].fingerprint).not.toBe(writes[0].fingerprint);
    let row = (await grants(page.request))[0]; expect(row).toMatchObject({ allocated: 30, consumed: 0 }); expect(row.bookings).toHaveLength(1);
    await editor.goto(`/dashboard/academy/groups/${group}/edit`); const options = await placementRead<{ categories: { id: string }[] }>(page.request, "/api/v1/manage/structure/options"); const other = options.categories.find(c => c.id !== original.ageCategoryId); expect(other).toBeTruthy();
    await editor.getByRole("combobox", { name: "الفئة العمرية", exact: true }).selectOption(other!.id);
    const categoryRejection = editor.waitForResponse(r => r.url().endsWith(groupPath) && r.request().method() === "PUT"); await editor.getByRole("button", { name: "حفظ التعديلات", exact: true }).click(); expect((await categoryRejection).status()).toBe(409);
    await expect(editor.locator("form").getByRole("alert")).toContainText("حرر حجوزات التعويض قبل تغيير فئة المجموعة"); await expect(editor).toHaveURL(new RegExp(`/groups/${group}/edit$`));
    expect(await placementRead(page.request, groupPath)).toEqual({ ...original, capacity: 1 });
    await bookPreview(targets[1], 30); await review.getByRole("button", { name: "تأكيد حجز التعويض" }).click(); await expect(panel.getByText("تم حجز الحصة التعويضية وتحديث مواعيد ولي الأمر.", { exact: true })).toBeVisible();
    row = (await grants(page.request))[0]; expect(row).toMatchObject({ allocated: 60, consumed: 0 }); expect(row.bookings).toHaveLength(2);
    await expect(panel.getByText("لا يوجد رصيد تعويض صالح للحجز الآن.", { exact: true })).toBeVisible();
    const exhausted = await placementWrite(page.request, `${bookingPath}/preview`, { trainingSessionId: targets[2].id }); expect(exhausted.status()).toBe(200); const blocked = await exhausted.json(); expect(blocked.conflicts.join(" ")).toContain("رصيد التعويض المتاح لا يغطي"); expect((await placementWrite(page.request, bookingPath, { trainingSessionId: targets[2].id, fingerprint: blocked.fingerprint })).status()).toBe(409);
    family = await browser.newContext({ baseURL: info.project.use.baseURL, viewport: { width: 390, height: 900 } }); await signInCompensationGuardian(family.request); const child = await family.newPage(); await child.goto(`/guardian/children/${player}`);
    const profilePath = `/api/v1/guardian/children/${player}/profile`;
    type Profile = { enrollments: { id: string; upcoming: { id: string }[] }[] };
    const profile = await placementRead<Profile>(family.request, profilePath); expect(profile.enrollments.find(e => e.id === enrollment)?.upcoming.map(s => s.id)).toEqual(expect.arrayContaining(targets.slice(0, 2).map(t => t.id)));
    const firstBooking = row.bookings.find(b => b.trainingSessionId === targets[0].id)!; const cancelPath = `/api/v1/people/compensation-bookings/${firstBooking.id}/cancel`; let cancellations = 0;
    await page.route(`**${cancelPath}`, async route => { cancellations++; const saved = await route.fetch(); expect(saved.status(), await saved.text()).toBe(200); await route.abort("failed"); });
    await panel.getByRole("button", { name: "تحرير حجز التعويض", exact: true }).first().click(); const dialog = page.getByRole("dialog", { name: "تحرير حجز التعويض", exact: true }); await expect(dialog).toContainText(formatDateAr(targets[0].sessionDate)); await dialog.screenshot({ path: info.outputPath("minute-release-dialog.png"), animations: "disabled" });
    await dialog.getByRole("button", { name: "تأكيد تحرير الحجز" }).click(); await expect(panel.getByRole("alert")).toContainText("تعذر تأكيد الحفظ"); expect(cancellations).toBe(1);
    let corrupted = true;
    await page.route(`**${grantRoot}/compensations`, async route => { if (corrupted) { const real = await route.fetch(); const data = await real.json(); data[0].allocated = -1; await route.fulfill({ response: real, json: data }); } else await route.continue(); });
    await panel.getByRole("button", { name: "تحقّق من سجل التعويض" }).click(); await expect(panel.getByRole("alert")).toContainText("تعذرت قراءة سجل التعويض"); expect(cancellations).toBe(1);
    corrupted = false; await panel.getByRole("button", { name: "تحقّق من سجل التعويض" }).click(); await expect(panel.getByText("تم تحرير الحجز مع حفظ سجله.", { exact: true })).toBeVisible(); expect(cancellations).toBe(1);
    row = (await grants(page.request))[0]; expect(row).toMatchObject({ allocated: 30, consumed: 0 }); expect(row.bookings).toEqual(expect.arrayContaining([expect.objectContaining({ id: firstBooking.id, state: "Cancelled" }), expect.objectContaining({ trainingSessionId: targets[1].id, state: "Reserved" })]));
    await child.getByRole("region", { name: "التدريب والمواعيد" }).getByRole("button", { name: "تحديث المواعيد" }).click(); await expect(child.getByRole("region", { name: "التعويضات المعتمدة" })).toContainText("متاح للحجز 30 دقيقة");
    const updated = await placementRead<Profile>(family.request, profilePath); const upcoming = updated.enrollments.find(e => e.id === enrollment)!.upcoming.map(s => s.id); expect(upcoming).not.toContain(targets[0].id); expect(upcoming).toContain(targets[1].id);
    await child.screenshot({ path: info.outputPath("family-minute-credit-30.png"), fullPage: true, animations: "disabled" });
    expect(await identity(page.request)).toEqual(service); expect(await financial(page.request, today)).toEqual(money); expect(await historical(page.request)).toEqual(history);
    await info.attach("minute-capacity-reconciliation", { contentType: "application/json", body: JSON.stringify({ source: source.id, targets, reviews, rejectedThenReviewed: writes.slice(0, 2).map(w => ({ status: w.status, fingerprint: w.fingerprint })), receiptCount: money.receipts.length, receiptTotal: money.receipts.reduce((sum, r) => sum + r.amount, 0), receiptsUnchanged: true, serviceUnchanged: true, cancelledBooking: firstBooking.id, allocatedAfterRelease: row.allocated }, null, 2) });
  } finally {
    await family?.close(); await page.unrouteAll({ behavior: "wait" });
    // Only this synthetic grant's remaining reservation is released; no reset.
    for (const item of await grants(page.request)) for (const booking of item.bookings.filter(b => b.state === "Reserved")) expect((await placementWrite(page.request, `/api/v1/people/compensation-bookings/${booking.id}/cancel`, { version: booking.version })).status()).toBe(200);
    await setCapacity(editor, original.capacity, original); await editor.close();
  }
  expect((await grants(page.request))[0]).toMatchObject({ units: 60, allocated: 0, consumed: 0 }); expect(await identity(page.request)).toEqual(service); expect(await financial(page.request, today)).toEqual(money);
});

test("Malformed compensation options, balances and previews recover without a business write", async ({ page }, info) => {
  const { today } = await placementRead<{ today: string }>(page.request, "/api/v1/academy/calendar"); const money = await financial(page.request, today), service = await identity(page.request); let mode = "options-json", saves = 0; const crashes: string[] = [];
  page.on("pageerror", e => crashes.push(e.message)); page.on("request", r => { if (r.method() === "POST" && /\/(compensation|booking|cancel)\/?$/.test(r.url())) saves++; });
  await page.route(`**${grantRoot}/compensation/options`, async route => { if (mode === "options-json") await route.fulfill({ status: 200, contentType: "application/json", body: "{invalid-json" }); else if (mode === "options-shape") { const real = await route.fetch(); const data = await real.json(); data.targets = {}; await route.fulfill({ response: real, json: data }); } else await route.continue(); });
  await page.route(`**${grantRoot}/compensations`, async route => { if (mode === "grants-shape") { const real = await route.fetch(); const data = await real.json(); data[0].bookings = null; await route.fulfill({ response: real, json: data }); } else await route.continue(); });
  const panel = await openPanel(page);
  for (const next of ["options-shape", "grants-shape", "healthy"]) { await expect(panel.getByRole("alert")).toContainText("تعذر تحميل التعويضات والمواعيد"); await expect(panel.getByRole("button", { name: "معاينة حجز التعويض" })).toHaveCount(0); mode = next; await panel.getByRole("button", { name: "إعادة تحميل التعويضات" }).click(); }
  await expect(panel.getByRole("combobox", { name: "الحصة التعويضية", exact: true })).toBeEnabled(); const [credit] = await grants(page.request); const target = (await placementRead<{ targets: { id: string }[] }>(page.request, `${grantRoot}/compensation/options`)).targets.find(t => ![id(71), id(91)].includes(t.id))!;
  await panel.getByRole("combobox", { name: "الحصة التعويضية", exact: true }).selectOption(target.id);
  await page.route(`**/api/v1/people/compensations/${credit.id}/booking/preview`, async route => { const real = await route.fetch(); if (mode === "preview-shape") { const data = await real.json(); data.units = "bad"; await route.fulfill({ response: real, json: data }); } else await route.fulfill({ response: real }); });
  mode = "preview-shape"; await panel.getByRole("button", { name: "معاينة حجز التعويض" }).click(); await expect(panel.getByRole("alert")).toContainText("تعذر عرض المعاينة"); await expect(panel.getByRole("button", { name: "تأكيد حجز التعويض" })).toHaveCount(0); await expect(panel).not.toContainText(/invalid-json|TypeError|bad دقيقة/);
  mode = "healthy"; await panel.getByRole("button", { name: "معاينة حجز التعويض" }).click(); await expect(panel.getByRole("region", { name: "معاينة التعويض", exact: true })).toBeVisible(); await page.screenshot({ path: info.outputPath("compensation-data-recovered.png"), fullPage: true, animations: "disabled" }); expect(saves).toBe(0); expect(crashes).toEqual([]); expect(await financial(page.request, today)).toEqual(money); expect(await identity(page.request)).toEqual(service);
});

test("Group editor guards invalid capacity, cancels its draft and rejects untrusted identities without changing receipts", async ({ page, browser }, info) => {
  const original = await placementRead<GroupRecord>(page.request, groupPath), { today } = await placementRead<{ today: string }>(page.request, "/api/v1/academy/calendar"); const money = await financial(page.request, today); let puts = 0;
  page.on("request", r => { if (r.url().endsWith(groupPath) && r.method() === "PUT") puts++; });
  await page.goto(`/dashboard/academy/groups/${group}`); await page.getByRole("link", { name: "تعديل", exact: true }).click(); await page.getByRole("textbox", { name: "الاسم بالعربية", exact: true }).fill("مسودة لا تحفظ"); await page.getByRole("button", { name: "إلغاء", exact: true }).click(); await expect(page).toHaveURL(new RegExp(`/groups/${group}$`)); expect(await placementRead(page.request, groupPath)).toEqual(original);
  await page.getByRole("link", { name: "تعديل", exact: true }).click(); const name = page.getByRole("textbox", { name: "الاسم بالعربية", exact: true }), capacity = page.getByRole("spinbutton", { name: "السعة", exact: true });
  await name.fill(""); await page.getByRole("button", { name: "حفظ التعديلات", exact: true }).click(); expect(await name.evaluate((el: HTMLInputElement) => el.validity.valueMissing)).toBe(true); await name.fill(original.arabicName);
  for (const value of ["0", "1.5"]) { await capacity.fill(value); await page.getByRole("button", { name: "حفظ التعديلات", exact: true }).click(); expect(await capacity.evaluate((el: HTMLInputElement) => el.validity.valid)).toBe(false); } expect(puts).toBe(0);
  await page.getByRole("link", { name: "العودة إلى الملف", exact: true }).click(); await expect(page).toHaveURL(new RegExp(`/groups/${group}$`));
  try { for (const value of [0, -1, 1.5]) { const response = await put(page.request, groupPath, groupBody(original, value)); expect(response.status(), await response.text()).toBe(Number.isInteger(value) ? 422 : 400); expect(await placementRead(page.request, groupPath)).toEqual(original); } }
  finally { expect((await put(page.request, groupPath, groupBody(original, original.capacity))).status()).toBe(200); }
  for (const role of ["visitor", "coach", "other-academy"] as const) {
    const context = await browser.newContext({ baseURL: info.project.use.baseURL }); try { let headers: Record<string, string> | undefined;
      if (role === "coach") { const token = await mobileDemoToken(context.request, "01000000020", "رفض تعديل سعة من مدرب اصطناعي"); headers = { Authorization: `Bearer ${token}` }; expect((await context.request.get("/api/v1/coach/groups", { headers })).status()).toBe(200); }
      else if (role === "other-academy") expect((await placementWrite(context.request, "/api/v1/auth/login", { email: "owner.future@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
      const expected = role === "visitor" ? 401 : role === "coach" ? 403 : 404; expect((await context.request.get(groupPath, { headers })).status()).toBe(expected);
      const response = headers ? await context.request.put(groupPath, { headers, data: groupBody(original, -1) }) : await put(context.request, groupPath, groupBody(original, -1)); expect(response.status()).toBe(expected);
    } finally { await context.close(); }
  }
  expect(await financial(page.request, today)).toEqual(money); expect(await placementRead(page.request, groupPath)).toEqual(original);
});
