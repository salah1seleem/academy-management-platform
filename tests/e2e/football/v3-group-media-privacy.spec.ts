import{expect,test}from'@playwright/test';
test('Guardian explicitly allows then withdraws group media; owner reviews the event audience and upload; both roles retain truthful finance',async({page,browser},info)=>{
 test.setTimeout(90_000);await page.setViewportSize({width:390,height:844});
 await page.goto('/login');await page.getByLabel('البريد الإلكتروني').fill('owner.football@example.test');await page.getByLabel('كلمة المرور').fill('Demo-Only-123!');await page.getByRole('button',{name:'دخول آمن'}).click();await page.waitForURL('**/dashboard');
 const family=await browser.newContext({baseURL:new URL(page.url()).origin,viewport:{width:390,height:844}});
 try{
  const write=async(path:string,data:object)=>{const csrf=await(await family.request.get('/api/v1/auth/csrf')).json();const r=await family.request.post(path,{data,headers:{'X-CSRF-TOKEN':csrf.token}});expect(r.ok(),await r.text()).toBe(true);return r.json();};
  const phoneNumber='01099900111',challenge=await write('/api/v1/auth/guardian/otp/request',{phoneNumber});await write('/api/v1/auth/guardian/otp/verify',{phoneNumber,challengeId:challenge.challengeId,code:'246810'});
  const child=(await(await family.request.get('/api/v1/guardian/home')).json()).children[0];
  const e=(await(await page.request.get('/api/v1/evaluations/options')).json()).enrollments.find((x:{playerId:string})=>x.playerId===child.id);expect(e).toBeTruthy();
  const money=await(await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json();
  const fp=await family.newPage();await fp.goto(`/guardian/children/${child.id}/gallery`);
  // Safe to rerun in the same isolated QA database without deleting consent history.
  const prior=await(await family.request.get(`/api/v1/guardian/children/${child.id}/group-media-consent`)).json();if(prior.allowed){await fp.getByRole('button',{name:'سحب موافقة المشاركة',exact:true}).click();await expect(fp.getByText(`${prior.player} · المشاركة غير مسموحة`,{exact:true})).toBeVisible();}
  await fp.getByRole('button',{name:'السماح بالمشاركة داخل المجموعة',exact:true}).click();await expect(fp.getByRole('button',{name:'سحب موافقة المشاركة',exact:true})).toBeVisible();
  await page.goto('/dashboard/players/media/new');await page.getByRole('combobox',{name:'رفع إلى',exact:true}).selectOption('group');await page.getByRole('combobox',{name:'المجموعة',exact:true}).selectOption(e.groupId);
  const subject=page.getByRole('checkbox',{name:new RegExp(child.arabicName)});await expect(subject).toBeEnabled();await subject.check();await page.getByLabel('الصورة أو الفيديو',{exact:true}).setInputFiles({name:'synthetic-pixel.png',mimeType:'image/png',buffer:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=','base64')});
  const caption='وسائط خصوصية اصطناعية '+crypto.randomUUID().slice(0,6);await page.getByLabel('الوصف (اختياري)').fill(caption);await page.getByRole('checkbox',{name:/راجعت الملف/}).check();await page.screenshot({path:info.outputPath('owner-media-audience-web-390.png'),fullPage:true,animations:'disabled'});await page.getByRole('button',{name:'رفع ونشر بعد مراجعة الخصوصية',exact:true}).click();await expect(page.getByRole('status').filter({hasText:'تم رفع ونشر'})).toBeVisible();
  const asset=(await(await page.request.get(`/api/v1/assets/gallery?groupId=${e.groupId}`)).json()).find((a:{caption:string})=>a.caption===caption);expect(asset).toBeTruthy();
  await fp.reload();await expect(fp.getByText(caption,{exact:true})).toBeVisible();expect((await family.request.get(asset.url)).status()).toBe(200);
  await fp.getByRole('button',{name:'سحب موافقة المشاركة',exact:true}).click();await expect(fp.getByRole('button',{name:'السماح بالمشاركة داخل المجموعة',exact:true})).toBeVisible();await expect(fp.getByText(caption,{exact:true})).toHaveCount(0);expect((await family.request.get(asset.url)).status()).toBe(404);expect((await page.request.get(asset.url)).status()).toBe(200);
  await fp.getByText('خصوصية صور المجموعة',{exact:true}).scrollIntoViewIfNeeded();await fp.screenshot({path:info.outputPath('guardian-withdrawn-media-consent-web-390.png'),animations:'disabled'});expect(await fp.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  expect(await(await page.request.get('/api/v1/reports/financial?from=2026-09-01&to=2026-09-28')).json()).toEqual(money);
 }finally{await family.close();}
});
