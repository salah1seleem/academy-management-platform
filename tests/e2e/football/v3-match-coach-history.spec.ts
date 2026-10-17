import { expect, test } from "@playwright/test";
import { mobileDemoToken } from "../support/mobile-demo-session";

test("Match coach remains independent of team changes, explicit review is audited and reaches only the assigned coach", async ({page},info)=>{
  test.setTimeout(90_000);
  await page.goto("/login");await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test");await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");await page.getByRole("button",{name:"دخول آمن"}).click();await page.waitForURL("**/dashboard");
  const headers={"X-CSRF-TOKEN":(await(await page.request.get("/api/v1/auth/csrf")).json()).token};
  const options=await(await page.request.get("/api/v1/manage/matches/options")).json();expect(options.coachHistoryEnabled).toBe(true);
  const team=options.teams.find((t:{name:string})=>t.name.includes("منتخب 2018"));expect(team).toBeTruthy();
  const coaches=await(await page.request.get("/api/v1/manage/staff/")).json();
  const original=coaches.members.find((m:{id:string})=>m.id===team.coachMembershipId);const replacement=coaches.members.find((m:{email:string})=>m.email==="coach2.football@example.test");expect(original).toBeTruthy();expect(replacement).toBeTruthy();
  const tournament=options.tournaments.find((t:{sportId:string})=>t.sportId===team.sportId);
  const body={tournamentId:tournament.id,teamId:team.id,opponent:`منافس إسناد اصطناعي ${Date.now()}`,matchDate:"2026-10-26",startTime:"18:00:00",venue:"ملعب مراجعة",notes:null,ourScore:null,opponentScore:null,benchPlayerIds:[],publish:true,isActive:true};
  const created=await page.request.post("/api/v1/manage/matches/",{headers,data:body});expect(created.status()).toBe(200);const id=(await created.json()).id;
  const first=await(await page.request.get(`/api/v1/manage/matches/${id}`)).json();expect(first.assignedCoachMembershipId).toBe(original.id);expect(first.coachAssignmentVersion).toBe(1);
  const changed=await page.request.put(`/api/v1/manage/matches/teams/${team.id}`,{headers,data:{...team,coachMembershipId:replacement.id}});expect(changed.status()).toBe(200);
  const before=await(await page.request.get("/api/v1/reports/owner-summary")).json();expect(before.collections).toEqual(expect.any(Object));
  const originalToken=await mobileDemoToken(page.request,original.phoneNumber,"مدرب إسناد اصطناعي");const replacementToken=await mobileDemoToken(page.request,replacement.phoneNumber,"مدرب بديل اصطناعي");
  const feed=async(token:string)=>{const r=await page.request.get("/api/v1/my-matches",{headers:{Authorization:`Bearer ${token}`}});expect(r.status()).toBe(200);return r.json();};
  expect((await feed(originalToken)).some((m:{id:string})=>m.id===id)).toBe(true);expect((await feed(replacementToken)).some((m:{id:string})=>m.id===id)).toBe(false);
  await page.goto(`/dashboard/matches?matchId=${id}`);const editor=page.getByRole("form",{name:"محرر المباراة"});await expect(editor.getByLabel("مدرب المباراة")).toHaveValue(original.id);
  await editor.getByLabel("مدرب المباراة").selectOption(replacement.id);await expect(editor.getByLabel("ملاحظة إسناد المدرب (اختياري)")).not.toHaveAttribute("required");
  const saving=page.waitForResponse(r=>r.request().method()==="PUT"&&r.url().endsWith(`/matches/${id}`));await editor.getByRole("button",{name:"حفظ التعديل",exact:true}).click();expect((await saving).status()).toBe(200);await page.getByRole("button",{name:"إغلاق رسالة النجاح"}).click();
  expect((await feed(originalToken)).some((m:{id:string})=>m.id===id)).toBe(false);const assigned=(await feed(replacementToken)).find((m:{id:string})=>m.id===id);expect(assigned.coachAssignmentSource).toBe("Confirmed");expect(assigned.coachName).toBe(replacement.name);
  await page.goto(`/dashboard/matches?matchId=${id}`);await expect(editor.getByLabel("مدرب المباراة")).toHaveValue(replacement.id);await page.getByText("سجل إسناد مدرب المباراة",{exact:true}).click();const history=page.locator("details").filter({hasText:"سجل إسناد مدرب المباراة"});await expect(history).toContainText("نسخة 2");await expect(history).toContainText("نسخة 1");await expect(history).toContainText("تغيير إسناد");
  for(const width of [390,1440]){await page.setViewportSize({width,height:1100});await history.scrollIntoViewIfNeeded();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1)).toBe(true);await page.screenshot({path:info.outputPath(`match-coach-history-${width}.png`),animations:"disabled"});}
  const after=await(await page.request.get("/api/v1/reports/owner-summary")).json();expect(after.collections).toEqual(before.collections);expect(after.paymentRequests).toEqual(before.paymentRequests);
});
