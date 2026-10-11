import { expect, test } from '@playwright/test';
test('Approved compensation books a real session and is visible to its linked Guardian without new collection', async ({ page }, info) => {
  const id = (n: number) => '5a000000-0000-0000-0000-' + String(n).padStart(12, '0');
  await page.goto('/login'); await page.getByLabel('البريد الإلكتروني').fill('owner.nogoom@example.test'); await page.getByLabel('كلمة المرور').fill('Demo-Only-123!'); await page.getByRole('button', { name: 'دخول آمن' }).click(); await page.waitForURL('**/dashboard');
  const before = await page.request.get(`/api/v1/people/players/${id(80)}`).then(r => r.json());
  await page.goto(`/dashboard/players/${id(80)}?tab=training`); await page.getByText('التعويضات والحصص البديلة', { exact: true }).click();
  let grants = await page.request.get(`/api/v1/people/enrollments/${id(81)}/compensations`).then(r => r.json());
  if (!grants.length) {
    await page.getByLabel('الحصة الأصلية', { exact: true }).selectOption(id(71)); await page.getByLabel('صالح إلى', { exact: true }).fill('2026-12-01');
    await page.getByRole('button', { name: 'معاينة استحقاق التعويض' }).click(); await page.getByRole('button', { name: 'تأكيد استحقاق التعويض' }).click(); await expect(page.getByText('تم اعتماد التعويض دون تحصيل أو تمديد اشتراك.', { exact: true })).toBeVisible();
    grants = await page.request.get(`/api/v1/people/enrollments/${id(81)}/compensations`).then(r => r.json());
  }
  if (!grants[0].bookings.some((b: { state: string }) => b.state === 'Reserved')) {
    await page.getByRole('combobox', { name: 'التعويض', exact: true }).selectOption(grants[0].id); await page.getByRole('combobox', { name: 'الحصة التعويضية', exact: true }).selectOption(id(91)); await page.getByRole('button', { name: 'معاينة حجز التعويض' }).click();
    const review = page.getByRole('region', { name: 'معاينة التعويض', exact: true }); await expect(review.getByRole('button', { name: 'تأكيد حجز التعويض' })).toBeEnabled();
    for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await review.screenshot({ path: info.outputPath(`compensation-${width}.png`), animations: 'disabled' }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
    await review.getByRole('button', { name: 'تأكيد حجز التعويض' }).click(); await expect(page.getByText('تم حجز الحصة التعويضية وتحديث مواعيد ولي الأمر.', { exact: true })).toBeVisible();
  }
  const after = await page.request.get(`/api/v1/people/players/${id(80)}`).then(r => r.json()); expect(after.enrollments[0].subscription?.id).toBe(before.enrollments[0].subscription?.id); expect(after.enrollments[0].trainingGroupId).toBe(id(70));
  const roster = await page.request.get(`/api/v1/attendance/sessions/${id(91)}/players`).then(r => r.json()); expect(roster.players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(81)).compensatory).toBe(true);
  const headers = { 'X-CSRF-TOKEN': (await page.request.get('/api/v1/auth/csrf').then(r => r.json())).token }; await page.request.post('/api/v1/auth/logout', { headers });
  const csrf = { 'X-CSRF-TOKEN': (await page.request.get('/api/v1/auth/csrf').then(r => r.json())).token }; const otp = await page.request.post('/api/v1/auth/guardian/otp/request', { headers: csrf, data: { phoneNumber: '01000000001' } }); expect(otp.ok(), await otp.text()).toBe(true);
  const verify = await page.request.post('/api/v1/auth/guardian/otp/verify', { headers: csrf, data: { phoneNumber: '01000000001', code: '246810', challengeId: (await otp.json()).challengeId } }); expect(verify.ok(), await verify.text()).toBe(true);
  await page.goto(`/guardian/children/${id(80)}`); await expect(page.getByLabel('التعويضات المعتمدة')).toContainText('السباحة'); await expect(page.getByLabel('التعويضات المعتمدة')).toContainText('موعد تعويضي');
  const child = await page.request.get(`/api/v1/guardian/children/${id(80)}/profile`).then(r => r.json()); expect(child.enrollments[0].upcoming[0].id).toBe(id(91)); expect(child.compensations[0].consumed).toBe(0); expect(child.compensations[0].allocated).toBe(1);
});
