import {test,expect,APIRequestContext} from '@playwright/test';
import {randomUUID} from 'node:crypto';
import {readFileSync} from 'node:fs';
import {resolve} from 'node:path';

async function json(api:APIRequestContext,path:string){const r=await api.get(path);expect(r.status(),await r.text()).toBe(200);return r.json();}
async function write(api:APIRequestContext,path:string,body:unknown,method='POST',key=randomUUID()){
  const csrf=await json(api,'/api/v1/auth/csrf');const r=await api.fetch(path,{method,data:body,headers:{'X-CSRF-TOKEN':csrf.token,'Idempotency-Key':key}});expect(r.ok(),await r.text()).toBeTruthy();return r.status()===204?null:r.json();
}
const mobileSessions=new Map<string,string>();
async function mobile(api:APIRequestContext,phone:string){
  const cached=mobileSessions.get(phone);if(cached)return cached;
  const r=await api.post('/api/v1/mobile/auth/otp/request',{data:{phoneNumber:phone}});expect(r.status()).toBe(202);
  const v=await api.post('/api/v1/mobile/auth/otp/verify',{data:{phoneNumber:phone,challengeId:(await r.json()).challengeId,code:'246810',deviceName:'قبول V41 اصطناعي'}});expect(v.status()).toBe(200);
  const token=(await v.json()).accessToken as string;mobileSessions.set(phone,token);return token;
}
test.beforeEach(async({page})=>{await page.goto('/login');await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test');await page.getByLabel('كلمة المرور').fill('Demo-Only-123!');await page.getByRole('button',{name:'دخول آمن'}).click();await page.waitForURL('**/dashboard');});

test('V41 canonical persisted demo finances, target, coherent owner desktop scope and drill downs',async({page,browser},info)=>{
  const api=page.request, calendar=await json(api,'/api/v1/academy/calendar');
  const options=await json(api,'/api/v1/manage/structure/options'),branch=options.branches[0].id,today=calendar.today,month=today.slice(0,7)+'-01';
  const versions=await json(api,'/api/v1/manage/configuration'),active=versions.versions.find((v:any)=>v.state==='Active');
  await write(api,'/api/v1/manage/configuration/collection-target',{amount:50000,expectedRevision:active.revision},'PUT');
  // Normal approved/quoted/posted expense workflow. These are persisted source
  // documents, not dashboard numbers or direct changes to an existing balance.
  for(const [category,amount] of [['Rent',5000],['Electricity',650],['Water',350],['Maintenance',450]] as const){
    const expense=await write(api,'/api/v1/finance/operating-expenses',{branchId:branch,scope:3,targetId:branch,category,periodFrom:month,periodTo:today,paidOn:today,amount,currency:'EGP',note:'قبول واجهة V41 — مصروف اصطناعي'},'POST',`v41-${category}-${month}`);
    const id=expense.id??expense.expense?.id;expect(id).toBeTruthy();
    const stored=await json(api,`/api/v1/finance/operating-expenses/${id}`);
    if(stored.expense.status!=='Posted'){
      await write(api,`/api/v1/finance/operating-expenses/${id}/approve`,{});
      const quote=await write(api,`/api/v1/finance/operating-expenses/${id}/quote`,{});
      await write(api,`/api/v1/finance/operating-expenses/${id}/post`,{quoteHash:quote.quoteHash},'POST',`v41-post-${category}-${month}`);
    }else{expect(stored.allocationTotal).toBe(amount);}
  }
  const staff=await json(api,'/api/v1/manage/employees');const employee=staff.items.find((e:any)=>e.isActive&&e.membershipId&&e.monthlySalary>1000);expect(employee).toBeTruthy();
  const root=`/api/v1/manage/payroll/${employee.id}/statement`;
  let payroll=await json(api,`${root}?month=${month}`);
  if(!payroll.statement){await write(api,`${root}/approve`,{month,quoteHash:payroll.quoteHash,note:'اعتماد راتب اصطناعي لقبول V41'});payroll=await json(api,`${root}?month=${month}`);}
  const paid=payroll.payments.reduce((sum:number,p:any)=>sum+p.amount,0);
  if(paid<1000)await write(api,`${root}/disbursements`,{month,amount:1000-paid,expectedPaid:paid,expectedRemaining:payroll.row.netDue-paid,reference:'صرف قبول اصطناعي'},'POST',`v41-salary-${employee.id}-${month}`);
  const token=await mobile(api,'01099900100'),family=await browser.newContext({baseURL:'http://127.0.0.1:3260',extraHTTPHeaders:{Authorization:`Bearer ${token}`}}),guardian=family.request;
  const enrollments=await json(guardian,'/api/v1/guardian/subscriptions/enrollments'),enrollment=enrollments[0];expect(enrollment).toBeTruthy();
  const catalog=await json(guardian,'/api/v1/guardian/catalog'),product=catalog.flatMap((s:any)=>s.items).find((p:any)=>p.displayPrice>0&&p.stockQuantity>0);expect(product).toBeTruthy();
  const sale=await write(guardian,'/api/v1/guardian/product-sales',{productId:product.id,quantity:1,playerId:enrollment.playerId,branchId:enrollment.branchId},'POST',`v41-product-${month}`);
  const tournaments=await json(guardian,'/api/v1/guardian/tournament-payments/options'),tournament=tournaments.tournaments.find((t:any)=>t.fee>0);expect(tournament).toBeTruthy();
  const entry=await write(guardian,'/api/v1/guardian/tournament-payments/',{tournamentId:tournament.id,enrollmentId:enrollment.id},'POST',`v41-tournament-${month}`);
  for(const kind of ['product-sales','tournament-payments']){
    const id=kind==='product-sales'?sale.id:entry.id;
    const details=await json(guardian,`/api/v1/guardian/${kind}/${id}`);
    if(details.status!=='Confirmed'){
      const proof=await guardian.post('/api/v1/assets/upload',{multipart:{file:{name:'synthetic-review-proof.jpg',mimeType:'image/jpeg',buffer:readFileSync(resolve('apps/mobile/assets/demo/portraits/guardian-01.jpg'))},purpose:'Proof'}});expect(proof.status(),await proof.text()).toBe(201);
      await write(guardian,`/api/v1/guardian/${kind}/${id}/proof`,{assetId:(await proof.json()).id});
      await write(api,`/api/v1/manage/${kind}/${id}/review`,{accept:true,reason:null,groupId:null});
    }
    expect((await json(guardian,`/api/v1/guardian/${kind}/${id}`)).status).toBe('Confirmed');
  }
  await family.close();
  const summary=await json(api,'/api/v1/reports/owner-summary'),cockpit=await json(api,'/api/v1/reports/owner-cockpit'),statement=await json(api,'/api/v1/reports/operating-statement');
  expect(summary.from).toBe(calendar.trailingMonthFrom);expect(summary.to).toBe(today);
  expect(statement.totalIncome).toBe(cockpit.collections.net);expect(statement.totalExpenses).toBeGreaterThanOrEqual(6450);expect(statement.surplus).toBeCloseTo(statement.totalIncome-statement.totalExpenses,2);
  expect(statement.income.find((l:any)=>l.key==='product').amount).toBeGreaterThan(0);expect(statement.income.find((l:any)=>l.key==='tournament').amount).toBeGreaterThan(0);
  console.log('V41 canonical acceptance totals',JSON.stringify({income:statement.totalIncome,expenses:statement.totalExpenses,surplus:statement.surplus,incomeLines:statement.income,expenseLines:statement.expenses}));
  expect(cockpit.collections.previous).toBeGreaterThan(0);expect(cockpit.collections.changePercentage).not.toBeNull();expect(cockpit.target.amount).toBeNull();
  const current=await json(api,`/api/v1/reports/owner-cockpit?from=${month}&to=${today}`);expect(current.target.amount).toBe(50000);
  for(const state of ['all','active','expiring','expired']){const d=await json(api,`/api/v1/reports/subscription-snapshot?state=${state}&asOf=${today}`);expect(d.total).toBe(summary.subscriptions[state==='all'?'total':state]);}
  const day=await json(api,`/api/v1/reports/financial?from=${today}&to=${today}`);expect(summary.collections.today).toBeCloseTo(day.netAmount,2);
  const m=await json(api,`/api/v1/reports/financial?from=${month}&to=${today}`);expect(summary.collections.month).toBeCloseTo(m.netAmount,2);
  await page.reload();await expect(page.getByRole('heading',{name:'ملخص الأكاديمية',exact:true})).toBeVisible();
  for(const heading of ['الاشتراكات','تقرير الأرباح والخسائر','طلبات الدفع','مركز القرارات الذكية','أعضاء مميزون','أبرز اللاعبين'])await expect(page.getByRole('heading',{name:heading,exact:true})).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);
  await page.screenshot({path:info.outputPath('owner-desktop-full.png'),fullPage:true,animations:'disabled'});
  await page.getByRole('button',{name:options.branches[0].arabicName,exact:true}).click();await expect(page.getByRole('button',{name:options.branches[0].arabicName,exact:true})).toHaveAttribute('aria-pressed','true');
  await page.getByRole('button',{name:'مسح الفلاتر',exact:true}).click();await expect(page.getByLabel('من تاريخ',{exact:true})).toHaveValue('');await expect(page.getByLabel('إلى تاريخ',{exact:true})).toHaveValue('');
  await page.goto(`/dashboard?from=${month}&to=${today}`);await expect(page.getByLabel('تحقيق هدف التحصيل')).toBeVisible();await page.screenshot({path:info.outputPath('owner-monthly-target.png'),fullPage:true,animations:'disabled'});
  await page.getByRole('link',{name:'اشتراكات فعالة'}).click();await expect(page.getByRole('heading',{name:/الاشتراكات ·/})).toBeVisible();
  const download=await api.get(`/api/v1/reports/financial/export?from=${month}&to=${today}`);expect(download.status()).toBe(200);expect(download.headers()['content-type']).toContain('csv');
});

test('V41 owner preview and confirmed reminder reaches selected family once and persists unread state',async({page,browser},info)=>{
  const token=await mobile(page.request,'01099900101');const context=await browser.newContext({baseURL:'http://127.0.0.1:3260',extraHTTPHeaders:{Authorization:`Bearer ${token}`}});
  const home=await json(context.request,'/api/v1/guardian/home');expect(home.children).toHaveLength(2);
  const before=await json(context.request,'/api/v1/notifications');
  await page.goto('/dashboard/notifications/reminders?kind=Renewal');const family=page.locator('label.placement-choice').filter({hasText:home.guardianName});await expect(family).toHaveCount(1);await family.locator('input').check();
  const text=`رسالة متابعة اصطناعية V41 ${Date.now()} إلى {guardian} بخصوص {children}`;
  await page.getByLabel('نص التنبيه').fill(text);await page.getByRole('button',{name:'معاينة 1 تنبيه',exact:true}).click();await expect(page.getByRole('region',{name:'معاينة التنبيهات'})).toBeVisible();
  expect((await json(context.request,'/api/v1/notifications')).total).toBe(before.total);
  await page.screenshot({path:info.outputPath('reminder-preview.png'),fullPage:true});await page.getByRole('button',{name:'تأكيد إرسال 1 تنبيه',exact:true}).click();await expect(page.getByRole('status')).toContainText('تم حفظ 1 تنبيه');
  const after=await json(context.request,'/api/v1/notifications');expect(after.total).toBe(before.total+1);const sent=after.items.find((n:any)=>n.message.includes(text.split(' إلى')[0]));expect(sent).toBeTruthy();expect(sent.targetKind).toBe('Renewal');
  const mark=await context.request.put(`/api/v1/notifications/${sent.id}/read`,{data:{isRead:true}});expect(mark.status()).toBe(204);expect((await json(context.request,'/api/v1/notifications')).items.find((n:any)=>n.id===sent.id).isRead).toBe(true);
  expect((await page.request.put(`/api/v1/notifications/${sent.id}/read`,{data:{isRead:false},headers:{'X-CSRF-TOKEN':(await json(page.request,'/api/v1/auth/csrf')).token}})).status()).toBe(404);
  await context.close();
});

test('V41 real match result update, coach boundary, actual featured score and template settings',async({page,browser},info)=>{
  const calendar=await json(page.request,'/api/v1/academy/calendar');
  const all=await json(page.request,'/api/v1/my-matches');expect(new Set(all.map((m:any)=>m.branchId)).size).toBe(3);expect(all.some((m:any)=>m.status==='Completed')).toBe(true);expect(all.some((m:any)=>m.isUpcoming)).toBe(true);
  // Enter a result through the real management form, then verify the native
  // feed changes. Publication uses the existing audience/coach snapshot rules.
  const target=all.find((m:any)=>m.status==='Completed'&&m.team==='منتخب 2018');expect(target).toBeTruthy();
  await page.goto('/dashboard/matches');await page.getByRole('button',{name:'المباريات',exact:true}).click();
  const row=page.getByRole('row').filter({hasText:target.team}).filter({hasText:target.opponent}).first();
  await row.getByRole('button',{name:'عرض / تعديل النتيجة'}).click();
  await page.getByLabel('أهداف الأكاديمية',{exact:true}).fill('4');await page.getByLabel('أهداف المنافس',{exact:true}).fill('2');
  const saved=page.waitForResponse(r=>r.request().method()==='PUT'&&r.url().includes('/api/v1/manage/matches/'));
  await page.getByRole('button',{name:'حفظ التعديل',exact:true}).click();expect((await saved).ok()).toBe(true);
  expect((await json(page.request,'/api/v1/my-matches')).find((m:any)=>m.id===target.id)).toMatchObject({ourScore:4,opponentScore:2,status:'Completed'});
  const token=await mobile(page.request,'01099900020');const coach=await browser.newContext({baseURL:'http://127.0.0.1:3260',extraHTTPHeaders:{Authorization:`Bearer ${token}`}});
  const owned=await json(coach.request,'/api/v1/coach/monthly-stars'),ids=new Set(owned.teams.map((t:any)=>t.id));
  const feed=await json(coach.request,'/api/v1/my-matches');expect(feed.length).toBeGreaterThan(0);expect(feed.every((m:any)=>ids.has(m.teamId))).toBe(true);
  const foreign=all.find((m:any)=>!ids.has(m.teamId));expect(foreign).toBeTruthy();expect(await json(coach.request,`/api/v1/my-matches?teamId=${foreign.teamId}`)).toEqual([]);
  expect((await coach.request.get(`/api/v1/my-matches?branchId=${foreign.branchId}`)).status()).toBe(422);
  const featured=await json(page.request,`/api/v1/reports/featured-players?asOf=${calendar.today}`),p85=featured.items.find((p:any)=>p.score===85),p87=featured.items.find((p:any)=>p.score===87);
  expect(p85.stars).toBe(3);expect(p87.stars).toBe(2);expect(featured.items.indexOf(p85)).toBeLessThan(featured.items.indexOf(p87));
  await page.goto('/dashboard/players/featured');await expect(page.getByRole('heading',{name:'أبرز اللاعبين',exact:true})).toBeVisible();await expect(page.getByText('85',{exact:true})).toBeVisible();await page.screenshot({path:info.outputPath('featured-after-coach-grant.png'),fullPage:true});
  await page.goto('/dashboard/settings');await expect(page.getByRole('heading',{name:'فيديوهات شرح الصفحات'})).toBeVisible();await expect(page.getByRole('heading',{name:'قوالب التنبيهات'})).toBeVisible();
  const help=await json(page.request,'/api/v1/page-help/home');expect(help.available).toBe(false);expect(help.url).toBeNull();
  await coach.close();
});

test('V41 isolated licensed ads use audited academy opt-in and explicit adult guardian preference',async({page,browser})=>{
  const api=page.request,configuration=await json(api,'/api/v1/manage/configuration');const active=configuration.versions.find((v:any)=>v.state==='Active');
  if(!active.settings.features.community_ads||!active.settings.tenantMarketingOptIn){
    const settings={...active.settings,features:{...active.settings.features,community_ads:true},tenantMarketingOptIn:true};
    const draft=await write(api,'/api/v1/manage/configuration/drafts',settings);
    for(const target of ['Validated','Staged','Active']){const data=await json(api,'/api/v1/manage/configuration'),version=data.versions.find((v:any)=>v.id===draft.id).version;await write(api,`/api/v1/manage/configuration/${draft.id}/transition`,{target,version});}
  }
  expect((await json(api,'/api/v1/manage/community-campaigns/options')).licensed).toBe(true);
  // The fixture was created with the actual iOS Files picker and native Save.
  // Correct its keyboard-test copy through the existing versioned Web editor;
  // this must not inherit the approval of a previous creative.
  const campaigns=await json(api,'/api/v1/manage/community-campaigns');
  const native=campaigns.items.find((c:any)=>c.internalName==='Academity V41');
  if(native){
    const revision=native.versions.find((r:any)=>r.revision===native.currentRevision);expect(revision.imageAssetId).toBeTruthy();expect(revision.storedState).toBe('Draft');
    await page.goto('/dashboard/community-campaigns');
    await page.getByRole('button',{name:'تعديل في نسخة جديدة',exact:true}).first().click();
    const editor=page.getByRole('dialog',{name:'مسودة المحتوى برعاية'});
    await editor.getByLabel('اسم داخلي',{exact:true}).fill('أيام رياضية للعائلة');
    await editor.getByLabel('العنوان',{exact:true}).fill('يوم رياضي يجمع العائلة');
    await editor.getByRole('textbox',{name:'التفاصيل',exact:true}).fill('أنشطة ممتعة لأطفال الأكاديمية وأسرهم في فرع مدينة نصر. بيانات قبول اصطناعية داخل التطبيق فقط.');
    await editor.getByLabel('وصف الصورة',{exact:true}).fill('شعار أكاديمية ذا بروفيشنال');
    await editor.getByRole('button',{name:'حفظ المسودة',exact:true}).click();await expect(editor).not.toBeVisible();
    const edited=(await json(api,'/api/v1/manage/community-campaigns')).items.find((c:any)=>c.id===native.id),current=edited.versions.find((r:any)=>r.revision===edited.currentRevision);
    expect(current.storedState).toBe('Draft');expect(current.imageAssetId).toBe(revision.imageAssetId);expect(current.targets.every((t:any)=>t.decision==='Pending')).toBe(true);
  }
  const approved=(await json(api,'/api/v1/manage/community-campaigns')).items.find((c:any)=>c.internalName==='أيام رياضية للعائلة');expect(approved).toBeTruthy();
  const current=approved.versions.find((r:any)=>r.revision===approved.currentRevision);expect(current.storedState).toBe('Approved');expect(current.imageAssetId).toBeTruthy();expect(current.targets.some((t:any)=>t.decision==='Approved')).toBe(true);
  for(const phone of ['01099900101','01099900100']){
    const token=await mobile(api,phone),family=await browser.newContext({baseURL:'http://127.0.0.1:3260',extraHTTPHeaders:{Authorization:`Bearer ${token}`}});
    await write(family.request,'/api/v1/guardian/community-ads/preference',{inAppOptIn:true,adultDeclared:true},'PUT');
    const preference=await json(family.request,'/api/v1/guardian/community-ads/preference');expect(preference.adultDeclared).toBe(true);expect(preference.inAppOptIn).toBe(true);expect(preference.available).toBe(true);await family.close();
  }
});
