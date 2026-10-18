import { expect, test, type Page } from '@playwright/test';
import { randomUUID } from 'node:crypto';

test('Owner reviews a minimal brief, scoped administrator returns private findings and loses cancelled access', async ({ page, browser }, info) => {
  test.setTimeout(90_000); await page.setViewportSize({ width: 390, height: 844 });
  const origin = new URL(info.project.use.baseURL!).origin;
  const family = await browser.newContext({ baseURL: origin }); const delegated = await browser.newContext({ baseURL: origin, viewport: { width: 390, height: 844 } });
  const login = async (p: Page, email: string) => { await p.goto('/login'); await p.getByLabel('البريد الإلكتروني').fill(email); await p.getByLabel('كلمة المرور').fill('Demo-Only-123!'); await p.getByRole('button', { name: 'دخول آمن' }).click(); await p.waitForURL('**/dashboard'); };
  try {
    const postFamily = async (path: string, data: object, key?: string) => {
      const token = (await (await family.request.get('/api/v1/auth/csrf')).json()).token;
      const r = await family.request.post(path, { data, headers: { 'X-CSRF-TOKEN': token, ...(key ? { 'Idempotency-Key': key } : {}) } }); expect(r.ok(), await r.text()).toBe(true); return r.json();
    };
    const phoneNumber = '01099900102'; const challenge = await postFamily('/api/v1/auth/guardian/otp/request', { phoneNumber }); await postFamily('/api/v1/auth/guardian/otp/verify', { phoneNumber, challengeId: challenge.challengeId, code: '246810' });
    const child = (await (await family.request.get('/api/v1/guardian/children')).json())[0].id;
    const groupId = (await (await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json()).groups[0].id;
    const rawMessage = `شكوى أسرة اصطناعية خاصة ${randomUUID()}`;
    const id = (await postFamily(`/api/v1/guardian/children/${child}/feedback`, { trainingGroupId: groupId, coachMembershipId: null, kind: 'Complaint', score: null, message: rawMessage }, randomUUID())).id;
    await login(page, 'owner.football@example.test');
    const headers = { 'X-CSRF-TOKEN': (await (await page.request.get('/api/v1/auth/csrf')).json()).token };
    const email = `investigation-${randomUUID()}@example.test`;
    const create = await page.request.post('/api/v1/manage/staff/', { headers, data: { name: 'مسؤول تحقيق مستقل — عرض اصطناعي', email, phone: `010999${String(Date.now()).slice(-5)}`, temporaryPassword: 'Demo-Only-123!', role: 'AcademyAdmin', jobTitle: 'متابعة خاصة' } }); expect(create.ok(), await create.text()).toBe(true); const member = (await create.json()).id;
    const access = await (await page.request.get(`/api/v1/manage/staff/${member}/access`)).json(); const group = (await (await page.request.get(`/api/v1/manage/groups/${groupId}/details`)).json()).group;
    const scope = { allBranches: false, branches: [group.branchId], allSports: false, sports: [group.sportId], actions: access.modules.map((module: string) => ({ module, read: module === 'followups', add: false, edit: module === 'followups', delete: false })) };
    expect((await page.request.put(`/api/v1/manage/staff/${member}/access`, { headers, data: scope })).status()).toBe(204);
    const money = await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
    await page.goto(`/dashboard/feedback/${id}`); await page.getByLabel('مسؤول التحقيق').selectOption(member);
    await expect(page.getByLabel('موجز يراجعه المالك')).toHaveValue(''); const brief = 'راجع توزيع وقت التدريب دون أي بيانات للأسرة'; await page.getByLabel('موجز يراجعه المالك').fill(brief); await page.getByRole('button', { name: 'حفظ تكليف التحقيق', exact: true }).click();
    const read = async () => (await page.request.get(`/api/v1/manage/feedback/${id}`)).json(); await expect.poll(async () => (await read()).investigatorMembershipId).toBe(member); const assignmentId = (await read()).investigationId;
    const admin = await delegated.newPage(); await login(admin, email); await admin.goto('/dashboard/my-followups'); await admin.locator(`a[href="/dashboard/my-followups/investigations/${assignmentId}"]`).click();
    await expect(admin.getByText(brief, { exact: true })).toBeVisible(); await expect(admin.getByText(rawMessage, { exact: true })).toHaveCount(0); await expect(admin.getByRole('link', { name: 'ملف اللاعب نفسه' })).toHaveCount(0);
    const findings = `نتيجة تحقيق اصطناعية خاصة ${randomUUID()}`; await admin.getByLabel('نتيجة التحقيق للمالك').fill(findings); await admin.getByRole('button', { name: 'حفظ النتيجة الخاصة', exact: true }).click(); await expect(admin.getByText('تم حفظ النتيجة الخاصة للمالك فقط.', { exact: true })).toBeVisible(); await expect(admin.getByText(findings, { exact: true })).toBeVisible();
    await expect.poll(() => admin.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); await admin.screenshot({ path: info.outputPath('private-investigation-delegate-390.png'), fullPage: true, animations: 'disabled' });
    await page.reload(); await expect(page.getByText(`ملاحظة داخلية: ${findings}`, { exact: true })).toBeVisible(); const source = await read(); expect(source.status).toBe('Open'); expect(source.ownerResponse).toBeNull();
    const mine = JSON.stringify(await (await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json()); expect(mine).not.toContain(brief); expect(mine).not.toContain(findings); expect(mine).not.toContain(assignmentId);
    expect((await delegated.request.get(`/api/v1/manage/feedback/${id}`)).status()).toBe(403);
    await page.getByRole('button', { name: 'إلغاء تكليف التحقيق', exact: true }).click(); await expect.poll(async () => (await read()).investigationId).toBeNull();
    await admin.getByRole('button', { name: 'تحديث التحقيقات', exact: true }).click(); await expect(admin.getByRole('alert').filter({ hasText: 'التكليف غير متاح' })).toBeVisible(); await expect(admin.getByText(brief, { exact: true })).toHaveCount(0);
    expect(await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(money);
  } finally { await family.close(); await delegated.close(); }
});
