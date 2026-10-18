import { expect, test } from '@playwright/test';

test('Owner follows overdue notes, replies from each actual source and only the linked guardian receives the response', async ({ page, browser }, info) => {
  test.setTimeout(90_000); await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/login'); await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test'); await page.getByLabel('كلمة المرور').fill('Demo-Only-123!');
  await page.getByRole('button', { name: 'دخول آمن' }).click(); await page.waitForURL('**/dashboard');
  const options = await (await page.request.get('/api/v1/manage/owner-followups/options')).json();
  const groupId = '428e9073-f93c-14ae-d9d5-c38e50278314';
  const candidate = options.candidates.find((c: { condition: string; source: { groupId: string } }) => c.condition === 'GroupOverdueFeedback' && c.source.groupId === groupId);
  expect(candidate).toBeTruthy(); expect(candidate.source.overdueFeedback).toMatchObject({ complaintCount: 1, suggestionCount: 1, submitterCount: 1 });
  const financial = await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
  await page.goto('/dashboard/operations/follow-ups');
  const card = page.locator('article').filter({ has: page.getByRole('heading', { name: candidate.source.name, exact: true }) }).filter({ hasText: 'ملاحظات تجاوزت موعد المتابعة' });
  await card.getByRole('button', { name: 'إنشاء متابعة' }).click(); await page.waitForURL(/follow-ups\/[a-f0-9-]+$/);
  const url = page.url(), id = url.split('/').at(-1)!;
  const read = async () => (await page.request.get(`/api/v1/manage/owner-followups/${id}`)).json();
  const before = await read(), ids: string[] = before.item.evidence.feedbackIds; expect(ids).toHaveLength(2);
  await page.getByLabel('حالة المتابعة').selectOption('Closed'); await page.getByLabel('سبب إغلاق أو رفض المتابعة').fill('إغلاق المهمة البشرية فقط؛ نراجع الشكوى من مصدرها');
  await page.getByRole('button', { name: 'حفظ المتابعة', exact: true }).click(); await page.waitForURL('**/operations/follow-ups');
  const closed = await read(); expect(closed.item.conditionActive).toBe(true);
  let child = '';
  for (const note of ids) {
    await page.goto(url); await page.locator(`a[href="/dashboard/feedback/${note}"]`).click();
    await expect(page.getByRole('heading', { name: 'متابعة ملاحظة الأسرة', exact: true })).toBeVisible();
    const source = await (await page.request.get(`/api/v1/manage/feedback/${note}`)).json(); child = source.playerId;
    expect(source.status).not.toBe('Resolved');
    await expect(page.getByRole('link', { name: 'ملاحظات المجموعة نفسها' })).toHaveAttribute('href', `/dashboard/feedback?groupId=${groupId}`);
    if (source.status === 'Open') { await page.getByRole('button', { name: 'تسجيل المراجعة', exact: true }).click(); await expect(page.getByText('الحالة: تمت المراجعة')).toBeVisible(); }
    await page.screenshot({ path: info.outputPath(`family-feedback-${note}-390.png`), fullPage: true, animations: 'disabled' });
    await page.getByLabel('الرد على الملاحظة').fill('رد تجريبي محفوظ للأسرة من صفحة الملاحظة نفسها');
    await page.getByRole('button', { name: 'إرسال الرد وحل الملاحظة', exact: true }).click(); await expect(page.getByText('الحالة: تم الرد والحل')).toBeVisible();
  }
  const after = await read(); expect(after.item.state).toBe('Closed'); expect(after.item.conditionActive).toBe(false); expect(after.item.evidence).toEqual(before.item.evidence); expect(after.history).toEqual(closed.history);
  expect(await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(financial);
  const context = await browser.newContext({ baseURL: new URL(page.url()).origin });
  try {
    const post = async (path: string, data: object) => {
      const csrf = await (await context.request.get('/api/v1/auth/csrf')).json();
      const r = await context.request.post(path, { data, headers: { 'X-CSRF-TOKEN': csrf.token } }); expect(r.ok(), await r.text()).toBe(true); return r.json();
    };
    const phoneNumber = '01099900109';
    const challenge = await post('/api/v1/auth/guardian/otp/request', { phoneNumber });
    await post('/api/v1/auth/guardian/otp/verify', { phoneNumber, challengeId: challenge.challengeId, code: '246810' });
    const mine = await (await context.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json();
    for (const note of ids) expect(mine.mine.find((n: { id: string }) => n.id === note)).toMatchObject({ status: 'Resolved', ownerResponse: 'رد تجريبي محفوظ للأسرة من صفحة الملاحظة نفسها' });
    expect((await context.request.get(`/api/v1/manage/feedback/${ids[0]}`)).status()).toBe(403);
  } finally { await context.close(); }
  await page.goto(`/dashboard/feedback?groupId=${groupId}`); await expect(page.getByText('ملاحظات المجموعة المختارة فقط', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'مسح', exact: true }).click(); await expect(page.getByText('ملاحظات المجموعة المختارة فقط', { exact: false })).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
