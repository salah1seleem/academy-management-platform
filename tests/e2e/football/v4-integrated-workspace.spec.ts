import {expect,test,type APIRequestContext} from '@playwright/test';
import {randomUUID} from 'node:crypto';

async function write(api:APIRequestContext,path:string,data:unknown,elevated=false,put=false){const csrf=await(await api.get(elevated?'/api/v1/platform/auth/csrf':'/api/v1/auth/csrf', { timeout: 10_000 })).json();const r=await api.fetch(path,{method:put?'PUT':'POST',data,timeout:10_000,headers:{'X-CSRF-TOKEN':csrf.token,'Idempotency-Key':randomUUID()}});expect(r.ok(),`${path}: ${r.status()} ${await r.text()}`).toBe(true);return r;}
test.beforeEach(async({page})=>{await page.goto('/login');await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test');await page.getByLabel('كلمة المرور').fill('Demo-Only-123!');await page.getByRole('button',{name:'تسجيل الدخول'}).click();await page.waitForURL('**/dashboard');});

test('V4 owner cockpit, member ordering, finance and drill-down use the same authorized sources',async({page},info)=>{
  const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));
  const range='from=2026-09-01&to=2026-09-28';await page.goto(`/dashboard?${range}`);
  await expect(page.getByRole('region',{name:'ملخص الأكاديمية'})).toBeVisible();await expect(page.getByRole('heading',{name:'مركز القرارات الذكية'})).toBeVisible();await expect(page.getByRole('heading',{name:'أعضاء مميزون'})).toBeVisible();
  const cockpit=await(await page.request.get(`/api/v1/reports/owner-cockpit?${range}`)).json(),statement=await(await page.request.get(`/api/v1/reports/operating-statement?${range}`)).json();expect(cockpit.collections.net).toBe(statement.totalIncome);expect(statement.totalIncome-statement.totalExpenses).toBe(statement.surplus);expect(statement.earnedRevenue).toBeNull();
  await page.screenshot({path:info.outputPath('v4-owner-home-390.png'),fullPage:true,animations:'disabled'});
  await page.goto('/dashboard/guardians?sort=value_desc');await expect(page.getByRole('table',{name:'قائمة أولياء الأمور'})).toContainText('رقم الهاتف');await page.locator('summary').filter({hasText:/^فلاتر وترتيب إضافي/}).click();await page.getByLabel('ترتيب أولياء الأمور').selectOption('value_asc');await expect.poll(async()=>{const r=await page.request.get('/api/v1/people/guardians?sort=value_asc&page=1&pageSize=25');return r.status();}).toBe(200);
  await page.goto(`/dashboard/finance/operating-statement?${range}`);await expect(page.getByText('ليس ربحًا نهائيًا على أساس الاستحقاق',{exact:true})).toBeVisible();await page.getByRole('button',{name:'مسح',exact:true}).click();expect(await page.getByLabel('من تاريخ').inputValue()).toBe('');expect(await page.getByLabel('إلى تاريخ').inputValue()).toBe('');
  await page.setViewportSize({width:1440,height:1000});await page.goto(`/dashboard?${range}`);await expect(page.getByRole('region',{name:'ملخص الأكاديمية'})).toBeVisible();await page.screenshot({path:info.outputPath('v4-owner-home-1440.png'),fullPage:true,animations:'disabled'});expect(errors).toEqual([]);
});

test('V4 employee source → focused follow-up → saved history without changing payroll',async({page},info)=>{
  const opts=await(await page.request.get('/api/v1/manage/structure/options')).json();const branch=opts.branches[0].id,name=`موظف متابعة اصطناعي ${randomUUID().slice(0,8)}`;
  const employee=await(await write(page.request,'/api/v1/manage/employees/',{name,phone:'010'+String(Date.now()).slice(-8),jobTitle:'منسق قبول',branchId:branch,monthlySalary:5000,contractStart:'2026-09-01',contractEnd:null,membershipId:null,photoAssetId:null})).json();
  const before=await(await page.request.get(`/api/v1/manage/employees/${employee.id}`)).json();
  await page.goto('/dashboard/operations/follow-ups/new?source=record');await page.getByLabel('نوع السجل').selectOption('Employee');await page.getByLabel('البحث بالاسم أو الهاتف').fill(name);await page.getByLabel('السجل المرتبط').selectOption({label:`${name} · ${opts.branches[0].arabicName}`});await page.getByLabel('الأولوية').selectOption('2');await page.getByLabel('الملاحظة — اختياري').fill('مراجعة عقد اصطناعي دون صرف أو خصم.');
  await page.getByRole('button',{name:'حفظ المتابعة',exact:true}).click();await page.waitForURL(/\/dashboard\/operations\/follow-ups\/[0-9a-f-]+$/);await expect(page.getByText(name,{exact:true})).toBeVisible();const taskId=new URL(page.url()).pathname.split('/').at(-1)!;await page.getByLabel('حالة المتابعة').selectOption('Closed');await page.getByLabel('سبب إغلاق أو رفض المتابعة').fill('تمت مراجعة المستندات في سيناريو القبول.');await page.getByRole('button',{name:'حفظ المتابعة',exact:true}).click();await page.waitForURL('**/dashboard/operations/follow-ups');await expect.poll(async()=>{const r=await page.request.get(`/api/v1/manage/owner-followups/${taskId}`);expect(r.ok()).toBe(true);return(await r.json()).item.state;}).toBe('Closed');await page.goto(`/dashboard/operations/follow-ups/${taskId}`);await expect(page.getByRole('heading',{name:'سجل المتابعة'})).toBeVisible();
  expect(await(await page.request.get(`/api/v1/manage/employees/${employee.id}`)).json()).toEqual(before);await page.screenshot({path:info.outputPath('v4-follow-up-390.png'),fullPage:true,animations:'disabled'});
});

test('V4 agreement UI saves partial receipt once, opens its document, and reconciles finance', async ({ page, browser }, info) => {
  test.setTimeout(90_000);
  const context = await browser.newContext({ baseURL: new URL(page.url()).origin });
  const platform = context.request;
  const academy = '30000000-0000-0000-0000-000000000003';
  const entitlementsPath = `/api/v1/platform/academies/${academy}/entitlements`;
  const configPath = '/api/v1/manage/configuration/';
  const financialPath = '/api/v1/reports/financial?allDates=true&collectionType=External';
  const api = '/api/v1/finance/external-agreements';

  async function read(client: APIRequestContext, path: string) {
    const response = await client.get(path, { timeout: 10_000 });
    expect(response.ok(), `${path}: ${response.status()}`).toBe(true);
    return response.json();
  }

  async function activate(settings: unknown) {
    const draft = await (await write(page.request, `${configPath}drafts`, settings)).json();
    for (const target of ['Validated', 'Staged', 'Active']) {
      const current = await read(page.request, configPath);
      const row = current.versions.find((version: { id: string }) => version.id === draft.id);
      expect(row).toBeDefined();
      await write(page.request, `${configPath}${draft.id}/transition`, { target, version: row.version });
    }
  }

  await write(platform, '/api/v1/platform/auth/login', { email: 'platform.demo@example.test', password: 'Demo-Only-123!', code: '246810' }, true);
  const licenses = await read(platform, entitlementsPath) as { feature: string; enabled: boolean; planCode: string }[];
  const features = Object.fromEntries(licenses.map(license => [license.feature, license.enabled]));
  const config = await read(page.request, configPath);
  const original = config.versions.find((version: { state: string }) => version.state === 'Active').settings;
  let failure: unknown;
  let licenseChanged = false;
  let configurationChanged = false;
  try {
    await write(platform, entitlementsPath, { planCode: 'Premium', features: { ...features, external_partners: true } }, true, true);
    licenseChanged = true;
    await activate({ ...original, features: { ...original.features, external_partners: true } });
    configurationChanged = true;

    const before = await read(page.request, financialPath);
    await page.goto('/dashboard/finance/external-agreements');
    await page.getByRole('link', { name: 'إنشاء اتفاق', exact: true }).click();
    const title = `اتفاق رحلة قبول ${randomUUID().slice(0, 8)}`;
    await page.getByLabel('اسم الاتفاق').fill(title);
    const options = await read(page.request, `${api}/options`);
    await page.getByLabel('الفرع', { exact: true }).selectOption(options.branches[0].id);
    await page.getByLabel('الرياضة', { exact: true }).selectOption(options.sports[0].id);
    await page.getByLabel('اسم الطرف').fill('جهة اصطناعية بلا تحويل حقيقي');
    await page.getByLabel(/قيمة الاتفاق/).fill('1000');
    await page.getByRole('button', { name: 'حفظ الاتفاق', exact: true }).click();
    await expect(page).toHaveURL(/external-agreements\/[0-9a-f-]+\?created=true/);
    await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
    const id = new URL(page.url()).pathname.split('/').at(-1);
    const detailPath = `${api}/${id}`;
    expect(await read(page.request, detailPath)).toMatchObject({ paid: 0, remaining: 1000, receipts: [] });
    expect((await read(page.request, financialPath)).totalAmount).toBe(before.totalAmount);

    await page.getByLabel(/^المبلغ \(/).fill('400');
    await page.getByRole('combobox', { name: 'طريقة الدفع', exact: true }).selectOption('Online');
    const collect = page.getByRole('button', { name: 'تسجيل التحصيل وإصدار الإيصال', exact: true });
    await collect.click();
    expect(await read(page.request, detailPath)).toMatchObject({ paid: 0, remaining: 1000, receipts: [] });
    await page.getByRole('checkbox', { name: 'أؤكد استلام المبلغ', exact: true }).check();
    await collect.click();
    const saved = page.getByRole('region', { name: 'التحصيل المسجّل', exact: true });
    const documentLink = saved.getByRole('link', { name: 'فتح الإيصال المسجّل', exact: true });
    await expect(documentLink).toBeVisible();
    const detail = await read(page.request, detailPath);
    expect(detail).toMatchObject({ paid: 400, remaining: 600 });
    expect(detail.receipts).toHaveLength(1);
    await expect(documentLink).toHaveAttribute('href', `/dashboard/finance/external-agreements/${id}/receipts/${detail.receipts[0].id}`);
    await page.screenshot({ path: info.outputPath('v4-external-receipt-390.png'), fullPage: true, animations: 'disabled' });
    await documentLink.click();
    const document = page.getByRole('article', { name: 'إيصال تحصيل اتفاق خارجي', exact: true });
    await expect(document.getByText('400 جنيه', { exact: true })).toBeVisible();
    await expect(document.getByText(detail.receipts[0].receiptNumber, { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'طباعة الإيصال', exact: true })).toBeVisible();
    const after = await read(page.request, financialPath);
    expect(after.totalAmount - before.totalAmount).toBe(400);
  } catch (error) {
    failure = error;
    throw error;
  } finally {
    const cleanupErrors: unknown[] = [];
    if (configurationChanged) await activate(original).catch(error => cleanupErrors.push(error));
    if (licenseChanged) await write(platform, entitlementsPath, { planCode: licenses[0].planCode, features }, true, true).catch(error => cleanupErrors.push(error));
    await context.close();
    if (cleanupErrors.length) {
      await info.attach('Agreement demo restoration', { body: cleanupErrors.map(String).join('\n'), contentType: 'text/plain' });
      if (!failure) throw new AggregateError(cleanupErrors, 'Restoring the agreement demo failed');
    }
  }
});
