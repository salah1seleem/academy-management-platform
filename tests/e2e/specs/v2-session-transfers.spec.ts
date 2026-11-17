import { expect, test } from '@playwright/test';
import { formatDateAr } from '../../../apps/web/src/lib/formatters';
import { ensurePlacement, placementEnrollment, placementRead, preparePlacementSessions, serviceIdentity } from '../support/placement';
test('Selected session replacement updates the roster and Guardian schedule, not the recurring group or subscription', async ({ page }, info) => {
  const initialCsrf = await page.request.get('/api/v1/auth/csrf').then(r => r.json());
  const login = await page.request.post('/api/v1/auth/login', {
    headers: { 'X-CSRF-TOKEN': initialCsrf.token },
    data: { email: 'owner.nogoom@example.test', password: 'Demo-Only-123!' },
  });
  expect(login.ok(), await login.text()).toBe(true);
  const id = (n: number) => '5a000000-0000-0000-0000-' + String(n).padStart(12, '0');
  const money = await placementRead(page.request, '/api/v1/subscriptions/collections');
  const sessions = await preparePlacementSessions(page.request);
  await ensurePlacement(page.request, 20, 21);
  let detail = await page.request.get(`/api/v1/people/players/${id(20)}`).then(r => r.json());
  const services = serviceIdentity(detail);
  const headers = { 'X-CSRF-TOKEN': (await page.request.get('/api/v1/auth/csrf').then(r => r.json())).token, 'Idempotency-Key': crypto.randomUUID() };
  const subscription = placementEnrollment(detail, id(21)).subscription?.id;
  expect(subscription).toBeTruthy();
  await page.goto(`/dashboard/players/${id(20)}?tab=training`);
  await page.getByText('نقل حصص مختارة', { exact: true }).click();
  if (!placementEnrollment(detail, id(21)).sessionMoves?.length) {
    await page.getByLabel('مجموعة الحصة البديلة', { exact: true }).selectOption(id(50));
    await page.getByRole('checkbox', { name: new RegExp(formatDateAr(sessions.date)) }).check();
    await page.getByRole('combobox', { name: /بديل حصة/ }).selectOption(sessions.target.id);
    await page.getByRole('button', { name: 'معاينة نقل الحصص' }).click();
    const preview = page.getByRole('region', { name: 'معاينة نقل الحصص', exact: true }); await expect(preview).toBeVisible(); await expect(preview.getByRole('button', { name: 'تأكيد نقل الحصص' })).toBeEnabled();
    for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await preview.screenshot({ path: info.outputPath(`session-transfer-${width}.png`), animations: 'disabled' }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
    await preview.getByRole('button', { name: 'تأكيد نقل الحصص' }).click(); await expect(page.getByLabel('حصص منقولة')).toBeVisible();
  }
  detail = await page.request.get(`/api/v1/people/players/${id(20)}`).then(r => r.json());
  expect(placementEnrollment(detail, id(21)).trainingGroupId).toBe(id(1)); expect(placementEnrollment(detail, id(21)).subscription?.id).toBe(subscription); expect(placementEnrollment(detail, id(21)).sessionMoves).toHaveLength(1);
  expect(serviceIdentity(detail)).toEqual(services);
  expect(await placementRead(page.request, '/api/v1/subscriptions/collections')).toEqual(money);
  const source = await page.request.get(`/api/v1/attendance/sessions/${sessions.source.id}/players`).then(r => r.json()); expect(source.players.some((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(21))).toBe(false);
  const target = await page.request.get(`/api/v1/attendance/sessions/${sessions.target.id}/players`).then(r => r.json()); expect(target.players.some((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(21))).toBe(true);
  await page.request.post('/api/v1/auth/logout', { headers }); const csrf = { 'X-CSRF-TOKEN': (await page.request.get('/api/v1/auth/csrf').then(r => r.json())).token };
  const otp = await page.request.post('/api/v1/auth/guardian/otp/request', { headers: csrf, data: { phoneNumber: '01000000001' } }); expect(otp.ok(), await otp.text()).toBe(true);
  const verify = await page.request.post('/api/v1/auth/guardian/otp/verify', { headers: csrf, data: { phoneNumber: '01000000001', code: '246810', challengeId: (await otp.json()).challengeId } }); expect(verify.ok(), await verify.text()).toBe(true);
  await page.goto(`/guardian/children/${id(20)}`); await expect(page.getByLabel('حصص منقولة')).toContainText('نقل السباحة');
  const child = await page.request.get(`/api/v1/guardian/children/${id(20)}/profile`).then(r => r.json()); expect(placementEnrollment(child, id(21)).upcoming?.[0].id).toBe(sessions.target.id); expect(placementEnrollment(child, id(21)).upcoming?.[0].startTime).toBe('17:00:00');
  await page.screenshot({ path: info.outputPath('family-current-session-transfer.png'), fullPage: true, animations: 'disabled' });
});
