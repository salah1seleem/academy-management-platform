import { expect, test } from '@playwright/test';
import { todaySession } from '../support/attendance';

test('Admin records late, corrects to excused, and Guardian sees the exact saved states without duplicate debit', async ({ page }, info) => {
  const id = (n: number) => '5a000000-0000-0000-0000-' + String(n).padStart(12, '0');
  await page.goto('/login'); await page.getByLabel('البريد الإلكتروني').fill('admin.nogoom@example.test'); await page.getByLabel('كلمة المرور').fill('Demo-Only-123!'); await page.getByRole('button', { name: 'دخول آمن' }).click(); await page.waitForURL('**/dashboard');
  const session = await todaySession(page, id(130), '04:30', '04:45');
  const path = `/api/v1/attendance/sessions/${session}/players`;
  const write = async (state: number) => { const roster = await page.request.get(path).then(r => r.json()); const row = roster.players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(111)); const csrf = await page.request.get('/api/v1/auth/csrf').then(r => r.json()); const response = await page.request.put(path, { headers: { 'X-CSRF-TOKEN': csrf.token }, data: { items: [{ sportEnrollmentId: id(111), status: state, expectedVersion: row.version }] } }); expect(response.ok(), await response.text()).toBe(true); };
  // Correct only this opt-in synthetic fixture. No database reset or unrelated records.
  await write(1);
  const baseline = (await page.request.get(path).then(r => r.json())).players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(111)).remainingSessions;
  await page.goto(`/dashboard/attendance/sessions/${session}/players`);
  const player = page.getByRole('group', { name: 'حضور لاعب حالات حضور اصطناعي 110', exact: true });
  await player.getByRole('button', { name: 'متأخر', exact: true }).click(); await page.getByRole('button', { name: 'حفظ الحضور', exact: true }).click(); await expect(page.getByText('تم حفظ الحضور بنجاح.', { exact: true })).toBeVisible();
  let snapshot = await page.request.get(path).then(r => r.json()); expect(snapshot.players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(111)).remainingSessions).toBe(baseline - 1); expect(snapshot.lastSynchronizedAtUtc).toBeTruthy();
  await player.getByRole('button', { name: 'حاضر', exact: true }).click(); await page.getByRole('button', { name: 'حفظ الحضور', exact: true }).click(); await expect(player.getByRole('button', { name: 'حاضر', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await expect.poll(async () => (await page.request.get(path).then(r => r.json())).players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(111)).attendanceStatus).toBe('Present');
  expect((await page.request.get(path).then(r => r.json())).players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(111)).remainingSessions).toBe(baseline - 1);
  await player.getByRole('button', { name: 'معتذر', exact: true }).click(); await page.getByRole('button', { name: 'حفظ الحضور', exact: true }).click(); await expect.poll(async () => (await page.request.get(path).then(r => r.json())).players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(111)).attendanceStatus).toBe('Excused');
  snapshot = await page.request.get(path).then(r => r.json()); expect(snapshot.players.find((p: { sportEnrollmentId: string }) => p.sportEnrollmentId === id(111)).remainingSessions).toBe(baseline);
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 900 }); await page.screenshot({ path: info.outputPath(`attendance-states-${width}.png`), fullPage: true, animations: 'disabled' }); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); }
  const csrf = { 'X-CSRF-TOKEN': (await page.request.get('/api/v1/auth/csrf').then(r => r.json())).token }; await page.request.post('/api/v1/auth/logout', { headers: csrf }); const token = { 'X-CSRF-TOKEN': (await page.request.get('/api/v1/auth/csrf').then(r => r.json())).token }; const otp = await page.request.post('/api/v1/auth/guardian/otp/request', { headers: token, data: { phoneNumber: '01000000001' } }); expect(otp.ok()).toBe(true); const verified = await page.request.post('/api/v1/auth/guardian/otp/verify', { headers: token, data: { phoneNumber: '01000000001', code: '246810', challengeId: (await otp.json()).challengeId } }); expect(verified.ok()).toBe(true);
  const attendance = await page.request.get(`/api/v1/guardian/children/${id(110)}/attendance`).then(r => r.json()); expect(attendance.find((row: { startTime: string }) => row.startTime === '04:30:00')?.status).toBe('Excused');
  await page.goto(`/guardian/children/${id(110)}`); await expect(page.getByText('لاعب حالات حضور اصطناعي 110').first()).toBeVisible();
  expect((await page.request.put(path, { headers: token, data: { items: [{ sportEnrollmentId: id(111), status: 4, expectedVersion: 0 }] } })).status()).toBe(403);
});
