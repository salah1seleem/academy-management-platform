import { expect,test } from "@playwright/test";

test("Dated base salary keeps current month unchanged across Owner UI, payroll and Coach own API",async({page},info)=>{
  await page.goto("/login");await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test");await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");await page.getByRole("button",{name:"دخول آمن"}).click();await page.waitForURL("**/dashboard");
  const options=await(await page.request.get("/api/v1/manage/sessions/options")).json();const coach=options.coaches.find((c:{name:string})=>c.name==="محمود طارق سليمان").id;
  const employee=(await(await page.request.get(`/api/v1/manage/payroll/coaches/${coach}/agreements`)).json()).employeeId;expect(employee).toBeTruthy();
  const root=`/api/v1/manage/employees/${employee}/salary-versions`;
  const before=await(await page.request.get(root)).json();const month=new Date(`${before.currentMonth}T12:00:00Z`);month.setUTCMonth(month.getUTCMonth()+1);const next=month.toISOString().slice(0,10);
  const payroll=async(date:string)=>(await(await page.request.get(`/api/v1/manage/payroll/?month=${date}`)).json()).items.find((r:{employeeId:string})=>r.employeeId===employee);
  const current=await payroll(before.currentMonth);
  await page.goto(`/dashboard/staff/employees/${employee}/salary`);await expect(page.getByRole("heading",{name:"سجل الراتب وتغييراته",exact:true})).toBeVisible();
  await page.getByLabel("شهر السريان").fill(next.slice(0,7));await page.getByLabel("الراتب الشهري الجديد").fill(String(before.currentSalary+500));await page.getByLabel("ملاحظة (اختياري)").fill("زيادة مستقبلية اصطناعية، لا صرف ولا تعديل للماضي");
  await page.getByRole("button",{name:"مراجعة واعتماد التغيير"}).click();await expect(page.getByRole("dialog")).toContainText("لن ينشأ صرف");
  const save=page.waitForResponse(r=>r.request().method()==="POST"&&r.url().endsWith(root));await page.getByRole("button",{name:"تأكيد اعتماد الراتب"}).click();expect((await save).status()).toBe(201);
  await expect(page.getByRole("dialog")).toHaveCount(0);await expect(page.getByText("تم اعتماد التغيير المؤرخ دون تعديل راتب مسدد أو إنشاء صرف.")).toBeVisible();
  const after=await(await page.request.get(root)).json();expect(after.currentSalary).toBe(before.currentSalary);expect(after.items[0]).toMatchObject({effectiveFrom:next,monthlySalary:before.currentSalary+500,kind:"Change"});expect(after.revision).toBe(before.revision+(before.revision===0?2:1));
  expect((await payroll(before.currentMonth)).netDue).toBe(current.netDue);expect((await payroll(next)).monthlySalary).toBe(before.currentSalary+500);expect((await payroll(next)).salaryBasis).toBe("ApprovedVersion");expect((await payroll(next)).isPaid).toBe(false);
  await page.getByRole("button",{name:"إغلاق رسالة النجاح"}).click();
  for(const width of [390,1440]){await page.setViewportSize({width,height:1000});await page.evaluate(()=>window.scrollTo(0,0));await expect(page.getByRole("heading",{name:"سجل الراتب وتغييراته",exact:true})).toBeInViewport();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);expect(await page.locator(".salary-history-summary").evaluate(el=>getComputedStyle(el).gridTemplateColumns.split(" ").length)).toBe(width<600?1:2);await page.screenshot({path:info.outputPath(`salary-history-${width}.png`),animations:"disabled"});}
  await page.goto(`/dashboard/staff/employees/${employee}`);await expect(page.getByLabel("الراتب الشهري")).toHaveAttribute("readonly","");await expect(page.getByRole("link",{name:"تغيير الراتب من السجل المؤرخ"})).toHaveAttribute("href",`/dashboard/staff/employees/${employee}/salary`);
  const otp=await page.request.post("/api/v1/mobile/auth/otp/request",{data:{phoneNumber:"+201099900020"}});expect(otp.status()).toBe(202);const verified=await page.request.post("/api/v1/mobile/auth/otp/verify",{data:{phoneNumber:"+201099900020",challengeId:(await otp.json()).challengeId,code:"246810",deviceName:"تاريخ الراتب — اختبار اصطناعي"}});expect(verified.status()).toBe(200);
  const own=await page.request.get("/api/v1/staff/payroll/salary-versions",{headers:{Authorization:`Bearer ${(await verified.json()).accessToken}`}});expect(own.status()).toBe(200);const mine=await own.json();expect(mine.id).toBe(employee);expect(mine.canChange).toBe(false);expect(mine.currentSalary).toBe(before.currentSalary);expect(mine.items[0].monthlySalary).toBe(before.currentSalary+500);for(const v of mine.items){expect(v.note).toBeNull();expect(v.approvedBy).toBeNull();}
});
