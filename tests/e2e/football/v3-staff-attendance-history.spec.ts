import { expect, test } from "@playwright/test";

test("Manual staff attendance protects a stale editor and keeps a selected-day before/after trail without duplicating the report", async ({ page }, info) => {
  test.setTimeout(90_000); // Multiple real writes, reloads and navigation steps; no fixed waits or relaxed assertions.
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button",{name:"دخول آمن"}).click(); await page.waitForURL("**/dashboard");
  const headers={"X-CSRF-TOKEN":(await (await page.request.get("/api/v1/auth/csrf")).json()).token,"Idempotency-Key":crypto.randomUUID()};
  const stamp=Date.now(),phone=`015${String(stamp).slice(-8)}`,name=`مدرب سجل حضور اصطناعي ${stamp}`;
  const memberResponse=await page.request.post("/api/v1/manage/coaches",{headers,data:{displayName:name,phoneNumber:phone,email:`history.${stamp}@example.test`,isActive:true,groupIds:[]}}); expect(memberResponse.ok(),await memberResponse.text()).toBe(true); const member=await memberResponse.json();
  const options=await (await page.request.get("/api/v1/manage/structure/options")).json();
  const employee=await page.request.post("/api/v1/manage/employees/",{headers,data:{name,phone,jobTitle:"مراجعة سجل الحضور",branchId:options.branches[0].id,membershipId:member.membershipId,monthlySalary:0,contractStart:"2026-09-01",contractEnd:null,isActive:true}}); expect(employee.ok(),await employee.text()).toBe(true);
  const root=`/api/v1/manage/staff-attendance/${member.membershipId}`,calendar=await (await page.request.get(root)).json();
  const prior=new Date(`${calendar.today}T12:00:00Z`); prior.setUTCDate(prior.getUTCDate()-1); const date=prior.toISOString().slice(0,10),route=`/dashboard/attendance/staff/${member.membershipId}?date=${date}`;
  const financialBefore=await (await page.request.get("/api/v1/reports/owner-summary")).json();
  await page.goto(route); await page.getByLabel("الحالة",{exact:true}).selectOption("Present");
  for(const [label,hour] of [["وقت الحضور","8"],["وقت الانصراف","10"]]) { await page.getByLabel(`${label} — الساعة`).selectOption(hour); await page.getByLabel(`${label} — الدقيقة`).selectOption("00"); await page.getByLabel(`${label} — الفترة`).selectOption("am"); }
  await page.getByRole("button",{name:"حفظ الحضور",exact:true}).click(); await page.waitForURL("**/dashboard/attendance/staff");
  await page.goto(route); await expect(page.getByText("لا يوجد سجل سابق",{exact:true})).toBeVisible();
  const first=await (await page.request.get(`${root}?date=${date}`)).json(); expect(first.version).toBeGreaterThan(0);
  const change=await page.request.put(root,{headers:{...headers,"Idempotency-Key":crypto.randomUUID()},data:{date,version:first.version,status:"Absent",checkIn:null,checkOut:null,approvedDeduction:0,reason:null}}); expect(change.ok(),await change.text()).toBe(true);
  await page.getByRole("button",{name:"حفظ الحضور",exact:true}).click(); await expect(page.getByText("تغير سجل الحضور منذ فتحه؛ أعد تحميل السجل وراجع التعديل قبل الحفظ.")).toBeVisible();
  await expect(page.getByRole("button",{name:"حفظ الحضور",exact:true})).toBeDisabled(); expect((await (await page.request.get(`${root}?date=${date}`)).json()).status).toBe("Absent");
  await page.getByRole("button",{name:"إعادة تحميل السجل"}).click(); await expect(page.getByLabel("الحالة",{exact:true})).toHaveValue("Absent"); await expect(page.getByText("آخر 2 من 2 تعديل",{exact:true})).toBeVisible();
  await page.getByLabel("الحالة",{exact:true}).selectOption("Present");
  for(const [label,hour] of [["وقت الحضور","9"],["وقت الانصراف","11"]]) { await page.getByLabel(`${label} — الساعة`).selectOption(hour); await page.getByLabel(`${label} — الدقيقة`).selectOption("00"); await page.getByLabel(`${label} — الفترة`).selectOption("am"); }
  await page.getByRole("button",{name:"حفظ الحضور",exact:true}).click(); await page.waitForURL("**/dashboard/attendance/staff"); await page.goto(route); await expect(page.getByText("آخر 3 من 3 تعديل",{exact:true})).toBeVisible();
  const history=await (await page.request.get(`${root}/history?date=${date}`)).json(); expect(history.total).toBe(3); expect(history.items[0].before.status).toBe("Absent"); expect(history.items[0].after.checkIn).toBe("09:00:00"); expect(history.items[0].after.checkOut).toBe("11:00:00");
  expect((await (await page.request.get(`${root}/history?date=${calendar.today}`)).json()).total).toBe(0);
  const report=await (await page.request.get(`/api/v1/reports/staff-daily?coachId=${member.membershipId}&allDates=true`)).json(); expect(report.totalCount).toBe(1); expect(report.totalDeductions).toBe(0); expect(report.items[0].status).toBe("Present");
  for(const width of [390,1440]) { await page.setViewportSize({width,height:1000}); expect(await page.evaluate(()=>document.documentElement.scrollWidth)).toBeLessThanOrEqual(width); const button=await page.getByRole("button",{name:"حفظ الحضور",exact:true}).boundingBox(); expect(button!.height).toBeGreaterThanOrEqual(44); expect(button!.height).toBeLessThan(85); await page.screenshot({path:info.outputPath(`staff-history-${width}.png`),fullPage:true,animations:"disabled"}); }
  const financialAfter=await (await page.request.get("/api/v1/reports/owner-summary")).json(); expect(financialAfter.collections).toEqual(financialBefore.collections); expect(financialAfter.paymentRequests).toEqual(financialBefore.paymentRequests);
});
