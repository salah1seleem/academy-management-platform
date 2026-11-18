import { expect, test } from "@playwright/test";
import { creationDayLabel, creationDraft, creationState, fillCreationIdentity, fillCreationSchedule } from "../support/group-creation";
import { afterDays, assertPlacementContrast, assertPlacementViewport, placementEnrollment, placementId as id, placementRead, placementWrite, serviceIdentity, setPlacementAppearance, type PlacementPlayer } from "../support/placement";

test.beforeEach(async ({ page }) => {
  expect((await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" })).status()).toBe(200);
});

test("Owner creates one reviewed group after a lost save reply; Guardian receives the dated session without a new service or collection", async ({ page }, info) => {
  const { draft, coach } = await creationDraft(page.request, 14, [id(101)]);
  const before = await creationState(page.request, draft.timetable.from);
  const child = await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(100)}`);
  expect(placementEnrollment(child, id(101)).trainingGroupId).toBeNull();
  await fillCreationIdentity(page, draft);
  await fillCreationSchedule(page, draft);
  await page.getByRole("button", { name: "متابعة", exact: true }).click();
  await expect(page.getByRole("heading", { name: "اختر لاعبي الانتظار · اختياري" })).toBeVisible();
  const selected = page.getByRole("checkbox", { name: "اختيار لاعب إنشاء مجموعة تجريبي", exact: true });
  await selected.check();
  await page.getByRole("button", { name: "معاينة المجموعة كاملة" }).click();
  const review = page.getByRole("region", { name: "معاينة إنشاء المجموعة" });
  await expect(review).toBeVisible();
  for (const label of ["حصة فعلية", "لاعب مختار"]) await expect(review.locator(".timetable-preview-counts p").filter({ hasText: label }).locator("strong")).toHaveText("1");
  await expect(page.getByRole("button", { name: "تأكيد إنشاء المجموعة" })).toBeEnabled();
  expect(await creationState(page.request, draft.timetable.from)).toEqual(before);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    for (const mode of ["dark", "light"] as const) {
      await setPlacementAppearance(page, mode);
      await page.screenshot({ path: info.outputPath(`group-review-${mode}-${width}.png`), fullPage: true, animations: "disabled" });
      await assertPlacementViewport(page, width);
      await assertPlacementContrast(page.locator(".group-creation"));
    }
  }
  const writes: { key: string | undefined; body: string | null; id: string; replayed: boolean }[] = [];
  await page.route("**/api/v1/manage/group-creation", async route => {
    const response = await route.fetch();
    expect(response.status(), await response.text()).toBe(200);
    const saved = await response.json();
    writes.push({ key: route.request().headers()["idempotency-key"], body: route.request().postData(), id: saved.id, replayed: saved.replayed });
    if (writes.length === 1) await route.abort("failed");
    else await route.fulfill({ response });
  });
  await page.getByRole("button", { name: "تأكيد إنشاء المجموعة" }).click();
  await expect(page.getByRole("alert")).toContainText("تعذر تأكيد الحفظ");
  await expect(review).toBeVisible();
  await expect(page).toHaveURL(/\/dashboard\/academy\/groups\/new$/);
  expect(writes).toHaveLength(1);
  const uncertain = await creationState(page.request, draft.timetable.from);
  expect(uncertain.groups).toHaveLength(before.groups.length + 1);
  expect(uncertain.collections).toEqual(before.collections);
  await page.screenshot({ path: info.outputPath("group-save-unconfirmed.png"), fullPage: true, animations: "disabled" });
  await page.getByRole("button", { name: "تأكيد إنشاء المجموعة" }).click();
  await expect(page).toHaveURL(new RegExp(`/dashboard/academy/groups/${writes[0].id}$`));
  expect(writes).toHaveLength(2);
  expect(writes[0].key).toBeTruthy();
  expect(writes[1]).toEqual({ ...writes[0], replayed: true });
  expect(writes[0].replayed).toBe(false);
  const group = writes[0].id;
  const after = await creationState(page.request, draft.timetable.from);
  expect(after.groups).toHaveLength(before.groups.length + 1);
  expect(after.groups.find(row => row.id === group)).toMatchObject({ arabicName: draft.arabicName, playerCount: 1, coaches: [coach.arabicName] });
  expect(after.sessions.filter(row => row.trainingGroupId === group)).toHaveLength(1);
  const session = after.sessions.find(row => row.trainingGroupId === group)!;
  expect(session).toMatchObject({ sessionDate: draft.timetable.from, startTime: "13:00:00", endTime: "14:00:00" });
  const roster = await placementRead<{ players: { sportEnrollmentId: string }[] }>(page.request, `/api/v1/attendance/sessions/${session.id}/players`);
  expect(roster.players.filter(row => row.sportEnrollmentId === id(101))).toHaveLength(1);
  const playerAfter = await placementRead<PlacementPlayer>(page.request, `/api/v1/people/players/${id(100)}`);
  expect(placementEnrollment(playerAfter, id(101)).trainingGroupId).toBe(group);
  expect(serviceIdentity(playerAfter)).toEqual(serviceIdentity(child));
  expect(after.collections).toEqual(before.collections);
  expect((await placementWrite(page.request, "/api/v1/auth/logout", {})).status()).toBe(204);
  const challenge = await placementWrite(page.request, "/api/v1/auth/guardian/otp/request", { phoneNumber: "01000000001" });
  expect(challenge.status()).toBe(200);
  expect((await placementWrite(page.request, "/api/v1/auth/guardian/otp/verify", { phoneNumber: "01000000001", code: "246810", challengeId: (await challenge.json()).challengeId })).status()).toBe(200);
  const profile = await placementRead<{ enrollments: { id: string; group: string; upcoming: { id: string; sessionDate: string; startTime: string }[] }[] }>(page.request, `/api/v1/guardian/children/${id(100)}/profile`);
  expect(profile.enrollments.find(row => row.id === id(101))).toMatchObject({ group: draft.arabicName, upcoming: [expect.objectContaining({ id: session.id, sessionDate: draft.timetable.from, startTime: "13:00:00" })] });
  await page.goto(`/guardian/children/${id(100)}`);
  await expect(page.getByRole("region", { name: "التدريب والمواعيد" })).toContainText(draft.arabicName);
  await page.screenshot({ path: info.outputPath("family-created-current-group.png"), fullPage: true, animations: "disabled" });
});

test("An empty-group preview recovers a read failure, preserves the draft on back navigation and cancels without any saved group or collection", async ({ page }, info) => {
  const { draft } = await creationDraft(page.request, 15);
  draft.arabicName = "مجموعة انتظار للمعاينة فقط";
  const before = await creationState(page.request, draft.timetable.from);
  await fillCreationIdentity(page, draft);
  await fillCreationSchedule(page, draft);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await page.screenshot({ path: info.outputPath(`group-schedule-${width}.png`), fullPage: true, animations: "disabled" });
    await assertPlacementViewport(page, width);
  }
  await page.getByRole("button", { name: "متابعة", exact: true }).click();
  await expect(page.getByRole("button", { name: "التالي", exact: true })).toBeDisabled();
  await expect(page.getByRole("button", { name: "السابق", exact: true })).toBeDisabled();
  let failed = false;
  await page.route("**/api/v1/manage/group-creation/preview", async route => {
    if (!failed) { failed = true; await route.fulfill({ status: 503, contentType: "application/problem+json", body: JSON.stringify({ title: "Synthetic upstream failure" }) }); }
    else await route.continue();
  });
  await page.getByRole("button", { name: "معاينة المجموعة كاملة" }).click();
  await expect(page.getByRole("alert")).toContainText("تعذر تحميل نتيجة الطلب. أعد المحاولة.");
  await expect(page.getByRole("heading", { name: "اختر لاعبي الانتظار · اختياري" })).toBeVisible();
  await expect(page.getByRole("button", { name: "تأكيد إنشاء المجموعة" })).toHaveCount(0);
  await page.getByRole("button", { name: "معاينة المجموعة كاملة" }).click();
  const review = page.getByRole("region", { name: "معاينة إنشاء المجموعة" });
  await expect(review.locator(".timetable-preview-counts p").filter({ hasText: "لاعب مختار" }).locator("strong")).toHaveText("0");
  await expect(page.getByRole("button", { name: "تأكيد إنشاء المجموعة" })).toBeEnabled();
  await page.getByRole("button", { name: "رجوع للخطوة السابقة" }).click();
  await expect(review).toHaveCount(0);
  await page.getByRole("button", { name: "رجوع للخطوة السابقة" }).click();
  await expect(page.getByLabel("من تاريخ", { exact: true })).toHaveValue(draft.timetable.from);
  await expect(page.getByRole("checkbox", { name: creationDayLabel(draft.timetable.days[0]), exact: true })).toBeChecked();
  await expect(page.getByRole("combobox", { name: "المدرب", exact: true })).toHaveValue(draft.coachMembershipId);
  await page.getByRole("button", { name: "رجوع للخطوة السابقة" }).click();
  await expect(page.getByLabel("اسم المجموعة", { exact: true })).toHaveValue(draft.arabicName);
  await expect(page.getByLabel("السعة", { exact: true })).toHaveValue("2");
  await page.getByRole("button", { name: "إلغاء", exact: true }).click();
  await expect(page).toHaveURL(/\/dashboard\/academy\/groups$/);
  expect(await creationState(page.request, draft.timetable.from)).toEqual(before);
});

test("Required fields, past dates, missing weekdays and reversed times block the wizard before candidate lookup or saving", async ({ page }, info) => {
  const { draft, today } = await creationDraft(page.request, 16);
  const before = await creationState(page.request, draft.timetable.from);
  let candidateReads = 0, writes = 0;
  page.on("request", request => {
    if (request.url().includes("/api/v1/manage/group-creation/candidates")) candidateReads++;
    if (request.url().includes("/api/v1/manage/group-creation") && request.method() === "POST") writes++;
  });
  await page.goto("/dashboard/academy/groups/new");
  await page.getByRole("button", { name: "متابعة", exact: true }).click();
  await expect(page.getByRole("heading", { name: "هوية المجموعة وأهليتها" })).toBeVisible();
  expect(await page.getByLabel("اسم المجموعة", { exact: true }).evaluate((el: HTMLInputElement) => el.validity.valueMissing)).toBe(true);
  await fillCreationIdentity(page, draft);
  await fillCreationSchedule(page, draft);
  const day = page.getByRole("checkbox", { name: creationDayLabel(draft.timetable.days[0]), exact: true });
  await day.uncheck();
  await page.getByRole("button", { name: "متابعة", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("اختر أيام التدريب أولًا");
  await day.check();
  await page.getByLabel("من تاريخ", { exact: true }).fill(afterDays(today, -1));
  await page.getByRole("button", { name: "متابعة", exact: true }).click();
  expect(await page.getByLabel("من تاريخ", { exact: true }).evaluate((el: HTMLInputElement) => el.validity.rangeUnderflow)).toBe(true);
  await expect(page.getByRole("heading", { name: "متى وأين ومع من؟" })).toBeVisible();
  await page.getByLabel("من تاريخ", { exact: true }).fill(draft.timetable.from);
  await page.getByRole("combobox", { name: "وقت النهاية — الساعة", exact: true }).selectOption("1");
  await page.getByRole("button", { name: "متابعة", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("اختر وقت نهاية بعد وقت البداية");
  await page.screenshot({ path: info.outputPath("group-invalid-schedule.png"), fullPage: true, animations: "disabled" });
  expect(candidateReads).toBe(0); expect(writes).toBe(0);
  expect(await creationState(page.request, draft.timetable.from)).toEqual(before);
});
