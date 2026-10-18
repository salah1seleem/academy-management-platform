import { expect, test } from '@playwright/test';

test('Owner creates, publishes, reviews and achieves a technical goal; linked Guardian sees only published history and money is unchanged', async ({ page, browser }, info) => {
  test.setTimeout(90_000); await page.setViewportSize({ width:390,height:844 });
  await page.goto('/login'); await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test'); await page.getByLabel('كلمة المرور').fill('Demo-Only-123!'); await page.getByRole('button',{name:'دخول آمن'}).click(); await page.waitForURL('**/dashboard');
  const guardian = await browser.newContext({baseURL:new URL(page.url()).origin});
  try {
    const post = async(path:string,data:object) => {const csrf=await(await guardian.request.get('/api/v1/auth/csrf')).json();const r=await guardian.request.post(path,{data,headers:{'X-CSRF-TOKEN':csrf.token}});expect(r.ok(),await r.text()).toBe(true);return r.json();};
    const phoneNumber='01099900109';const challenge=await post('/api/v1/auth/guardian/otp/request',{phoneNumber});await post('/api/v1/auth/guardian/otp/verify',{phoneNumber,challengeId:challenge.challengeId,code:'246810'});
    const home=await(await guardian.request.get('/api/v1/guardian/home')).json(), child=home.children[0].id;
    const options=await(await page.request.get('/api/v1/evaluations/options')).json();const enrollment=options.enrollments.find((e:{playerId:string})=>e.playerId===child);expect(enrollment).toBeTruthy();
    const financial=await(await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
    const profile=await(await page.request.get(`/api/v1/people/players/${child}`)).json();
    await page.goto(`/dashboard/players/${child}?tab=development`);await page.getByRole('link',{name:'أهداف التطور · كرة القدم',exact:true}).click();await page.getByRole('link',{name:'هدف جديد',exact:true}).click();
    const title=`التمرير بالقدمين — تجربة ${crypto.randomUUID().slice(0,4)}`;await page.getByLabel('عنوان الهدف',{exact:true}).fill(title);await page.getByLabel('ما الذي نعمل عليه؟',{exact:true}).fill('نراجع التمرير القصير مع المدرب في الحصة القادمة.');await page.getByRole('button',{name:'حفظ المسودة',exact:true}).click();await page.waitForURL(/goals\/[a-f0-9-]{36}$/);
    const id=page.url().split('/').at(-1)!;
    const mine=async()=> (await(await guardian.request.get(`/api/v1/guardian/children/${child}/development-goals`)).json()).items.find((x:{goal:{id:string}})=>x.goal.id===id);
    expect(await mine()).toBeUndefined();await expect(page.getByText('مسودة خاصة بفريق التدريب',{exact:true})).toBeVisible();
    await page.getByRole('button',{name:'نشر الهدف للأسرة',exact:true}).click();await expect(page.getByText('قيد العمل · منشور للأسرة',{exact:true})).toBeVisible();expect((await mine()).reviews.map((r:{action:string})=>r.action)).toEqual(['Published']);
    await page.getByLabel('ملاحظة التقدم للأسرة (اختياري)').fill('مراجعة اصطناعية: تحسن التمرير ونواصل التدريب.');await page.getByRole('button',{name:'حفظ المراجعة',exact:true}).click();await expect(page.getByRole('heading',{name:'مراجعة التقدم · مالك أكاديمية العرض',exact:true})).toBeVisible();
    await expect(page.getByLabel('عنوان الهدف',{exact:true})).toHaveCount(0);await page.screenshot({path:info.outputPath('owner-development-goal-390.png'),fullPage:true,animations:'disabled'});
    await page.getByRole('combobox',{name:'نتيجة المراجعة',exact:true}).selectOption('Achieved');await page.getByRole('button',{name:'حفظ المراجعة',exact:true}).click();await expect(page.getByText('تحقق الهدف بمراجعة المدرب',{exact:true})).toBeVisible();
    const family=await mine();expect(family.goal.state).toBe('Achieved');expect(family.reviews.map((r:{action:string})=>r.action)).toEqual(['Achieved','Reviewed','Published']);
    const parentPage=await guardian.newPage();await parentPage.setViewportSize({width:390,height:844});await parentPage.goto(`/guardian/children/${child}/goals`);await expect(parentPage.getByRole('heading',{name:title,exact:true})).toBeVisible();
    const goalCard=parentPage.locator('section.profile-section').filter({has:parentPage.getByRole('heading',{name:title,exact:true})});await expect(goalCard.getByText('مراجعة اصطناعية: تحسن التمرير ونواصل التدريب.',{exact:true})).toBeVisible();await expect(parentPage.getByRole('button',{name:'حفظ المراجعة'})).toHaveCount(0);await parentPage.screenshot({path:info.outputPath('guardian-development-goal-390.png'),fullPage:true,animations:'disabled'});
    expect(await(await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(financial);expect(await(await page.request.get(`/api/v1/people/players/${child}`)).json()).toEqual(profile);
    expect((await guardian.request.get(`/api/v1/evaluations/development-goals/${id}`)).status()).toBe(403);await expect.poll(()=>parentPage.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  } finally {await guardian.close();}
});
