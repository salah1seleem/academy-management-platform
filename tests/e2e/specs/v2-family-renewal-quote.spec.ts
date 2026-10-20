import { expect, test } from "@playwright/test";

test("Guardian reviews siblings and multi-sport plans without creating payments", async ({ page, browser }, info) => {
  const staffContext=await browser.newContext({baseURL:info.project.use.baseURL}); const staff=await staffContext.newPage();
  await staff.goto("/login"); await staff.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await staff.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await staff.getByRole("button",{name:"دخول آمن"}).click(); await staff.waitForURL("**/dashboard");
  const stamp=Date.now(), phone=`012${String(stamp).slice(-8)}`,token=(await (await staff.request.get("/api/v1/auth/csrf")).json()).token,headers={"X-CSRF-TOKEN":token};
  const structure=await (await staff.request.get("/api/v1/manage/structure/options")).json();
  const swimming=structure.groups.find((g:{id:string})=>g.id==="34000000-0000-0000-0000-000000000002");
  const created=await staff.request.post("/api/v1/manage/groups",{headers,data:{arabicName:`أسرة متعددة الرياضات ${stamp}`,branchId:"31000000-0000-0000-0000-000000000001",sportId:"32000000-0000-0000-0000-000000000001",ageCategoryId:swimming.ageCategoryId,capacity:20}}); expect(created.status()).toBe(201); const footballGroup=await created.json();
  const family:string[]=[];
  for(const name of ["آدم","ليلى"]){const registered=await staff.request.post("/api/v1/people/registrations",{headers,data:{playerArabicName:`${name} أسرة اقتباس اصطناعي ${stamp}`,dateOfBirth:"2016-01-01",guardianName:`ولي أمر اقتباس اصطناعي ${stamp}`,guardianPhone:phone,branchId:"31000000-0000-0000-0000-000000000001",sportId:"32000000-0000-0000-0000-000000000001",groupId:footballGroup}});expect(registered.status(),await registered.text()).toBe(201);family.push((await registered.json()).playerId);}
  const extra=await staff.request.post("/api/v1/people/registrations",{headers,data:{existingPlayerId:family[0],guardianPhone:phone,guardianName:`ولي أمر اقتباس اصطناعي ${stamp}`,branchId:swimming.branchId,sportId:swimming.sportId,groupId:swimming.id}});expect(extra.status(),await extra.text()).toBe(201);
  await staffContext.close();
  const root = "/api/v1/guardian/subscriptions/family";
  const csrf = { "X-CSRF-TOKEN": (await page.request.get("/api/v1/auth/csrf").then(r => r.json())).token };
  const challenge = await page.request.post("/api/v1/auth/guardian/otp/request", { headers: csrf, data: { phoneNumber: phone } }); expect(challenge.ok()).toBe(true);
  const verify = await page.request.post("/api/v1/auth/guardian/otp/verify", { headers: csrf, data: { phoneNumber: phone, challengeId: (await challenge.json()).challengeId, code: "246810" } }); expect(verify.ok()).toBe(true);
  const options = await page.request.get(`${root}/options`).then(r => r.json());
  const enrollments = options.items.filter((e: { playerId: string; sport: string; plans: unknown[] }) => family.includes(e.playerId) && e.plans.length && (e.playerId === family[0] || e.sport === "كرة القدم")); expect(enrollments).toHaveLength(3);
  const receipts = await page.request.get("/api/v1/guardian/subscriptions/receipts"); expect(receipts.ok()).toBe(true); const before = await receipts.json();
  await page.goto("/guardian"); await page.getByRole("link", { name: "مراجعة تجديد الأبناء معًا", exact: true }).click();
  for (const e of enrollments) {
    await page.getByRole("checkbox", { name: `تجديد ${e.player} · ${e.sport}`, exact: true }).check();
    await page.getByRole("combobox", { name: `الباقة لـ${e.player} · ${e.sport}`, exact: true }).selectOption(e.plans[0].id);
  }
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 }); await page.evaluate(() => scrollTo(0, 0));
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`family-choices-${width}.png`), animations: "disabled" });
  }
  const response = page.waitForResponse(r => new URL(r.url()).pathname === `${root}/quote` && r.request().method() === "POST");
  await page.getByRole("button", { name: "مراجعة إجمالي 3 تسجيل", exact: true }).click(); const result = await response; expect(result.ok(), await result.text()).toBe(true); const quote = await result.json(); expect(quote.items).toHaveLength(3);
  await expect(page.getByRole("region", { name: "ملخص تجديد الأسرة" })).toBeVisible(); await expect(page.getByText(/لم يُنشأ طلب دفع/)).toBeVisible(); await expect(page.getByRole("checkbox")).toHaveCount(0);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 }); await page.evaluate(() => scrollTo(0, 0)); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: info.outputPath(`family-summary-${width}.png`), fullPage: true, animations: "disabled" });
  }
  await page.getByRole("button", { name: "تعديل التسجيلات والباقات" }).scrollIntoViewIfNeeded();
  await expect(page.getByRole("button", { name: "تعديل التسجيلات والباقات" })).toBeInViewport();
  await page.getByRole("button", { name: "تعديل التسجيلات والباقات" }).click(); for (const e of enrollments) await expect(page.getByRole("checkbox", { name: `تجديد ${e.player} · ${e.sport}`, exact: true })).toBeChecked();
  expect(await page.request.get("/api/v1/guardian/subscriptions/receipts").then(r => r.json())).toEqual(before);
});
