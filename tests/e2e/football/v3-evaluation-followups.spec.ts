import { expect, test } from '@playwright/test';

test('Owner reviews actual saved draft evidence; only source publication removes the condition', async ({ page }, info) => {
  test.setTimeout(90_000);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/login');
  await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test');
  await page.getByLabel('كلمة المرور').fill('Demo-Only-123!');
  await page.getByRole('button', { name: 'دخول آمن' }).click();
  await page.waitForURL('**/dashboard');
  const options = await (await page.request.get('/api/v1/manage/owner-followups/options')).json();
  // FootballDemoSeed.Id("group/2"): other journeys can add valid candidates.
  const candidate = options.candidates.find((c: { condition: string; source: { groupId: string } }) => c.condition === 'GroupEvaluationDrafts' && c.source.groupId === '428e9073-f93c-14ae-d9d5-c38e50278314');
  expect(candidate).toBeTruthy();
  expect(candidate.source.evaluationDrafts.draftCount).toBe(2);
  expect(candidate.source.evaluationDrafts.playerCount).toBe(1);
  const financial = await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
  await page.goto('/dashboard/operations/follow-ups');
  const card = page.locator('article').filter({ has: page.getByRole('heading', { name: candidate.source.name, exact: true }) }).filter({ hasText: 'مسودات تقييم منذ 7 أيام' });
  if (candidate.activeFollowUpId) await card.getByRole('link', { name: 'فتح المتابعة القائمة' }).click();
  else await card.getByRole('button', { name: 'إنشاء متابعة' }).click();
  await page.waitForURL(/follow-ups\/[a-f0-9-]+$/);
  const url = page.url(), id = url.split('/').at(-1)!;
  const get = async () => (await page.request.get(`/api/v1/manage/owner-followups/${id}`)).json();
  const before = await get(), ids: string[] = before.item.evidence.evaluationIds;
  expect(ids).toHaveLength(2);
  await expect(page.getByText(/2 مسودة · 1 لاعب فريد/)).toBeVisible();
  await expect(page.getByRole('link', { name: 'مراجعة تقييم من دليل الإنشاء 1' })).toHaveAttribute('href', `/dashboard/evaluations/${ids[0]}/edit`);
  await expect(page.getByRole('button', { name: 'نشر التقييم' })).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('evaluation-followup-web-390.png'), fullPage: true, animations: 'disabled' });
  await page.getByLabel('حالة المتابعة').selectOption('Closed');
  await page.getByLabel('سبب إغلاق أو رفض المتابعة').fill('مراجعة بشرية دون نشر؛ سنراجع المصدر الأصلي');
  await page.getByRole('button', { name: 'حفظ المتابعة', exact: true }).click();
  await page.waitForURL('**/operations/follow-ups');
  const closed = await get(); expect(closed.item.state).toBe('Closed'); expect(closed.item.conditionActive).toBe(true);
  for (const draft of ids) expect((await (await page.request.get(`/api/v1/evaluations/${draft}`)).json()).status).toBe('Draft');
  // Existing evaluation editor is the only publisher; follow-up actions do not masquerade as publication.
  for (const draft of ids) {
    await page.goto(url);
    await page.locator(`a[href="/dashboard/evaluations/${draft}/edit"]`).click();
    await page.getByRole('button', { name: 'نشر التقييم', exact: true }).click();
    await page.getByRole('button', { name: 'تأكيد النشر', exact: true }).click();
    await page.waitForURL(`**/evaluations/${draft}/report`);
    expect((await (await page.request.get(`/api/v1/evaluations/${draft}`)).json()).status).toBe('Published');
  }
  const after = await get();
  expect(after.item.state).toBe('Closed'); expect(after.item.conditionActive).toBe(false);
  expect(after.item.evidence).toEqual(before.item.evidence);
  expect(after.history).toEqual(closed.history);
  const current = await (await page.request.get('/api/v1/manage/owner-followups/options')).json();
  expect(current.candidates.filter((c: { condition: string; source: { groupId: string } }) => c.condition === 'GroupEvaluationDrafts' && c.source.groupId === candidate.source.groupId)).toHaveLength(0);
  expect(await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(financial);
  await page.goto(url); await expect(page.getByRole('status').filter({ hasText: 'الحالة الأصلية لم تعد تنطبق' })).toBeVisible();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
