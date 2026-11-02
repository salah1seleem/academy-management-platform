import { expect, test } from "@playwright/test";
import { mobileDemoToken } from "../support/mobile-demo-session";

test("Overnight staff checkout keeps the original workday across mobile, Web review, daily view and CSV", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button",{name:"دخول آمن"}).click(); await page.waitForURL("**/dashboard");
  const csrf=(await (await page.request.get("/api/v1/auth/csrf")).json()).token;
  const headers={"X-CSRF-TOKEN":csrf,"Idempotency-Key":crypto.randomUUID()};
  const stamp=Date.now(),phone=`015${String(stamp).slice(-8)}`,name=`مدرب وردية ليلية اصطناعي ${stamp}`;
  const memberResponse=await page.request.post("/api/v1/manage/coaches",{headers,data:{displayName:name,phoneNumber:phone,email:`overnight.${stamp}@example.test`,isActive:true,groupIds:[]}}); expect(memberResponse.ok(),await memberResponse.text()).toBe(true); const member=await memberResponse.json();
  const options=await (await page.request.get("/api/v1/manage/structure/options")).json(),branchId=options.branches[0].id;
  const employee=await page.request.post("/api/v1/manage/employees/",{headers,data:{name,phone,jobTitle:"وردية عرض ليلية",branchId,membershipId:member.membershipId,monthlySalary:0,contractStart:"2026-09-01",contractEnd:null,isActive:true}}); expect(employee.ok(),await employee.text()).toBe(true);
  const calendar=await (await page.request.get(`/api/v1/manage/staff-attendance/${member.membershipId}`)).json(); const today=calendar.today;
  const prior=new Date(`${today}T12:00:00Z`); prior.setUTCDate(prior.getUTCDate()-1); const workDate=prior.toISOString().slice(0,10);
  const manual=await page.request.put(`/api/v1/manage/staff-attendance/${member.membershipId}`,{headers,data:{date:workDate,status:"Present",checkIn:"23:30:00",checkOut:null,approvedDeduction:0,reason:null}}); expect(manual.ok(),await manual.text()).toBe(true);
  const token=await mobileDemoToken(page.request,phone,"انصراف وردية ليلية اصطناعية"),mobile={Authorization:`Bearer ${token}`};
  const own=await (await page.request.get("/api/v1/staff/attendance-exceptions/options",{headers:mobile})).json(); expect(own.openAttendance.workDate).toBe(workDate);
  const before=await (await page.request.get("/api/v1/reports/owner-summary")).json();
  const key=crypto.randomUUID(),body={branchId,operation:"CheckOut",claimedAtUtc:own.nowUtc,reason:"انصراف وردية ليلية اصطناعية بعد منتصف الليل",targetAttendanceId:own.openAttendance.id};
  const sent=await page.request.post("/api/v1/staff/attendance-exceptions/",{headers:{...mobile,"Idempotency-Key":key},data:body}); expect(sent.status(),await sent.text()).toBe(201); const claim=await sent.json(); expect(claim.workDate).toBe(workDate); expect(claim.attendanceId).toBeNull();
  await page.goto(`/dashboard/attendance/staff/exceptions/${claim.id}`); await expect(page.getByText(/يوم بدء الحضور:/)).toBeVisible();
  await page.getByRole("button",{name:"قبول وتسجيل إداري"}).click(); await expect(page.getByText("مقبول إداريًا",{exact:true})).toBeVisible();
  const saved=await (await page.request.get(`/api/v1/manage/staff-attendance-exceptions/${claim.id}`)).json(); expect(saved.currentAttendance.checkOutDate).toBe(today); expect(saved.request.attendanceId).toBe(own.openAttendance.id);
  const replay=await page.request.post("/api/v1/staff/attendance-exceptions/",{headers:{...mobile,"Idempotency-Key":key},data:body}); expect((await replay.json()).status).toBe("Approved");
  const report=await (await page.request.get(`/api/v1/reports/staff-daily?coachId=${member.membershipId}&allDates=true`)).json(); expect(report.totalCount).toBe(1); expect(report.items[0].workDate).toBe(workDate); expect(report.items[0].checkOutDate).toBe(today); expect(report.totalDeductions).toBe(0);
  const csv=await (await page.request.get(`/api/v1/reports/staff-daily/export?coachId=${member.membershipId}&allDates=true`)).text(); expect(csv).toContain("تاريخ الانصراف"); expect(csv).toContain(workDate); expect(csv).toContain(today);
  await page.goto(`/dashboard/attendance/staff/${member.membershipId}?date=${workDate}`); await expect(page.getByLabel("تاريخ الانصراف")).toHaveValue(today); await expect(page.getByLabel("التاريخ",{exact:true})).toHaveValue(workDate);
  for(const width of [390,1440]) { await page.setViewportSize({width,height:1000}); expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(width); await page.screenshot({path:info.outputPath(`overnight-record-${width}.png`),fullPage:true,animations:"disabled"}); }
  const after=await (await page.request.get("/api/v1/reports/owner-summary")).json(); expect(after.collections).toEqual(before.collections); expect(after.paymentRequests).toEqual(before.paymentRequests);
});
