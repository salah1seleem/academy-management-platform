import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';

test('Family request reaches its own owner inbox and reply without becoming a payment or complaint',async({page,browser},info)=>{
  await page.setViewportSize({width:390,height:844});await page.goto('/login');await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test');await page.getByLabel('كلمة المرور').fill('Demo-Only-123!');await page.getByRole('button',{name:'دخول آمن'}).click();await page.waitForURL('**/dashboard');
  const summary=async()=> (await(await page.request.get('/api/v1/reports/owner-summary?from=2026-09-01&to=2026-09-28')).json()).operations;
  const before=await summary();const money=await(await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
  const family=await browser.newContext({baseURL:new URL(info.project.use.baseURL!).origin,viewport:{width:390,height:844}});
  try{
    const post=async(path:string,data:object)=>{const csrf=(await(await family.request.get('/api/v1/auth/csrf')).json()).token;const r=await family.request.post(path,{data,headers:{'X-CSRF-TOKEN':csrf}});expect(r.ok(),await r.text()).toBe(true);return r.json();};
    const phoneNumber='01099900101';const challenge=await post('/api/v1/auth/guardian/otp/request',{phoneNumber});await post('/api/v1/auth/guardian/otp/verify',{phoneNumber,challengeId:challenge.challengeId,code:'246810'});
    const child=(await(await family.request.get('/api/v1/guardian/children')).json())[0].id;const options=await(await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json();
    const guardian=await family.newPage();await guardian.goto(`/guardian/children/${child}/feedback`);await guardian.getByLabel('المجموعة').selectOption(options.groups[0].id);await guardian.getByLabel('نوع الملاحظة').selectOption('Request');await expect(guardian.getByRole('note')).toContainText('لا ينفّذ دفعًا');await expect(guardian.getByLabel('التقييم',{exact:true})).toHaveCount(0);
    const message=`طلب اصطناعي لتوضيح إجراءات الدفع دون تحصيل فعلي ${randomUUID()}`;await guardian.getByLabel('موضوع الملاحظة').selectOption('Payment');await guardian.getByLabel('اكتب الملاحظة').fill(message);
    await guardian.locator('form.guardian-form').screenshot({path:info.outputPath('guardian-request-intake-web-390.png'),animations:'disabled'});
    const saved=guardian.waitForResponse(r=>r.url().endsWith(`/children/${child}/feedback`)&&r.request().method()==='POST');await guardian.getByRole('button',{name:'إرسال للإدارة',exact:true}).click();const response=await saved;expect(response.status()).toBe(201);const id=(await response.json()).id;await expect(guardian.getByRole('status')).toContainText('وصلت ملاحظتك');
    expect((await summary()).openRequests).toBe(before.openRequests+1);expect((await summary()).openComplaints).toBe(before.openComplaints);
    await page.reload();await page.getByRole('link',{name:/راجع طلبات الأسر/}).click();await expect(page.getByRole('combobox',{name:/^النوع/})).toHaveValue('Request');await page.locator(`a[href="/dashboard/feedback/${id}"]`).click();await expect(page.getByRole('heading',{name:'طلب للإدارة',exact:true})).toBeVisible();
    const detail=await(await page.request.get(`/api/v1/manage/feedback/${id}`)).json();expect(detail).toMatchObject({kind:'Request',score:null,coachName:null,subject:'Payment'});
    await page.locator('section.section-card').filter({has:page.getByRole('heading',{name:'طلب للإدارة',exact:true})}).screenshot({path:info.outputPath('owner-request-detail-web-390.png'),animations:'disabled'});
    const reply='تم توضيح الإجراءات؛ هذا الرد لا ينشئ اشتراكًا أو تحصيلًا.';await page.getByLabel('الرد على الملاحظة').fill(reply);await page.getByRole('button',{name:'إرسال الرد وحل الملاحظة',exact:true}).click();await expect(page.getByText('الحالة: تم الرد والحل')).toBeVisible();
    await guardian.reload();await expect(guardian.locator('article.profile-section').filter({has:guardian.getByText(message,{exact:true})}).getByText(`رد الإدارة: ${reply}`,{exact:true})).toBeVisible();expect((await family.request.get(`/api/v1/manage/feedback/${id}`)).status()).toBe(403);
    const mine=(await(await family.request.get(`/api/v1/guardian/children/${child}/feedback-options`)).json()).mine.find((x:{id:string})=>x.id===id);expect(mine).toMatchObject({kind:'Request',status:'Resolved',coachMembershipId:null});expect(mine.history).toBeUndefined();
    expect((await summary()).openRequests).toBe(before.openRequests);expect(await(await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(money);
    await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  }finally{await family.close();}
});
