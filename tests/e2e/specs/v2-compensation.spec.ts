import { expect, test } from "@playwright/test";
import { compensationLedger, prepareCompensation, signInCompensationGuardian } from "../support/compensation";
import { afterDays, assertPlacementContrast, assertPlacementViewport, placementId as id, placementRead, placementWrite, setPlacementAppearance } from "../support/placement";
import { formatDateAr } from "../../../apps/web/src/lib/formatters";

test("Approved compensation books once after a lost reply and is visible to its linked Guardian through reviewed release", async ({ page, browser }, info) => {
  expect((await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
  const setup = await prepareCompensation(page.request);
  const before = await compensationLedger(page.request);
  expect(before.grants).toEqual([]);
  await page.goto(`/dashboard/players/${id(80)}?tab=training`);
  const summary = page.getByText("التعويضات والحصص البديلة", { exact: true });
  const panel = page.locator("details.placement-level").filter({ has: summary });
  await summary.click();
  await page.getByLabel("الحصة الأصلية", { exact: true }).selectOption(id(71));
  const from = page.getByLabel("صالح من", { exact: true }), to = page.getByLabel("صالح إلى", { exact: true });
  await expect(from).toHaveValue(setup.today);
  for (const [a, b] of [[afterDays(setup.today, -1), setup.validTo], [setup.today, afterDays(setup.today, -1)], [setup.today, afterDays(setup.today, 91)], [setup.today, ""]]) {
    await from.fill(a); await to.fill(b);
    await expect(page.getByRole("button", { name: "معاينة استحقاق التعويض" })).toBeDisabled();
  }
  await from.fill(setup.today); await to.fill(setup.validTo);
  const grantsSent: { body: Record<string, unknown>; key: string; id: string }[] = [];
  await page.route(`**/api/v1/people/enrollments/${id(81)}/compensation`, async route => {
    const response = await route.fetch(); expect(response.status(), await response.text()).toBe(200);
    grantsSent.push({ body: route.request().postDataJSON(), key: route.request().headers()["idempotency-key"], id: (await response.json()).id });
    await route.fulfill({ response });
  });
  await page.getByRole("button", { name: "معاينة استحقاق التعويض" }).click();
  const review = page.getByRole("region", { name: "معاينة التعويض", exact: true });
  await expect(review).toContainText("مراجعة الاستحقاق");
  await page.getByRole("button", { name: "تأكيد استحقاق التعويض" }).click();
  await expect(panel.getByText("تم اعتماد التعويض دون تحصيل أو تمديد اشتراك.", { exact: true })).toBeVisible();
  expect(grantsSent).toHaveLength(1);
  const saved = grantsSent[0], root = `/api/v1/people/enrollments/${id(81)}/compensation`;
  expect(saved.key).toMatch(/^[0-9a-f]{8}-/);
  const replay = await placementWrite(page.request, root, saved.body, saved.key);
  expect(replay.status()).toBe(200); expect(await replay.json()).toMatchObject({ id: saved.id, replayed: true });
  expect((await placementWrite(page.request, root, { ...saved.body, validTo: afterDays(setup.today, 31) }, saved.key)).status()).toBe(409);
  const duplicate = await placementWrite(page.request, `${root}/preview`, saved.body);
  expect(duplicate.status()).toBe(200); expect((await duplicate.json()).conflicts.join(" ")).toContain("صدر تعويض لهذه الواقعة بالفعل");
  expect((await placementWrite(page.request, root, saved.body)).status()).toBe(409);
  let ledger = await compensationLedger(page.request);
  expect(ledger.grants).toHaveLength(1); expect(ledger.grants[0]).toMatchObject({ id: saved.id, units: 1, allocated: 0, consumed: 0, bookings: [] });
  expect(ledger.service).toEqual(before.service); expect(ledger.collections).toEqual(before.collections);

  let optionsFail = true;
  await page.route(`**/api/v1/people/enrollments/${id(81)}/compensation/options`, async route => {
    if (optionsFail) await route.fulfill({ status: 503, body: "Synthetic options failure" }); else await route.continue();
  });
  await summary.click(); await expect(panel).not.toHaveAttribute("open"); await summary.click();
  await expect(panel.getByRole("alert")).toContainText("تعذر تحميل التعويضات والمواعيد");
  await expect(panel.getByRole("combobox", { name: "الحصة التعويضية", exact: true })).toHaveCount(0);
  optionsFail = false; await panel.getByRole("button", { name: "إعادة تحميل التعويضات" }).click();
  await page.getByRole("combobox", { name: "التعويض", exact: true }).selectOption(saved.id);
  await page.getByRole("combobox", { name: "الحصة التعويضية", exact: true }).selectOption(setup.target.id);
  await page.getByRole("button", { name: "معاينة حجز التعويض" }).click();
  await expect(review).toContainText(formatDateAr(setup.date));
  await expect(review).toContainText("حصة واحدة");
  expect((await compensationLedger(page.request)).grants[0].bookings).toEqual([]);
  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    for (const mode of ["dark", "light"] as const) {
      await setPlacementAppearance(page, mode);
      await page.screenshot({ path: info.outputPath(`compensation-${mode}-${width}.png`), fullPage: true, animations: "disabled" });
      await review.screenshot({ path: info.outputPath(`compensation-review-${mode}-${width}.png`), animations: "disabled" });
      await assertPlacementViewport(page, width); await assertPlacementContrast(review, "h3, dt, dd, p");
    }
  }
  const bookingsSent: { id: string; body: Record<string, unknown>; key: string }[] = [];
  await page.route(`**/api/v1/people/compensations/${saved.id}/booking`, async route => {
    const response = await route.fetch(); expect(response.status(), await response.text()).toBe(200);
    bookingsSent.push({ id: (await response.json()).id, body: route.request().postDataJSON(), key: route.request().headers()["idempotency-key"] });
    await route.abort("failed");
  });
  await review.getByRole("button", { name: "تأكيد حجز التعويض" }).click();
  await expect(panel.getByRole("alert")).toContainText("تعذر تأكيد الحفظ. تحقّق من سجل التعويض أولًا.");
  await expect(panel.getByText("تم حجز الحصة التعويضية وتحديث مواعيد ولي الأمر.", { exact: true })).toHaveCount(0);
  await expect(panel.getByRole("combobox", { name: "التعويض", exact: true })).toBeDisabled();
  expect(bookingsSent).toHaveLength(1);
  let failRead = true;
  await page.route(`**/api/v1/people/enrollments/${id(81)}/compensations`, async route => {
    if (failRead) await route.fulfill({ status: 503, body: "Synthetic readback failure" }); else await route.continue();
  });
  await panel.getByRole("button", { name: "تحقّق من سجل التعويض" }).click();
  await expect(panel.getByRole("alert")).toContainText("تعذرت قراءة سجل التعويض");
  expect(bookingsSent).toHaveLength(1);
  failRead = false; await panel.getByRole("button", { name: "تحقّق من سجل التعويض" }).click();
  await expect(panel.getByText("تم حجز الحصة التعويضية وتحديث مواعيد ولي الأمر.", { exact: true })).toBeVisible();
  expect(bookingsSent).toHaveLength(1);
  const bookedReplay = await placementWrite(page.request, `/api/v1/people/compensations/${saved.id}/booking`, bookingsSent[0].body, bookingsSent[0].key);
  expect(bookedReplay.status()).toBe(200); expect(await bookedReplay.json()).toMatchObject({ id: bookingsSent[0].id, replayed: true });
  ledger = await compensationLedger(page.request);
  expect(ledger.grants[0]).toMatchObject({ units: 1, allocated: 1, consumed: 0 });
  expect(ledger.grants[0].bookings).toHaveLength(1);
  const booking = ledger.grants[0].bookings[0]; expect(booking).toMatchObject({ id: bookingsSent[0].id, trainingSessionId: setup.target.id, state: "Reserved" });
  expect(ledger.service).toEqual(before.service); expect(ledger.collections).toEqual(before.collections); expect(ledger.historical).toEqual(before.historical);
  const roster = await placementRead<{ players: { sportEnrollmentId: string; compensatory: boolean }[] }>(page.request, `/api/v1/attendance/sessions/${setup.target.id}/players`);
  expect(roster.players.filter(p => p.sportEnrollmentId === id(81))).toEqual([expect.objectContaining({ compensatory: true })]);
  const stale = await placementWrite(page.request, `/api/v1/people/compensation-bookings/${booking.id}/cancel`, { version: booking.version + 1 });
  expect(stale.status()).toBe(409);

  const family = await browser.newContext({ baseURL: info.project.use.baseURL, viewport: { width: 390, height: 900 } });
  try {
    await signInCompensationGuardian(family.request);
    const child = await family.newPage(); await child.goto(`/guardian/children/${id(80)}`);
    const card = child.getByRole("region", { name: "التعويضات المعتمدة" }), schedule = child.getByRole("region", { name: "التدريب والمواعيد" });
    await expect(card).toContainText("موعد تعويضي"); await expect(schedule.getByText("حصة تعويضية", { exact: true })).toBeVisible();
    const profile = await placementRead<{ enrollments: { id: string; upcoming: { id: string; sessionDate: string; startTime: string; endTime: string }[] }[] }>(family.request, `/api/v1/guardian/children/${id(80)}/profile`);
    expect(profile.enrollments.find(e => e.id === id(81))?.upcoming).toContainEqual(expect.objectContaining({ id: setup.target.id, sessionDate: setup.date, startTime: "19:00:00", endTime: "20:00:00" }));
    await child.screenshot({ path: info.outputPath("family-compensation-booked.png"), fullPage: true, animations: "disabled" });
    const release = panel.getByRole("button", { name: "تحرير حجز التعويض", exact: true });
    await release.click(); const dialog = page.getByRole("dialog", { name: "تحرير حجز التعويض", exact: true });
    await dialog.getByRole("button", { name: "إلغاء", exact: true }).click(); await expect(dialog).toHaveCount(0); await expect(release).toBeFocused();
    expect((await compensationLedger(page.request)).grants[0].allocated).toBe(1);
    await release.click(); await dialog.press("Escape"); await expect(dialog).toHaveCount(0);
    expect((await compensationLedger(page.request)).grants[0].allocated).toBe(1);
    await release.click(); await dialog.getByRole("button", { name: "تأكيد تحرير الحجز" }).click();
    await expect(panel.getByText("تم تحرير الحجز مع حفظ سجله.", { exact: true })).toBeVisible();
    await schedule.getByRole("button", { name: "تحديث المواعيد" }).click();
    await expect(card.getByText("أُلغي الحجز", { exact: true })).toBeVisible(); await expect(card.getByText("متاح للحجز حصة واحدة", { exact: true })).toBeVisible();
    await expect(schedule.getByText("حصة تعويضية", { exact: true })).toHaveCount(0);
    const updated = await placementRead<{ enrollments: { id: string; upcoming: { id: string }[] }[] }>(family.request, `/api/v1/guardian/children/${id(80)}/profile`);
    expect(updated.enrollments.find(e => e.id === id(81))?.upcoming.some(s => s.id === setup.target.id)).toBe(false);
    await child.screenshot({ path: info.outputPath("family-compensation-released.png"), fullPage: true, animations: "disabled" });
  } finally { await family.close(); }
  const repeatedCancel = await placementWrite(page.request, `/api/v1/people/compensation-bookings/${booking.id}/cancel`, { version: booking.version });
  expect(repeatedCancel.status()).toBe(200); expect((await repeatedCancel.json()).replayed).toBe(true);
  const final = await compensationLedger(page.request);
  expect(final.grants).toHaveLength(1); expect(final.grants[0]).toMatchObject({ units: 1, allocated: 0, consumed: 0 });
  expect(final.grants[0].bookings).toEqual([expect.objectContaining({ id: booking.id, state: "Cancelled" })]);
  expect(final.service).toEqual(before.service); expect(final.collections).toEqual(before.collections); expect(final.historical).toEqual(before.historical);
});
