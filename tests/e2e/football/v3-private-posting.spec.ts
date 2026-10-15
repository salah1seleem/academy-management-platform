import { expect, test } from "@playwright/test";
import { mobileDemoToken } from "../support/mobile-demo-session";

test("Private payroll source: partial allocation, later delta, explicit review/reversal and coach read reflect the same persisted amounts", async ({ page }, info) => {
  test.setTimeout(120_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", {name:"دخول آمن"}).click(); await page.waitForURL("**/dashboard");
  const options = await (await page.request.get("/api/v1/manage/sessions/options")).json();
  const coach = options.coaches.find((c:{name:string})=>c.name==="محمود طارق سليمان").id;
  const root = `/api/v1/manage/payroll/coaches/${coach}/private-services`;
  const sources = await (await page.request.get(`${root}/options`)).json();
  const seed = (await (await page.request.get(root)).json()).items.find((r:{serviceDate:string;ordinal:number})=>r.serviceDate==="2026-10-01"&&r.ordinal===1);
  const enrollment = sources.packages.find((p:{id:string})=>p.id===seed.packageId).sportEnrollmentId;
  const plan = (await (await page.request.get(`/api/v1/finance/receivables/${seed.documentId}`)).json()).document.planId;
  const token = (await (await page.request.get("/api/v1/auth/csrf")).json()).token;
  async function post(path:string,data:unknown) {
    const r=await page.request.post(path,{headers:{"X-CSRF-TOKEN":token,"Idempotency-Key":crypto.randomUUID()},data}); expect(r.ok(),await r.text()).toBe(true); return r.json();
  }
  const document=(await post("/api/v1/finance/receivables/",{sportEnrollmentId:enrollment,planId:plan,amount:"2000.00",dueDate:"2026-10-01",source:"ManualCharge",description:"رحلة ترحيل الخاص — بيانات اصطناعية مستقلة"})).id;
  const group=options.groups.find((g:{name:string})=>g.name==="خاص — مصادر أجر اصطناعية").id;
  const seconds=Math.floor(Date.now()/1000)%3500, stamp=(n:number)=>`00:${String(Math.floor(n/60)).padStart(2,"0")}:${String(n%60).padStart(2,"0")}`;
  const session=(await post("/api/v1/attendance/sessions",{trainingGroupId:group,sessionDate:"2026-10-04",startTime:stamp(seconds),endTime:stamp(seconds+1)})).id;
  const pack=(await post(`${root}/packages`,{documentId:document,sessionCount:8})).id;
  await post(`${root}/${pack}/deliveries`,{trainingSessionId:session,ordinal:1});
  const delivery=(await (await page.request.get(root)).json()).items.find((r:{documentId:string})=>r.documentId===document).id;
  const path=`${root}/${delivery}`;
  const employee=(await (await page.request.get(`/api/v1/manage/payroll/coaches/${coach}/agreements`)).json()).employeeId;
  const payroll=async()=> (await (await page.request.get("/api/v1/manage/payroll/")).json()).items.find((r:{employeeId:string})=>r.employeeId===employee);
  const before=await payroll(); expect(before.isPaid).toBe(false);
  async function allocateThousand() {
    const existing=new Set((await (await page.request.get(`/api/v1/finance/receivables/${document}/collections`)).json()).map((c:{id:string})=>c.id));
    const renewal=await post("/api/v1/subscriptions/admin-renewals",{sportEnrollmentId:enrollment,subscriptionPlanId:plan});
    await post(`/api/v1/subscriptions/payments/${renewal.paymentId}/simulate`,{outcome:"Success"});
    const funds=await (await page.request.get(`/api/v1/finance/receivables/${document}/collections`)).json();
    const collection=funds.find((c:{id:string;available:string})=>!existing.has(c.id)&&Number(c.available)>=1000); expect(collection).toBeTruthy();
    await post(`/api/v1/finance/receivables/${document}/movements`,{kind:"PaymentAllocation",amount:"1000.00",collectionId:collection.id,reason:"تخصيص تحصيل اختبار داخلي لنفس المطالبة"});
  }
  await allocateThousand();
  await page.goto(`/dashboard/academy/coaches/${coach}`);
  const section=page.getByRole("region",{name:"تنفيذ الخاص وعمولاته"});
  const row=section.locator(".linked-list > article").filter({hasText:document.slice(0,8)}); await expect(row).toHaveCount(1);
  async function confirm(label:string) {
    const request=page.waitForResponse(r=>r.request().method()==="POST"&&r.url().endsWith(`${delivery}/entries`));
    await page.getByRole("button",{name:label,exact:true}).click(); expect((await request).status()).toBe(201);
    await expect(page.getByRole("dialog",{name:"مراجعة عمولة الخاص"})).toHaveCount(0);
  }
  await row.getByRole("button",{name:"مراجعة الاستحقاق"}).click(); await expect(page.getByRole("dialog")).toContainText("فرق العمولة");
  expect((await (await page.request.get(`${path}/posting-preview`)).json()).difference).toBe(50);
  await confirm("ترحيل فرق العمولة"); expect((await payroll()).netDue).toBe(before.netDue+50);
  await allocateThousand(); await section.getByRole("button",{name:"تحديث التحصيل"}).click();
  await row.getByRole("button",{name:"مراجعة الاستحقاق"}).click();
  const later=await (await page.request.get(`${path}/posting-preview`)).json(); expect(later).toMatchObject({currentPosted:50,targetAmount:100,difference:50});
  await confirm("ترحيل فرق العمولة"); expect((await payroll()).netDue).toBe(before.netDue+100);
  await post(`/api/v1/finance/receivables/${document}/movements`,{kind:"CreditNote",amount:"400.00",reason:"تصحيح مصدر مستقل قبل سداد عمولة الاختبار"});
  await row.getByRole("button",{name:"مراجعة الاستحقاق"}).click(); await confirm("حفظ استثناء للمراجعة"); expect((await payroll()).netDue).toBe(before.netDue+100);
  await page.getByRole("button",{name:"إغلاق رسالة النجاح"}).click();
  await row.getByRole("button",{name:"مراجعة الاستحقاق"}).click(); const modal=page.getByRole("dialog",{name:"مراجعة عمولة الخاص"});
  await expect(modal.getByRole("button",{name:"تأكيد عكس العمولة غير المسددة"})).toBeVisible();
  for(const width of [390,1440]) { await page.setViewportSize({width,height:1000}); await expect(modal).toBeInViewport(); expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true); expect(await modal.locator(".private-posting-metrics").evaluate(el=>getComputedStyle(el).gridTemplateColumns.split(" ").length)).toBe(2); expect(await modal.locator(".private-posting-metrics > div").first().evaluate(el=>getComputedStyle(el).display)).toBe("flex"); await modal.screenshot({path:info.outputPath(`private-posting-${width}.png`),animations:"disabled"}); }
  await confirm("تأكيد عكس العمولة غير المسددة"); expect((await payroll()).netDue).toBe(before.netDue+80);
  const history=await (await page.request.get(`${path}/entries`)).json(); expect(history.map((e:{delta:number})=>e.delta)).toEqual([-20,0,50,50]); expect(history[0].reviewEntryId).toBe(history[1].id); expect(new Set(history.filter((e:{payrollAdjustmentId:string|null})=>e.payrollAdjustmentId).map((e:{payrollAdjustmentId:string})=>e.payrollAdjustmentId)).size).toBe(3);
  await page.goto(`/dashboard/payroll?employeeId=${employee}`); await expect(page.getByRole("link",{name:/محتسبة بالصافي بالفعل/})).toHaveAttribute("href",`/dashboard/academy/coaches/${coach}`);
  const mobileToken = await mobileDemoToken(page.request, "+201099900020", "رحلة عرض اصطناعية");
  const own=await page.request.get("/api/v1/staff/payroll/private-services",{headers:{Authorization:`Bearer ${mobileToken}`}}); expect(own.status()).toBe(200);
  const mine=(await own.json()).items.find((r:{id:string})=>r.id===delivery); expect(mine).toMatchObject({earned:80,postedAmount:80,pendingDelta:0,postingCount:3}); expect(mine).not.toHaveProperty("documentId"); expect(mine).not.toHaveProperty("player");
});
