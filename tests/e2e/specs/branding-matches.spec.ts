import { expect, Page, test } from "@playwright/test";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { guardianLogin } from "../support/auth";
async function login(page: Page) { await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", {name:"دخول آمن"}).click(); await page.waitForURL("**/dashboard"); }
async function write(page: Page, path: string, data: unknown, method="POST", key?: string) { const token = await page.request.get("/api/v1/auth/csrf").then(r=>r.json()); return page.request.fetch(path,{method,data,headers:{"X-CSRF-TOKEN":token.token,...(key?{"Idempotency-Key":key}:{})}}); }
test("Compact stored-data dashboard has matched filter heights, drill-downs and no hero/latest", async ({page}, info) => {
  await page.setViewportSize({width:1440,height:1000}); await login(page);
  const kpis=page.getByRole("region",{name:"مؤشرات الأكاديمية"});
  await expect(kpis.getByRole("link")).toHaveCount(4);
  for(const link of await kpis.getByRole("link").all())await expect(link).toHaveAttribute("href",/\/dashboard\//);
  await expect(page.getByText("أحدث التحصيلات",{exact:true})).toHaveCount(0);
  await expect(page.locator(".sports-welcome")).toHaveCount(0);
  const from=await page.getByLabel("من تاريخ").boundingBox(), to=await page.getByLabel("إلى تاريخ").boundingBox(); expect(to!.height).toBe(from!.height);
  const subscription=page.locator("section").filter({has:page.getByRole("heading",{name:"الاشتراكات",exact:true})});
  await expect(subscription.getByRole("link")).toHaveCount(4);
  await expect(subscription.locator('a[href*="state=subscribed"]')).toHaveCount(1);
  await page.screenshot({path:info.outputPath("owner-dashboard-desktop.png"),fullPage:true});
  await page.setViewportSize({width:390,height:844}); await page.screenshot({path:info.outputPath("owner-dashboard-mobile.png"),fullPage:true});
  await subscription.locator('a[href*="state=subscribed"]').click(); await expect(page).toHaveURL(/players\/subscriptions\?.*state=subscribed/);
  await expect(page.getByRole("heading",{name:/اشتراكات/})).toBeVisible();
});
test("Published match reaches only its guardian and a separate tournament proof produces one receipt", async ({page}) => {
  await login(page); expect((await page.request.get("/api/v1/platform/academies")).status()).toBe(401);
  const options=await page.request.get("/api/v1/manage/matches/options").then(r=>r.json());
  const player=options.players.find((p:{playerId:string,sportId:string})=>p.playerId==="36000000-0000-0000-0000-000000000001"&&p.sportId==="32000000-0000-0000-0000-000000000001");
  const structure=await page.request.get("/api/v1/manage/structure/options").then(r=>r.json()); const category=structure.categories.find((c:{minimumBirthYear:number,maximumBirthYear:number})=>c.minimumBirthYear<=player.birthYear&&c.maximumBirthYear>=player.birthYear);
  const suffix=Date.now(); const tournamentResponse=await write(page,"/api/v1/manage/matches/tournaments",{name:`بطولة اختبار ${suffix}`,sportId:player.sportId,fee:250,currency:"EGP",isActive:true}); expect(tournamentResponse.ok()).toBeTruthy(); const tournament=(await tournamentResponse.json()).id;
  const teamResponse=await write(page,"/api/v1/manage/matches/teams",{name:`منتخب اختبار ${suffix}`,sportId:player.sportId,branchId:player.branchId,ageCategoryId:category.id,coachMembershipId:options.coaches[0].id,enrollmentIds:[player.id],isActive:true}); expect(teamResponse.ok()).toBeTruthy(); const team=(await teamResponse.json()).id;
  const match={tournamentId:tournament,teamId:team,opponent:"منافس عرض خيالي",matchDate:"2026-10-03",startTime:"17:00",venue:"ملعب اختبار",notes:"بيانات مصطنعة",benchPlayerIds:[],publish:true,isActive:true};
  const matchResponse=await write(page,"/api/v1/manage/matches/",match); expect(matchResponse.ok()).toBeTruthy(); const matchId=(await matchResponse.json()).id;
  await page.goto("/dashboard/matches"); await page.getByRole("button",{name:"المباريات",exact:true}).click(); await expect(page.getByRole("table",{name:"المباريات",exact:true}).getByRole("row").filter({hasText:`منتخب اختبار ${suffix} × منافس عرض خيالي`})).toContainText("2026-10-03");
  await guardianLogin(page); expect(await page.request.get("/api/v1/my-matches").then(r=>r.text())).toContain(matchId);
  expect(await page.request.get("/api/v1/my-matches?playerId=36000000-0000-0000-0000-000000000004").then(r=>r.json())).toEqual([]);
  const key=`tournament-e2e-${suffix}`, body={tournamentId:tournament,enrollmentId:player.id};
  const requested=await write(page,"/api/v1/guardian/tournament-payments/",body,"POST",key); expect(requested.status()).toBe(201); const id=(await requested.json()).id;
  expect((await write(page,"/api/v1/guardian/tournament-payments/",body,"POST",key)).status()).toBe(200);
  const csrf=await page.request.get("/api/v1/auth/csrf").then(r=>r.json()); const asset=await page.request.post("/api/v1/assets/upload",{headers:{"X-CSRF-TOKEN":csrf.token},multipart:{purpose:"Proof",file:{name:"synthetic-proof.jpg",mimeType:"image/jpeg",buffer:readFileSync(resolve(__dirname,"../../../apps/mobile/assets/demo/nutrition/breakfast-1.jpg"))}}}); expect(asset.status()).toBe(201);
  const assetId=(await asset.json()).id; expect((await write(page,`/api/v1/guardian/tournament-payments/${id}/proof`,{assetId})).status()).toBe(200);
  await page.context().clearCookies(); await login(page); await page.goto(`/dashboard/tournament-payments/${id}`);
  await expect(page.getByRole("link",{name:"عرض إثبات التحويل"})).toBeVisible(); expect((await page.request.get(`/api/v1/assets/${assetId}`)).status()).toBe(200);
  await page.getByRole("button",{name:"قبول وإصدار الإيصال"}).click(); await expect(page.getByText(/TRN-/)).toBeVisible();
  expect((await write(page,`/api/v1/manage/tournament-payments/${id}/review`,{accept:true})).status()).toBe(200);
  const report=await page.request.get("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28&collectionType=Tournament").then(r=>r.json()); expect(report.items.filter((x:{collectionId:string})=>x.collectionId===id)).toHaveLength(1);
});
