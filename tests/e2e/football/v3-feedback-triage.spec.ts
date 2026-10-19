import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
test('Family safety intake reaches the owner human queue, survives reclassification and resolves only after explicit review', async ({ page, browser }, info) => {
  test.setTimeout(90_000); await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/login'); await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test'); await page.getByLabel('كلمة المرور').fill('Demo-Only-123!'); await page.getByRole('button', { name: 'دخول آمن' }).click(); await page.waitForURL('**/dashboard');
  const financial = await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
  const family = await browser.newContext({ baseURL: new URL(info.project.use.baseURL!).origin, viewport: { width: 390, height: 844 } });
  try {
    const post = async (path: string, data: object) => { const csrf = (await (await family.request.get('/api/v1/auth/csrf')).json()).token; const r = await family.request.post(path, { data, headers: { 'X-CSRF-TOKEN': csrf } }); expect(r.ok(), await r.text()).toBe(true); return r.json(); };
    const phoneNumber = '01099900101'; const challenge = await post('/api/v1/auth/guardian/otp/request', { phoneNumber }); await post('/api/v1/auth/guardian/otp/verify', { phoneNumber, challengeId: challenge.challengeId, code: '246810' });
    const child = (await (await family.request.get('/api/v1/guardian/children')).json())[0].id; const options = await (await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json();
    const guardian = await family.newPage(); await guardian.goto(`/guardian/children/${child}`); await guardian.getByRole('link', { name: /ملاحظتك للإدارة/ }).click();
    await guardian.getByLabel('المجموعة').selectOption(options.groups[0].id); await guardian.getByLabel('موضوع الملاحظة').selectOption('Water'); await guardian.getByLabel('درجة الاستعجال').selectOption('Safety'); await expect(guardian.getByLabel('درجة الاستعجال')).toHaveValue('Safety'); await guardian.getByLabel('تاريخ الواقعة (اختياري)').fill(options.asOfDate);
    await expect(guardian.getByRole('note')).toContainText('ليست طوارئ'); const message = `بلاغ سلامة اصطناعي للاختبار فقط ${randomUUID()}`; await guardian.getByLabel('اكتب الملاحظة').fill(message);
    await guardian.screenshot({ path: info.outputPath('guardian-safety-intake-390.png'), fullPage: true, animations: 'disabled' });
    const response = guardian.waitForResponse(r => r.url().endsWith(`/children/${child}/feedback`) && r.request().method() === 'POST'); await guardian.getByRole('button', { name: 'إرسال للإدارة', exact: true }).click(); const r = await response; expect(r.status()).toBe(201); const id = (await r.json()).id;
    await expect(guardian.getByRole('status').filter({ hasText: 'وصل بلاغ السلامة' })).toBeVisible();
    await page.reload(); const safetyLink = page.getByRole('region', { name: 'بلاغات السلامة' }).getByRole('link'); await expect(safetyLink).toContainText('بلاغ سلامة مفتوح'); await safetyLink.click(); await expect(page.getByLabel('مسار السلامة')).toHaveValue('Safety');
    await page.locator(`a[href="/dashboard/feedback/${id}"]`).click(); await expect(page.getByRole('button', { name: 'إرسال الرد وحل الملاحظة', exact: true })).toBeDisabled();
    await page.getByLabel('موضوع المراجعة').selectOption('Facility'); await page.getByLabel('درجة الاستعجال المراجعة').selectOption('Routine'); const internal = `فرز داخلي اصطناعي لا يظهر للأسرة ${id}`; await page.getByLabel('ملاحظة فرز داخلية (اختياري)').fill(internal); await page.getByRole('button', { name: 'تأكيد الفرز البشري', exact: true }).click();
    await expect(page.getByText(/تم تأكيد الفرز البشري وحفظ/)).toBeVisible(); const source = await (await page.request.get(`/api/v1/manage/feedback/${id}`)).json(); expect(source).toMatchObject({ subject: 'Facility', urgency: 'Routine', reportedSubject: 'Water', reportedUrgency: 'Safety', safety: true }); expect(source.triagedAtUtc).not.toBeNull();
    await page.screenshot({ path: info.outputPath('owner-human-triage-390.png'), fullPage: true, animations: 'disabled' });
    const list = await (await page.request.get('/api/v1/manage/feedback?safetyOnly=true&status=Unresolved')).json(); expect(list.some((x: {id:string})=>x.id===id)).toBe(true);
    const reply = `رد عام اصطناعي بعد مراجعة بشرية ${id}`; await page.getByLabel('الرد على الملاحظة').fill(reply); await page.getByRole('button', { name: 'إرسال الرد وحل الملاحظة', exact: true }).click(); await expect(page.getByText('الحالة: تم الرد والحل')).toBeVisible();
    await guardian.reload(); await expect(guardian.getByText(`رد الإدارة: ${reply}`, { exact: true })).toBeVisible(); await expect(guardian.getByText(internal, { exact: true })).toHaveCount(0); expect((await family.request.get(`/api/v1/manage/feedback/${id}`)).status()).toBe(403);
    const mine = await (await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json(); expect(mine.mine.find((x: {id:string})=>x.id===id)).toMatchObject({ reportedUrgency: 'Safety', reportedSubject: 'Water', status: 'Resolved' }); expect(JSON.stringify(mine)).not.toContain(internal);
    expect(await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(financial);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  } finally { await family.close(); }
});
