import { expect,test } from "@playwright/test";

test("Admin recovers signed check-in and checkout after losing both responses and reloading",async({page,context},info)=>{
  test.setTimeout(90_000);
  async function login(email:string){await page.goto("/login");await page.getByLabel("البريد الإلكتروني").fill(email);await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");await page.getByRole("button",{name:"تسجيل الدخول",exact:true}).click();await page.waitForURL("**/dashboard");}
  async function logout(){const toggle=page.getByRole("button",{name:"فتح القائمة"});if(await toggle.isVisible())await toggle.click();await page.getByRole("button",{name:"تسجيل الخروج",exact:true}).click();await page.waitForURL("**/login");}
  const cdp=await context.newCDPSession(page);await cdp.send("WebAuthn.enable");const {authenticatorId}=await cdp.send("WebAuthn.addVirtualAuthenticator",{options:{protocol:"ctap2",transport:"internal",hasResidentKey:true,hasUserVerification:true,isUserVerified:true,automaticPresenceSimulation:true}});
  await login("owner.football@example.test");const headers={"X-CSRF-TOKEN":(await(await page.request.get("/api/v1/auth/csrf")).json()).token};const staff=await(await page.request.get("/api/v1/manage/staff/")).json(),admin=staff.members.find((m:{email:string})=>m.email==="admin.football@example.test");expect(admin).toBeTruthy();const options=await(await page.request.get("/api/v1/manage/structure/options")).json();
  const existing=await(await page.request.get("/api/v1/manage/employees/")).json(),employee=existing.items.find((e:{membershipId:string})=>e.membershipId===admin.id),branch=employee?options.branches.find((b:{id:string})=>b.id===employee.branchId):options.branches[0];expect(branch).toBeTruthy();if(!employee){const created=await page.request.post("/api/v1/manage/employees/",{headers,data:{name:"إداري تحقق اصطناعي",phone:"01099900011",jobTitle:"عرض حضور موقع",branchId:branch.id,membershipId:admin.id,monthlySalary:0,contractStart:"2026-09-01",isActive:true}});expect(created.ok(),await created.text()).toBe(true);}
  const site=await page.request.put(`/api/v1/manage/branches/${branch.id}/check-in-site`,{headers,data:{latitude:30.05,longitude:31.3}});expect(site.ok(),await site.text()).toBe(true);
  await page.goto(`/dashboard/academy/branches/${branch.id}`);const policy=page.locator(".branch-attendance-policy");await expect(policy.getByLabel("نصف القطر بالمتر")).toBeVisible();await policy.getByLabel("نصف القطر بالمتر").fill("50");await policy.getByLabel("أقصى تقدير دقة بالمتر").fill("10");await policy.getByLabel("حداثة قراءة الموقع بالثواني").fill("15");await policy.getByLabel("صلاحية محاولة التوقيع بالثواني").fill("60");await policy.getByLabel("الإداريون ملزمون بتسجيل الحضور").check();await policy.getByLabel("مسؤول الاستثناءات").selectOption(admin.id);await policy.getByRole("button",{name:"حفظ سياسة الحضور"}).click();await expect(policy.getByText("تم حفظ سياسة حضور الفرع وتأكيد قراءتها.")).toBeVisible();
  for(const width of [390,1440]){await page.setViewportSize({width,height:1100});expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);await policy.screenshot({path:info.outputPath(`branch-attendance-policy-${width}.png`),animations:"disabled"});}
  const before=await(await page.request.get("/api/v1/reports/owner-summary")).json();await logout();await login("admin.football@example.test");
  await page.goto("/dashboard/account/verification");await page.getByLabel("اسم الوسيلة").fill("وسيلة حضور الإداري الاصطناعية");await page.getByRole("button",{name:"إضافة وسيلة تحقق"}).click();await expect(page.getByText(/تمت إضافة وسيلة/)).toBeVisible();
  const previous=await(await page.request.get("/api/v1/staff/work-periods")).json();
  const { membershipId } = await (await page.request.get("/api/v1/staff/passkeys/attendance")).json();
  const storageKey = `academy-staff-clock:${membershipId}`;
  await page.setViewportSize({width:390,height:844});
  await context.grantPermissions(["geolocation"]);await page.goto("/dashboard/account/attendance");await page.getByLabel("فرع الحضور").selectOption(branch.id);
  await expect(page.getByLabel("سياسة الفرع المختار")).toContainText("النطاق 50 مترًا · الدقة حتى 10 مترًا · قراءة خلال 15 ثانية · محاولة التوقيع 60 ثانية");await expect(page.getByLabel("سياسة الفرع المختار")).toContainText("تسجيل الحضور مطلوب لفئتك");
  async function refuseOutside(label:string) {
    await context.setGeolocation({latitude:30.0506,longitude:31.3,accuracy:5});
    const options = page.waitForResponse(r=>r.url().endsWith("/attendance/options")&&r.request().method()==="POST");
    await page.getByRole("button",{name:label,exact:true}).click();
    expect((await options).status()).toBe(409);
    await expect(page.getByRole("alert").filter({hasText:"الموقع خارج نطاق الفرع"})).toBeVisible();
    await context.setGeolocation({latitude:30.0503,longitude:31.3,accuracy:5});
  }
  await refuseOutside("تسجيل حضوري");
  await context.setGeolocation({latitude:30.05044,longitude:31.3,accuracy:5});
  await page.getByRole("button",{name:"تسجيل حضوري",exact:true}).click();
  await expect(page.getByRole("alert").filter({hasText:"حدود النطاق"})).toBeVisible();
  expect((await(await page.request.get("/api/v1/staff/work-periods")).json()).items).toEqual(previous.items);
  await context.setGeolocation({latitude:30.0503,longitude:31.3,accuracy:5});
  async function recover(label:string,message:string){
    let command: {challengeId:string} | undefined;
    let receipt: {attendanceId:string;workPeriodId:string;workDate:string;confirmedAtUtc:string;attendanceRecorded:boolean} | undefined;
    let writes=0;
    const complete="**/api/v1/staff/passkeys/attendance/complete";
    await page.route(complete,async route=>{
      writes++;
      command=route.request().postDataJSON();
      const saved=await route.fetch();
      expect(saved.status()).toBe(200);
      receipt=await saved.json();
      await route.abort();
    });
    try{
      await page.getByRole("button",{name:label,exact:true}).click();
      const verify=page.getByRole("button",{name:"التحقق من نتيجة التسجيل"});
      await expect(verify).toBeEnabled();
      expect(command).toBeDefined();
      await page.screenshot({path:info.outputPath(`pending-${label==="تسجيل حضوري"?"in":"out"}.png`),fullPage:true,animations:"disabled"});
      expect(await page.evaluate(key => JSON.parse(sessionStorage.getItem(key)!), storageKey))
        .toEqual({ challengeId: command!.challengeId, operation: label === "تسجيل حضوري" ? "CheckIn" : "CheckOut" });
      const reading = page.waitForResponse(r => r.url().endsWith(`/attendance/attempts/${command!.challengeId}?includeState=true`) && r.request().method() === "GET");
      await page.reload({ waitUntil: "domcontentloaded" });
      const result=await reading;
      expect(result.status()).toBe(200);
      expect(await result.json()).toEqual(receipt);
      await expect(page.getByText(message,{exact:true})).toBeVisible();
      expect(await page.evaluate(key => sessionStorage.getItem(key), storageKey)).toBeNull();
      await page.getByLabel("فرع الحضور").selectOption(branch.id);
      expect(writes).toBe(1);
      expect(receipt?.attendanceRecorded).toBe(true);
      return {receipt:receipt!,command:command!};
    }finally{await page.unroute(complete);}
  }
  const {receipt:first}=await recover("تسجيل حضوري","تم تسجيل حضورك بوقت الخادم المؤكد.");
  await refuseOutside("تسجيل انصرافي");
  const {receipt,command}=await recover("تسجيل انصرافي","تم تسجيل انصرافك للفترة نفسها بوقت الخادم المؤكد.");expect(receipt.attendanceId).toBe(first.attendanceId);expect(receipt.workPeriodId).toBe(first.workPeriodId);
  const csrf={"X-CSRF-TOKEN":(await(await page.request.get("/api/v1/auth/csrf")).json()).token};const replay=await page.request.post("/api/v1/staff/passkeys/attendance/complete",{headers:csrf,data:command});expect(await replay.json()).toEqual(receipt);
  const own=await(await page.request.get(`/api/v1/staff/work-periods?date=${first.workDate}`)).json();expect(own.items).toHaveLength((previous.items?.length??0)+1);const applied=own.items.find((p:{id:string})=>p.id===receipt.workPeriodId);expect(applied.trustSource).toBe("ServerVerified");expect(applied.endUtc).toBe(receipt.confirmedAtUtc);expect(own.totalWorkedSeconds).toBeGreaterThanOrEqual(previous.totalWorkedSeconds??0);
  for(const width of [390,1440]){await page.setViewportSize({width,height:1100});expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);await page.locator(".staff-verified-attendance").screenshot({path:info.outputPath(`verified-attendance-${width}.png`),animations:"disabled"});}
  await logout();await login("owner.football@example.test");const after=await(await page.request.get("/api/v1/reports/owner-summary")).json();expect(after.collections).toEqual(before.collections);expect(after.paymentRequests).toEqual(before.paymentRequests);await cdp.send("WebAuthn.removeVirtualAuthenticator",{authenticatorId});
});
