import { expect, test } from "@playwright/test";

test("Owner persists a source-linked operational follow-up through its full lifecycle without changing money or group", async ({page}, info)=>{
  test.setTimeout(90_000);
  await page.setViewportSize({width:390,height:844});await page.goto('/login');await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test');await page.getByLabel('كلمة المرور').fill('Demo-Only-123!');await page.getByRole('button',{name:'دخول آمن'}).click();await page.waitForURL('**/dashboard');
  await page.getByRole('link',{name:/متابعات المالك المحفوظة/}).click();await expect(page.getByRole('heading',{name:'متابعات المالك',exact:true})).toBeVisible();
  const options=await page.evaluate(async()=>{const r=await fetch('/api/v1/manage/owner-followups/options');if(!r.ok)throw new Error('Options failed');return r.json();});
  const candidate=options.candidates.find((c:{condition:string;activeFollowUpId:string|null})=>c.condition==='GroupLowOccupancy'&&!c.activeFollowUpId);expect(candidate).toBeTruthy();
  const snapshot=()=>page.evaluate(async id=>{const responses=await Promise.all([fetch(`/api/v1/manage/groups/${id}/details`),fetch('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')]);for(const r of responses)if(!r.ok)throw new Error('Snapshot failed');return Promise.all(responses.map(r=>r.json()));},candidate.source.groupId);
  const before=await snapshot();
  const card=page.locator('article').filter({has:page.getByRole('heading',{name:candidate.source.name,exact:true})}).filter({hasText:'إشغال أقل من نصف السعة'});await card.getByRole('button',{name:'إنشاء متابعة'}).click();await page.waitForURL(/follow-ups\/[a-f0-9-]+$/);const detailUrl=page.url();
  await expect(page.getByRole('link',{name:'فتح المجموعة نفسها'})).toHaveAttribute('href',`/dashboard/academy/groups/${candidate.source.groupId}`);
  for(const state of ['Assigned','InProgress','Snoozed','Closed']){
    await page.getByLabel('حالة المتابعة').selectOption(state);if(state==='Assigned')await page.getByLabel('المالك المسؤول').selectOption(options.owners[0].id);
    if(state==='Snoozed'){const d=new Date(`${options.asOfDate}T12:00:00Z`);d.setUTCDate(d.getUTCDate()+3);await page.getByLabel('موعد المتابعة').fill(d.toISOString().slice(0,10));}
    if(state==='Closed'){await expect(page.getByLabel('سبب إغلاق أو رفض المتابعة')).toHaveAttribute('required','');await page.getByLabel('سبب إغلاق أو رفض المتابعة').fill('أغلقت المتابعة البشرية فقط ولم يتغير مصدر الحالة');}
    await page.getByRole('button',{name:'حفظ المتابعة',exact:true}).click();await page.waitForURL('**/operations/follow-ups');
    if(state==='Closed')await page.getByRole('combobox',{name:'الحالة',exact:true}).selectOption('');
    await page.getByRole('link',{name:'إدارة المتابعة وسجلها'}).and(page.locator(`a[href="${new URL(detailUrl).pathname}"]`)).click();await expect(page).toHaveURL(detailUrl);
  }
  await expect(page.getByRole('status').filter({hasText:'الحالة الأصلية ما زالت قائمة'})).toBeVisible();await expect(page.getByRole('heading',{name:'سجل المتابعة'})).toBeVisible();
  expect(await snapshot()).toEqual(before);
  await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath('owner-followup-detail-390.png'),fullPage:true,animations:'disabled'});
  await page.goto('/dashboard/operations/follow-ups');await page.getByRole('button',{name:'مسح',exact:true}).click();await expect(page.getByLabel('من تاريخ')).toHaveValue('');await expect(page.getByRole('link',{name:'إدارة المتابعة وسجلها'}).and(page.locator(`a[href="${new URL(detailUrl).pathname}"]`))).toBeVisible();await page.screenshot({path:info.outputPath('owner-followup-list-390.png'),fullPage:true,animations:'disabled'});
  await page.setViewportSize({width:1440,height:1000});await page.screenshot({path:info.outputPath('owner-followup-list-1440.png'),fullPage:true,animations:'disabled'});
});
