import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';

test('Owner publishes a follow-up policy, family submits, captured deadline survives disabling', async ({page, browser}, info) => {
  await page.setViewportSize({width:390,height:844});
  await page.goto('/login'); await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test'); await page.getByLabel('كلمة المرور').fill('Demo-Only-123!'); await page.getByRole('button',{name:'دخول آمن'}).click(); await page.waitForURL('**/dashboard');
  const post = async (path:string,data:object) => {
    const csrf = (await (await page.request.get('/api/v1/auth/csrf')).json()).token;
    const r = await page.request.post(path,{data,headers:{'X-CSRF-TOKEN':csrf}}); expect(r.ok(),await r.text()).toBe(true); return r;
  };
  const configurations = async () => (await (await page.request.get('/api/v1/manage/configuration')).json()).versions;
  const original = (await configurations()).find((v:{state:string})=>v.state==='Active').settings;
  const publish = async (id:string) => {
    for (const target of ['Validated','Staged','Active']) {
      const row = (await configurations()).find((v:{id:string})=>v.id===id);
      await post(`/api/v1/manage/configuration/${id}/transition`,{target,version:row.version});
    }
  };
  const financial = await (await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
  const family = await browser.newContext({baseURL:new URL(info.project.use.baseURL!).origin,viewport:{width:390,height:844}});
  try {
    await page.goto('/dashboard/settings');
    await page.getByRole('checkbox',{name:'تحديد مهلة متابعة تلقائية'}).check(); await page.getByLabel('مهلة المتابعة بالأيام').fill('2');
    const draftResponse = page.waitForResponse(r=>r.url().endsWith('/configuration/drafts')&&r.request().method()==='POST');
    await page.getByRole('button',{name:'حفظ إصدار مسودة'}).click(); const draft = await (await draftResponse).json();
    expect((await configurations()).find((v:{state:string})=>v.state==='Active').settings.feedbackFollowUpDays??null).toBe(original.feedbackFollowUpDays??null);
    await publish(draft.id);
    const familyPost = async (path:string,data:object) => {const csrf=(await(await family.request.get('/api/v1/auth/csrf')).json()).token; const r=await family.request.post(path,{data,headers:{'X-CSRF-TOKEN':csrf}});expect(r.ok(),await r.text()).toBe(true);return r.json();};
    const phoneNumber='01099900101'; const challenge=await familyPost('/api/v1/auth/guardian/otp/request',{phoneNumber});await familyPost('/api/v1/auth/guardian/otp/verify',{phoneNumber,challengeId:challenge.challengeId,code:'246810'});
    const child=(await(await family.request.get('/api/v1/guardian/children')).json())[0].id;
    const options=await(await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json();
    const guardian=await family.newPage(); await guardian.goto(`/guardian/children/${child}/feedback`);
    await guardian.getByLabel('المجموعة').selectOption(options.groups[0].id); await guardian.getByLabel('اكتب الملاحظة').fill(`شكوى تجريبية لمهلة المتابعة ${randomUUID()}`);
    const response=guardian.waitForResponse(r=>r.url().endsWith(`/children/${child}/feedback`)&&r.request().method()==='POST'); await guardian.getByRole('button',{name:'إرسال للإدارة',exact:true}).click(); const saved=await response;expect(saved.status()).toBe(201);const id=(await saved.json()).id;
    await page.goto(`/dashboard/feedback/${id}`); await expect(page.getByText(/سياسة المتابعة وقت التقديم/)).toBeVisible();
    const source=await(await page.request.get(`/api/v1/manage/feedback/${id}`)).json(); const evidence=JSON.parse(source.followUpPolicyEvidence);
    expect(evidence).toMatchObject({configurationId:draft.id,days:2,receivedOn:options.asOfDate});expect(source.dueOn).toBe(evidence.dueOn);
    await page.getByText(/سياسة المتابعة وقت التقديم/).screenshot({path:info.outputPath('owner-deadline-evidence-390.png')});
    const disable=await(await post('/api/v1/manage/configuration/drafts',{...original,feedbackFollowUpDays:null})).json();await publish(disable.id);
    await page.reload(); await expect(page.getByText(/سياسة المتابعة وقت التقديم/)).toBeVisible();
    expect((await(await page.request.get(`/api/v1/manage/feedback/${id}`)).json()).followUpPolicyEvidence).toBe(source.followUpPolicyEvidence);
    expect((await family.request.get(`/api/v1/manage/feedback/${id}`)).status()).toBe(403);
    expect(JSON.stringify(await(await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json())).not.toContain('followUpPolicyEvidence');
    expect(await(await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(financial);
    await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  } finally {
    const restore=await(await post('/api/v1/manage/configuration/drafts',original)).json();await publish(restore.id); await family.close();
  }
});
