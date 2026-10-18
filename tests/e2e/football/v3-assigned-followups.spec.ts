import { expect, test, type Page } from "@playwright/test";

test("Owner delegates a scoped follow-up; administrator updates, closes and loses live access without changing money", async ({ page, browser }, info) => {
  test.setTimeout(120_000);
  const login = async (p: Page, email: string) => {
    await p.goto('/login'); await p.getByLabel('البريد الإلكتروني').fill(email);
    await p.getByLabel('كلمة المرور').fill('Demo-Only-123!');
    await p.getByRole('button', { name: 'دخول آمن' }).click(); await p.waitForURL('**/dashboard');
  };
  await login(page, 'owner.football@example.test');
  const headers = { 'X-CSRF-TOKEN': (await (await page.request.get('/api/v1/auth/csrf')).json()).token };
  const email = `assigned-${crypto.randomUUID()}@example.test`;
  const created = await page.request.post('/api/v1/manage/staff/', { headers, data: {
    name: 'مسؤول متابعة اصطناعي', email, phone: `010999${String(Date.now()).slice(-5)}`,
    temporaryPassword: 'Demo-Only-123!', role: 'AcademyAdmin', jobTitle: 'متابعة تشغيل',
  } }); expect(created.ok(), await created.text()).toBe(true);
  const member = (await created.json()).id;
  const access = await (await page.request.get(`/api/v1/manage/staff/${member}/access`)).json();
  const options = await (await page.request.get('/api/v1/manage/owner-followups/options')).json();
  const candidate = options.candidates.find((c: { condition: string; activeFollowUpId: string | null }) => c.condition === 'GroupLowOccupancy' && !c.activeFollowUpId);
  expect(candidate).toBeTruthy();
  const group = await (await page.request.get(`/api/v1/manage/groups/${candidate.source.groupId}/details`)).json();
  // Existing staff API establishes the fixture; assignment and lifecycle below use real screens.
  const scope = { allBranches: false, branches: [group.group.branchId], allSports: false, sports: [group.group.sportId],
    actions: access.modules.map((module: string) => ({ module, read: module === 'followups', add: false, edit: module === 'followups', delete: false })) };
  const grant = await page.request.put(`/api/v1/manage/staff/${member}/access`, { headers, data: scope }); expect(grant.status(), await grant.text()).toBe(204);
  const financial = await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
  await page.goto('/dashboard/operations/follow-ups');
  const card = page.locator('article').filter({ has: page.getByRole('heading', { name: candidate.source.name, exact: true }) }).filter({ hasText: 'إشغال أقل من نصف السعة' });
  await card.getByRole('button', { name: 'إنشاء متابعة' }).click();
  await page.waitForURL(/follow-ups\/[a-f0-9-]+$/); const ownerUrl = page.url(), id = ownerUrl.split('/').at(-1)!;
  await page.getByLabel('حالة المتابعة').selectOption('Assigned');
  await page.getByRole('combobox', { name: 'المسؤول', exact: true }).selectOption(member);
  await page.getByRole('button', { name: 'حفظ المتابعة', exact: true }).click(); await page.waitForURL('**/operations/follow-ups');

  const context = await browser.newContext({ viewport: { width: 390, height: 844 } }); const admin = await context.newPage();
  try {
    await login(admin, email); await admin.goto('/dashboard/my-followups');
    await expect(admin.getByRole('heading', { name: 'متابعاتي المسندة', exact: true })).toBeVisible();
    await admin.getByRole('button', { name: 'مسح', exact: true }).click();
    await expect(admin.getByLabel('من تاريخ')).toHaveValue('');
    await admin.locator(`a[href="/dashboard/my-followups/${id}"]`).click();
    await expect(admin.getByRole('heading', { name: candidate.source.name, exact: true })).toBeVisible();
    await expect(admin.getByRole('link', { name: 'فتح المجموعة نفسها' })).toHaveCount(0);
    await expect(admin.getByLabel('المسؤول', { exact: true })).toHaveCount(0);
    await admin.getByLabel('ملاحظة اختيارية').fill('مراجعة تشغيل اصطناعية من المسؤول المسند');
    await admin.getByRole('button', { name: 'حفظ تحديث المتابعة', exact: true }).click();
    await expect(admin.getByRole('status').filter({ hasText: 'تم تحديث المتابعة' })).toBeVisible();
    await admin.getByLabel('حالة المتابعة').selectOption('Closed');
    await admin.getByLabel('سبب إغلاق أو رفض المتابعة').fill('أغلقت المتابعة البشرية فقط؛ لم يتغير مصدر الحالة');
    await admin.getByRole('button', { name: 'حفظ تحديث المتابعة', exact: true }).click();
    await expect(admin.getByRole('status').filter({ hasText: 'للقراءة فقط' })).toBeVisible();
    await expect(admin.getByRole('heading', { name: 'سجل المتابعة' })).toBeVisible();
    await expect.poll(() => admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await admin.screenshot({ path: info.outputPath('assigned-followup-detail-390.png'), fullPage: true, animations: 'disabled' });
    await page.goto(ownerUrl); await expect(page.getByText('مراجعة تشغيل اصطناعية من المسؤول المسند').first()).toBeVisible();
    expect(await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(financial);
    expect(await (await page.request.get(`/api/v1/manage/groups/${candidate.source.groupId}/details`)).json()).toEqual(group);
    const revoke = await page.request.put(`/api/v1/manage/staff/${member}/access`, { headers, data: { ...scope, actions: scope.actions.map((a: object) => ({ ...a, read: false, edit: false })) } }); expect(revoke.status()).toBe(204);
    await admin.getByRole('button', { name: 'تحديث المتابعات', exact: true }).click();
    await expect(admin.getByRole('alert').filter({ hasText: 'غير متاحة' })).toBeVisible();
    await expect(admin.getByRole('heading', { name: candidate.source.name, exact: true })).toHaveCount(0);
  } finally { await context.close(); }
});
