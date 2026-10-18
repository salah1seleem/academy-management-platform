import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';

test('Private complaint lifecycle survives close and reopen, while the family receives public responses only', async ({ page, browser }, info) => {
  test.setTimeout(90_000); await page.setViewportSize({ width: 390, height: 844 });
  const family = await browser.newContext({ baseURL: new URL(info.project.use.baseURL!).origin });
  try {
    const post = async (path: string, data: object, key?: string) => {
      const csrf = await (await family.request.get('/api/v1/auth/csrf')).json();
      const r = await family.request.post(path, { data, headers: { 'X-CSRF-TOKEN': csrf.token, ...(key ? { 'Idempotency-Key': key } : {}) } }); expect(r.ok(), await r.text()).toBe(true); return r.json();
    };
    const phoneNumber = '01099900101';
    const challenge = await post('/api/v1/auth/guardian/otp/request', { phoneNumber });
    await post('/api/v1/auth/guardian/otp/verify', { phoneNumber, challengeId: challenge.challengeId, code: '246810' });
    const children = await (await family.request.get('/api/v1/guardian/children')).json();
    const child = children[0].id;
    const options = await (await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json();
    const group = options.groups[0]; expect(group).toBeTruthy();
    const made = await post(`/api/v1/guardian/children/${child}/feedback`, { trainingGroupId: group.id, coachMembershipId: null, kind: 'Complaint', score: null, message: 'شكوى اصطناعية لرحلة المعالجة الخاصة دون أي تواصل خارجي' }, randomUUID());
    const id = made.id, path = `/api/v1/manage/feedback/${id}`;
    await page.goto('/login'); await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test'); await page.getByLabel('كلمة المرور').fill('Demo-Only-123!'); await page.getByRole('button', { name: 'دخول آمن' }).click(); await page.waitForURL('**/dashboard');
    const financial = await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
    await page.goto(`/dashboard/feedback/${id}`);
    const read = async () => (await page.request.get(path)).json();
    const move = async (status: string, note = '') => {
      await page.getByLabel('الخطوة التالية').selectOption(status); await page.getByLabel('ملاحظة داخلية (اختياري)').fill(note); await page.getByRole('button', { name: 'حفظ مرحلة المعالجة', exact: true }).click();
      await expect.poll(async () => (await read()).status).toBe(status);
      await expect(page.getByLabel('الخطوة التالية')).toHaveValue('');
    };
    await move('InProgress', 'دليل تحقيق داخلي اصطناعي للمالك فقط');
    await move('AwaitingInfo');
    const waiting = await read(); expect(waiting.history).toHaveLength(2);
    await page.getByText('سجل الملاحظة الخاص', { exact: true }).scrollIntoViewIfNeeded();
    await expect(page.getByText('ملاحظة داخلية: دليل تحقيق داخلي اصطناعي للمالك فقط')).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath('private-case-history-390.png'), fullPage: true, animations: 'disabled' });
    let mine = await (await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json();
    const familyCase = mine.mine.find((x: {id:string})=>x.id===id); expect(familyCase.status).toBe('AwaitingInfo'); expect(familyCase).not.toHaveProperty('history'); expect(familyCase).not.toHaveProperty('internalNote'); expect(JSON.stringify(mine)).not.toContain('دليل تحقيق داخلي');
    expect((await family.request.get(path)).status()).toBe(403);
    await page.getByLabel('الرد على الملاحظة').fill('رد عام محفوظ للأسرة بعد مراجعة تنظيم التدريب'); await page.getByRole('button', { name: 'إرسال الرد وحل الملاحظة', exact: true }).click();
    await expect.poll(async()=> (await read()).status).toBe('Resolved');
    await move('Closed'); await expect(page.getByRole('button', { name: 'إرسال الرد وحل الملاحظة' })).toHaveCount(0);
    await move('Reopened', 'مراجعة داخلية إضافية دون مسح الرد الأول');
    await page.getByLabel('الرد على الملاحظة').fill('رد عام ثانٍ بعد استكمال المراجعة الاصطناعية'); await page.getByRole('button', { name: 'إرسال الرد وحل الملاحظة', exact: true }).click();
    await expect.poll(async()=> (await read()).status).toBe('Resolved');
    const final = await read(); expect(final.history).toHaveLength(6); expect(final.history.filter((c:{action:string})=>c.action==='Resolve')).toHaveLength(2);
    mine = await (await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json(); expect(mine.mine.find((x:{id:string})=>x.id===id)).toMatchObject({status:'Resolved',ownerResponse:'رد عام ثانٍ بعد استكمال المراجعة الاصطناعية'});
    expect(await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(financial);
    await page.goto(`/dashboard/feedback?status=Closed`); await expect(page.getByRole('combobox',{name:'الحالة'})).toHaveValue('Closed');
  } finally { await family.close(); }
});
