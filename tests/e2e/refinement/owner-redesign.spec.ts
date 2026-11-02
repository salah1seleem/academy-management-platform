import {test,expect} from '@playwright/test';

async function login(page:import('@playwright/test').Page){
  await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test');
  await page.getByLabel('كلمة المرور').fill('Demo-Only-123!');
  await page.getByRole('button',{name:'دخول آمن'}).click();
}

for(const width of [1440,390])test(`Owner reference dashboard real workload, pies, actions and filters at ${width}`,async({page},info)=>{
  await page.setViewportSize({width,height:1000});
  await page.goto('/login');await login(page);await page.waitForURL('**/dashboard');
  const table=page.getByRole('table',{name:'ترتيب المدربين حسب عدد المجموعات المسندة'});
  await expect(table).toBeVisible();
  const response=await page.request.get('/api/v1/reports/owner-summary');expect(response.status()).toBe(200);
  const summary=await response.json();
  const expected=[...summary.operations.coachPerformance].sort((a:any,b:any)=>b.groupCount-a.groupCount||a.name.localeCompare(b.name,'ar'));
  await expect(table.locator('tbody tr')).toHaveCount(expected.length);
  for(let i=0;i<expected.length;i++){
    const row=table.locator('tbody tr').nth(i);
    await expect(row.getByRole('rowheader')).toContainText(expected[i].name);
    await expect(row.locator('td').nth(1)).toHaveText(String(expected[i].groupCount));
    await expect(row.getByRole('link')).toHaveAttribute('href',new RegExp(`/coaches/${expected[i].id}\\?from=${summary.from}&to=${summary.to}`));
  }
  const pies=page.getByRole('region',{name:'الرسوم الدائرية'});
  await expect(pies.getByRole('img')).toHaveCount(2);
  const actions=page.getByRole('navigation',{name:'الإجراءات التشغيلية السريعة'});
  await expect(actions.getByRole('link')).toHaveCount(6);
  await expect(actions.getByRole('link',{name:/تسجيل لاعب/})).toHaveAttribute('href','/dashboard/players/new');
  await expect(actions.getByRole('link',{name:/تجديد اشتراك/})).toHaveAttribute('href','/dashboard/subscriptions/renew/new');
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);
  for(const photo of await page.getByRole('main').locator('img[loading="lazy"]').all()){
    await photo.scrollIntoViewIfNeeded({timeout:5000});
    await expect.poll(()=>photo.evaluate((img:HTMLImageElement)=>img.naturalWidth)).toBeGreaterThan(0);
  }
  await page.getByRole('heading',{name:'الرئيسية',exact:true}).scrollIntoViewIfNeeded();
  await page.screenshot({path:info.outputPath(`owner-${width}-top.png`)});
  await page.screenshot({path:info.outputPath(`owner-${width}-full.png`),fullPage:true});
  if(width===1440){await table.screenshot({path:info.outputPath('coach-workload-table.png')});await pies.screenshot({path:info.outputPath('retained-pie-charts.png')});}
  await page.getByRole('button',{name:'فرع مدينتي',exact:true}).click();
  await expect(page.getByRole('button',{name:'فرع مدينتي',exact:true})).toHaveAttribute('aria-pressed','true');
  await expect(table).toBeVisible();
  await page.getByRole('button',{name:'مسح الفلاتر',exact:true}).click();
  await expect(page.getByLabel('من تاريخ',{exact:true})).toHaveValue('');
  await expect(page.getByLabel('إلى تاريخ',{exact:true})).toHaveValue('');
  await expect(table).toBeVisible();
  await expect(page.getByRole('button',{name:'كل الفروع',exact:true})).toHaveAttribute('aria-pressed','true');
  await actions.getByRole('link',{name:/تسجيل لاعب/}).click();
  await expect(page.getByRole('heading',{name:'تسجيل لاعب جديد',exact:true})).toBeVisible();
  await expect(page.getByLabel('الباقة',{exact:true}).locator('option').nth(1)).toBeAttached();
  expect(await page.getByLabel('الباقة',{exact:true}).locator('option').count()).toBeGreaterThan(1);
});

test('mobile operation handoff resumes its protected form after web sign-in',async({page})=>{
  await page.goto('/dashboard/players/new');
  await page.waitForURL('**/login?returnTo=*');
  expect(new URL(page.url()).searchParams.get('returnTo')).toBe('/dashboard/players/new');
  await login(page);
  await page.waitForURL('**/dashboard/players/new');
  await expect(page.getByRole('heading',{name:'تسجيل لاعب جديد',exact:true})).toBeVisible();
});
