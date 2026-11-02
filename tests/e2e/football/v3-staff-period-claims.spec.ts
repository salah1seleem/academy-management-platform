import { expect, test } from "@playwright/test";
import { mobileDemoToken } from "../support/mobile-demo-session";

test("Staff second shift and exact-period checkout flow through independent review, own history and one report row", async ({ page }, info) => {
  test.setTimeout(90_000);
  await page.goto("/login");await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test");await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");await page.getByRole("button",{name:"دخول آمن"}).click();await page.waitForURL("**/dashboard");
  const headers={"X-CSRF-TOKEN":(await(await page.request.get("/api/v1/auth/csrf")).json()).token,"Idempotency-Key":crypto.randomUUID()};
  const stamp=Date.now(), phone=`015${String(stamp).slice(-8)}`, name=`مدرب طلب فترة اصطناعي ${stamp}`;
  const made=await page.request.post("/api/v1/manage/coaches",{headers,data:{displayName:name,phoneNumber:phone,email:`period-claim.${stamp}@example.test`,isActive:true,groupIds:[]}});expect(made.ok(),await made.text()).toBe(true);const member=await made.json(),root=`/api/v1/manage/staff-attendance/${member.membershipId}`;
  const options=await(await page.request.get("/api/v1/manage/structure/options")).json();const branch=options.branches[0].id;
  const employee=await page.request.post("/api/v1/manage/employees/",{headers,data:{name,phone,jobTitle:"طلب وردية اصطناعي",branchId:branch,membershipId:member.membershipId,monthlySalary:0,contractStart:"2026-09-01",isActive:true}});expect(employee.ok(),await employee.text()).toBe(true);
  const today=(await(await page.request.get(root)).json()).today;const day=new Date(`${today}T12:00:00Z`);day.setUTCDate(day.getUTCDate()-2);const date=day.toISOString().slice(0,10);
  const seeded=await page.request.put(root,{headers,data:{date,status:"Present",checkIn:"08:00:00",checkOut:"10:00:00",approvedDeduction:0,reason:null}});expect(seeded.ok(),await seeded.text()).toBe(true);
  const before=await(await page.request.get("/api/v1/reports/owner-summary")).json();
  const token=await mobileDemoToken(page.request,phone,"طلب فترة اصطناعي"), mobile={Authorization:`Bearer ${token}`,"Idempotency-Key":crypto.randomUUID()};
  const requestedStart=new Date(`${date}T11:00:00Z`).toLocaleTimeString("en-GB",{timeZone:"Africa/Cairo",hour:"2-digit",minute:"2-digit"});
  const first=await page.request.post("/api/v1/staff/attendance-exceptions/",{headers:mobile,data:{branchId:branch,operation:"CheckIn",claimedAtUtc:`${date}T11:00:00Z`,reason:"تعذر الاتصال عند بداية الوردية الإضافية"}});expect(first.ok(),await first.text()).toBe(true);const claim=await first.json();
  await page.goto(`/dashboard/attendance/staff/exceptions/${claim.id}`);await page.getByRole("button",{name:"قبول وتسجيل إداري"}).click();await expect(page.getByText("تم قبول الطلب وتسجيل الحضور إداريًا.")).toBeVisible();
  const open=(await(await page.request.get("/api/v1/staff/attendance-exceptions/options",{headers:mobile})).json()).openAttendance;expect(open.targetWorkPeriodId).toBeTruthy();
  const checkout={branchId:branch,operation:"CheckOut",claimedAtUtc:`${date}T12:00:00Z`,reason:"تعذر التحقق بالجهاز وقت الانصراف الاصطناعي",targetAttendanceId:open.id,targetWorkPeriodId:open.targetWorkPeriodId,targetWorkPeriodVersion:open.targetWorkPeriodVersion};
  const checkHeaders={...mobile,"Idempotency-Key":crypto.randomUUID()};const sent=await page.request.post("/api/v1/staff/attendance-exceptions/",{headers:checkHeaders,data:checkout});expect(sent.ok(),await sent.text()).toBe(true);const out=await sent.json();
  expect(out.targetPeriod.id).toBe(open.targetWorkPeriodId);await page.goto(`/dashboard/attendance/staff/exceptions/${out.id}`);const target=page.getByRole("complementary",{name:"الفترة المحددة"});await expect(target).toContainText(requestedStart);
  for(const width of [390,1440]){await page.setViewportSize({width,height:1000});expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);await target.screenshot({path:info.outputPath(`period-claim-${width}.png`),animations:"disabled"});}
  await page.getByRole("button",{name:"قبول وتسجيل إداري"}).click();await expect(page.getByText("تم قبول الطلب وتسجيل الحضور إداريًا.")).toBeVisible();
  const replay=await page.request.post("/api/v1/staff/attendance-exceptions/",{headers:checkHeaders,data:checkout});expect(replay.ok()).toBe(true);expect((await replay.json()).status).toBe("Approved");
  const own=await(await page.request.get(`/api/v1/staff/work-periods?date=${date}`,{headers:mobile})).json();expect(own.canEdit).toBe(false);expect(own.items).toHaveLength(2);expect(own.items[1].id).toBe(open.targetWorkPeriodId);expect(own.items[1].endUtc).toBe(`${date}T12:00:00+00:00`);expect(own.knownWorkedSeconds).toBe(3600);expect(own.totalWorkedSeconds).toBeNull();
  const report=await(await page.request.get(`/api/v1/reports/staff-daily?coachId=${member.membershipId}&allDates=true`)).json();expect(report.totalCount).toBe(1);expect(report.totalDeductions).toBe(0);expect(report.items[0].workPeriods.periodCount).toBe(2);expect(report.items[0].workPeriods.knownWorkedSeconds).toBe(3600);
  const history=await(await page.request.get(`${root}/history?date=${date}`)).json();expect(history.total).toBe(3);expect(history.items[0].after.periods[1].id).toBe(open.targetWorkPeriodId);
  const after=await(await page.request.get("/api/v1/reports/owner-summary")).json();expect(after.collections).toEqual(before.collections);expect(after.paymentRequests).toEqual(before.paymentRequests);
});
