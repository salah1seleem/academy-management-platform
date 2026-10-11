import { expect, test } from '@playwright/test';
test('Selected session replacement updates the roster and Guardian schedule, not the recurring group or subscription', async ({ page }, info) => {
  await page.goto('/login'); await page.getByLabel('البريد الإلكتروني').fill('owner.nogoom@example.test'); await page.getByLabel('كلمة المرور').fill('Demo-Only-123!'); await page.getByRole('button', { name: 'دخول آمن' }).click(); await page.waitForURL('**/dashboard');
  const id = (n: number) => '5a000000-0000-0000-0000-' + String(n).padStart(12, '0');
  let detail = await page.request.get(`/api/v1/people/players/${id(20)}`).then(r => r.json());
  const headers = { 'X-CSRF-TOKEN': (await page.request.get('/api/v1/auth/csrf').then(r => r.json())).token, 'Idempotency-Key': crypto.randomUUID() };
  if (!detail.enrollments[0].trainingGroupId) {
    const preview = await page.request.post(`/api/v1/manage/groups/${id(1)}/placement/preview`, { headers, data: { enrollmentIds: [id(21)] } }); expect(preview.ok(), await preview.text()).toBe(true);
    const placed = await page.request.post(`/api/v1/manage/groups/${id(1)}/placement`, { headers, data: { enrollmentIds: [id(21)], fingerprint: (await preview.json()).fingerprint } }); expect(placed.ok(), await placed.text()).toBe(true);
  }
  const subscription = detail.enrollments[0].subscription?.id;
  await page.goto(`/dashboard/players/${id(20)}?tab=training`);
  await page.getByText('نقل حصص مختارة', { exact: true }).click();
  if (!detail.enrollments[0].sessionMoves?.length) {
    await page.getByLabel('مجموعة الحصة البديلة', { exact: true }).selectOption(id(50));
    await page.getByRole('checkbox', { name: /تسكين السباحة/ }).check();
    await page.getByLabel(`بديل الحصة ${id(4)}`, { exact: true }).selectOption(id(51));
    await page.getByRole('button', { name: 'معاينة نقل الحصص' }).click();
    const preview = page.getByRole('region', { name: 'معاينة نقل الحصص', exact: true }); await expect(preview).toBeVisible(); await expect(preview.getByRole('button', { name: 'تأكيد نقل الحصص' })).toBeEnabled();
    for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await preview.screenshot({ path: info.outputPath(`session-transfer-${width}.png`), animations: 'disabled' }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
    await preview.getByRole('button', { name: 'تأكيد نقل الحصص' }).click(); await expect(page.getByLabel('حصص منقولة')).toBeVisible();
  }
  detail = await page.request.get(`/api/v1/people/players/${id(20)}`).then(r => r.json());
  expect(detail.enrollments[0].trainingGroupId).toBe(id(1)); expect(detail.enrollments[0].subscription?.id).toBe(subscription); expect(detail.enrollments[0].sessionMoves).toHaveLength(1);
  const source = await page.request.get(`/api/v1/attendance/sessions/${id(4)}/players`).then(r => r.json()); expect(source.players.some((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(21))).toBe(false);
  const target = await page.request.get(`/api/v1/attendance/sessions/${id(51)}/players`).then(r => r.json()); expect(target.players.some((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(21))).toBe(true);
  await page.request.post('/api/v1/auth/logout', { headers }); const csrf = { 'X-CSRF-TOKEN': (await page.request.get('/api/v1/auth/csrf').then(r => r.json())).token };
  const otp = await page.request.post('/api/v1/auth/guardian/otp/request', { headers: csrf, data: { phoneNumber: '01000000001' } }); expect(otp.ok(), await otp.text()).toBe(true);
  const verify = await page.request.post('/api/v1/auth/guardian/otp/verify', { headers: csrf, data: { phoneNumber: '01000000001', code: '246810', challengeId: (await otp.json()).challengeId } }); expect(verify.ok(), await verify.text()).toBe(true);
  await page.goto(`/guardian/children/${id(20)}`); await expect(page.getByLabel('حصص منقولة')).toContainText('نقل السباحة');
  const child = await page.request.get(`/api/v1/guardian/children/${id(20)}/profile`).then(r => r.json()); expect(child.enrollments[0].upcoming[0].id).toBe(id(51)); expect(child.enrollments[0].upcoming[0].startTime).toBe('17:00:00');
});
