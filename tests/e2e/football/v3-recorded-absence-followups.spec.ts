import { expect, test } from '@playwright/test';

test('Recorded absences use saved registers; follow-up closure is not a register change or a message', async ({ page }, info) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/login'); await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test');
  await page.getByLabel('كلمة المرور').fill('Demo-Only-123!'); await page.getByRole('button', { name: 'دخول آمن' }).click();
  await page.waitForURL('**/dashboard');
  const groupId = 'f6814615-520c-adaa-123b-6d8aed2c2eee'; // Explicit optional synthetic fixture, not a first search result.
  const options = await (await page.request.get('/api/v1/manage/owner-followups/options')).json();
  const c = options.candidates.find((c: { condition: string; source: { groupId: string } }) => c.condition === 'GroupRecordedAbsences' && c.source.groupId === groupId);
  expect(c).toBeTruthy(); expect(c.source.recordedAbsences.playerCount).toBe(1);
  const financial = await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
  await page.goto('/dashboard/operations/follow-ups');
  const card = page.locator('article').filter({ has: page.getByRole('heading', { name: c.source.name, exact: true }) }).filter({ hasText: 'غياب متكرر مسجل' });
  if (c.activeFollowUpId) await card.getByRole('link', { name: 'فتح المتابعة القائمة' }).click();
  else await card.getByRole('button', { name: 'إنشاء متابعة' }).click();
  await page.waitForURL(/follow-ups\/[a-f0-9-]+$/);
  const url = page.url(), id = url.split('/').at(-1)!;
  const read = async () => (await page.request.get(`/api/v1/manage/owner-followups/${id}`)).json();
  const before = await read(), records: { AttendanceId: string; SessionId: string }[] = before.item.evidence.absenceEvidence;
  expect(records).toHaveLength(3);
  const registers = async () => Promise.all(records.map(async r => (await (await page.request.get(`/api/v1/attendance/sessions/${r.SessionId}/players`)).json()).players));
  const initial = await registers();
  for (const players of initial) { expect(players).toHaveLength(1); expect(players[0].attendanceStatus).toBe('Absent'); }
  await page.reload(); await expect(page.getByRole('heading', { name: 'غياب متكرر مسجل', exact: true })).toBeVisible();
  await expect(page.getByText(/1 لاعب فريد · 1 تسجيل/)).toBeVisible();
  await expect(page.getByRole('link', { name: /مراجعة كشف/ }).first()).toHaveAttribute('href', `/dashboard/attendance/sessions/${records[0].SessionId}/players`);
  await page.screenshot({ path: info.outputPath('recorded-absence-followup-390.png'), fullPage: true, animations: 'disabled' });
  await page.getByRole('link', { name: /مراجعة كشف/ }).first().click();
  await expect(page.getByRole('heading', { name: 'حضور اللاعبين', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'حفظ الحضور', exact: true })).toHaveCount(0);
  await page.goto(url); await page.getByLabel('حالة المتابعة').selectOption('Closed');
  await page.getByLabel('سبب إغلاق أو رفض المتابعة').fill('مراجعة بشرية دون تغيير حضور أو إرسال تواصل');
  await page.getByRole('button', { name: 'حفظ المتابعة', exact: true }).click(); await page.waitForURL('**/operations/follow-ups');
  const closed = await read(); expect(closed.item.conditionActive).toBe(true); expect(await registers()).toEqual(initial);
  const group = await (await page.request.get(`/api/v1/manage/groups/${groupId}/details`)).json();
  const today = group.sessions.find((s: { sessionDate: string }) => s.sessionDate === options.asOfDate); expect(today).toBeTruthy();
  await page.goto(`/dashboard/attendance/sessions/${today.id}/players`);
  await page.getByRole('button', { name: 'تحديد الحاضرين', exact: true }).click();
  await page.getByRole('button', { name: 'حفظ الحضور', exact: true }).click();
  await expect.poll(async () => (await (await page.request.get(`/api/v1/attendance/sessions/${today.id}/players`)).json()).players[0].attendanceStatus).toBe('Present');
  const after = await read(); expect(after.item.conditionActive).toBe(false); expect(after.item.state).toBe('Closed');
  expect(after.item.evidence).toEqual(before.item.evidence); expect(after.history).toEqual(closed.history);
  expect(await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(financial);
  await page.goto(url); await expect(page.getByRole('status').filter({ hasText: 'الحالة الأصلية لم تعد تنطبق' })).toBeVisible();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
