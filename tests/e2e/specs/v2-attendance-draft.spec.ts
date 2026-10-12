import { expect,test } from "@playwright/test";

test("Admin recovers offline attendance and reviews a concurrent correction without duplicate balance debit",async({page},info)=>{
  await page.goto('/login'); await page.getByLabel('البريد الإلكتروني').fill('owner.nogoom@example.test'); await page.getByLabel('كلمة المرور').fill('Demo-Only-123!'); await page.getByRole('button',{name:'دخول آمن'}).click(); await page.waitForURL('**/dashboard');
  const id=(n:number)=>'5a000000-0000-0000-0000-'+String(n).padStart(12,'0');
  const today=new Intl.DateTimeFormat('en-CA',{timeZone:'Africa/Cairo',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date());
  const csrf=async()=>({'X-CSRF-TOKEN':(await page.request.get('/api/v1/auth/csrf').then(r=>r.json())).token});
  const existing=await page.request.get(`/api/v1/attendance/sessions?from=${today}&to=${today}&groupId=${id(130)}`).then(r=>r.json());
  let session=existing.find((s:{startTime:string})=>s.startTime==='06:45:00')?.id;
  if(!session){const created=await page.request.post('/api/v1/attendance/sessions',{headers:await csrf(),data:{trainingGroupId:id(130),sessionDate:today,startTime:'06:45:00',endTime:'07:15:00',notes:'سيناريو مسودة اصطناعي فقط'}});expect(created.ok(),await created.text()).toBe(true);session=(await created.json()).id;}
  const path=`/api/v1/attendance/sessions/${session}/players`,snapshot=()=>page.request.get(path).then(r=>r.json());
  const write=async(status:number)=>{const current=await snapshot(),row=current.players.find((p:{sportEnrollmentId:string})=>p.sportEnrollmentId===id(111));const result=await page.request.put(path,{headers:await csrf(),data:{draftLease:current.draftLease,items:[{sportEnrollmentId:id(111),status,expectedVersion:row.version}]}});expect(result.ok(),await result.text()).toBe(true);};
  await write(1);await page.goto(`/dashboard/attendance/sessions/${session}/players`);
  const player=page.getByRole('group',{name:'حضور لاعب حالات حضور اصطناعي 110',exact:true});await expect(player).toBeVisible();
  await player.getByRole('button',{name:'حاضر',exact:true}).click();
  await page.route(`**${path}`,route=>route.request().method()==='PUT'?route.abort('internetdisconnected'):route.continue());
  await page.getByRole('button',{name:'حفظ الحضور',exact:true}).click();await expect(page.getByText('بانتظار المزامنة',{exact:true})).toBeVisible();
  await expect(player.getByRole('button',{name:'حاضر',exact:true})).toBeDisabled();
  await write(5);await page.unroute(`**${path}`);await page.getByRole('button',{name:'مراجعة وإعادة المزامنة'}).click();
  const conflict=page.getByRole('region',{name:'تعارض حضور لاعب حالات حضور اصطناعي 110'});await expect(conflict).toContainText('الخادم: معتذر');
  for(const width of[390,1440]){await page.setViewportSize({width,height:1000});await page.screenshot({path:info.outputPath(`attendance-conflict-${width}.png`),fullPage:true,animations:'disabled'});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);}
  await conflict.getByRole('button',{name:'الاحتفاظ باختياري بعد المراجعة'}).click();await page.getByRole('button',{name:'مراجعة وإعادة المزامنة'}).click();await expect(page.getByText('تم حفظ الحضور بنجاح.',{exact:true})).toBeVisible();
  const confirmed=await snapshot(),saved=confirmed.players.find((p:{sportEnrollmentId:string})=>p.sportEnrollmentId===id(111));expect(saved.attendanceStatus).toBe('Present');
  // Simulate a lost response after the API has accepted the write; recheck, never replay.
  await player.getByRole('button',{name:'متأخر',exact:true}).click();let writes=0;
  await page.route(`**${path}`,async route=>{if(route.request().method()!=='PUT')return route.continue();writes++;const response=await route.fetch();expect(response.ok()).toBe(true);await route.abort('connectionreset');});
  await page.getByRole('button',{name:'حفظ الحضور',exact:true}).click();await expect(page.getByText('بانتظار المزامنة',{exact:true})).toBeVisible();await page.getByRole('button',{name:'مراجعة وإعادة المزامنة'}).click();await expect(page.getByText(/الحضور محفوظ بالفعل دون إعادة الإرسال/)).toBeVisible();expect(writes).toBe(1);
  const final=(await snapshot()).players.find((p:{sportEnrollmentId:string})=>p.sportEnrollmentId===id(111));expect(final.attendanceStatus).toBe('Late');expect(final.remainingSessions).toBe(saved.remainingSessions);
});
