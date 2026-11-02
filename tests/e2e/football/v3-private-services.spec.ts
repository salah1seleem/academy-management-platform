import { expect, test } from "@playwright/test";
import { mobileDemoToken } from "../support/mobile-demo-session";

// Football config enables only reserved synthetic sources in an explicit test DB.
test("Owner reviews actual private delivery against one existing invoice; coach reads compensation without family billing data", async ({ page }, info) => {
  test.setTimeout(90_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const opts = await (await page.request.get("/api/v1/manage/sessions/options")).json();
  const coach = opts.coaches.find((c: { name: string }) => c.name === "محمود طارق سليمان").id;
  const root = `/api/v1/manage/payroll/coaches/${coach}/private-services`;
  const initial = await (await page.request.get(root)).json(); const seed = initial.items.find((r: { serviceDate: string; ordinal: number }) => r.serviceDate === "2026-10-01" && r.ordinal === 1); expect(seed).toMatchObject({ sessionCount: 8, variableValue: 40, earned: 50, contingent: 50, serviceNet: 250 });
  const sources = await (await page.request.get(`${root}/options`)).json(); const originalPackage = sources.packages.find((p: { id: string }) => p.id === seed.packageId);
  const doc = (await (await page.request.get(`/api/v1/finance/receivables/${seed.documentId}`)).json()).document;
  const groups = opts;
  const group = groups.groups.find((g: { name: string }) => g.name === "خاص — مصادر أجر اصطناعية").id;
  const headers = { "X-CSRF-TOKEN": (await (await page.request.get("/api/v1/auth/csrf")).json()).token, "Idempotency-Key": crypto.randomUUID() };
  const created = await page.request.post("/api/v1/finance/receivables/", { headers, data: { sportEnrollmentId: originalPackage.sportEnrollmentId, planId: doc.planId, amount: "2000.00", dueDate: "2026-10-01", source: "ManualCharge", description: "مطالبة مستقلة لرحلة مراجعة الخاص — اصطناعية" } }); expect(created.status()).toBe(201); const documentId = (await created.json()).id;
  const seconds = Math.floor(Date.now() / 1000) % 3500, h = "00", m = String(Math.floor(seconds / 60) % 60).padStart(2, "0"), s = String(seconds % 60).padStart(2, "0");
  // A historical fixture date covered by the explicitly approved demo agreement.
  const endSeconds = seconds + 1, endM = String(Math.floor(endSeconds / 60) % 60).padStart(2, "0"), endS = String(endSeconds % 60).padStart(2, "0");
  const sessionResponse = await page.request.post("/api/v1/attendance/sessions", { headers, data: { trainingGroupId: group, sessionDate: "2026-10-03", startTime: `${h}:${m}:${s}`, endTime: `${h}:${endM}:${endS}` } }); expect(sessionResponse.status()).toBe(201); const sessionId = (await sessionResponse.json()).id;
  await page.goto(`/dashboard/academy/coaches/${coach}`); await page.getByRole("tab", {name:"الاتفاقات والمستحقات",exact:true}).click(); const section = page.getByRole("region", { name: "تنفيذ الخاص وعمولاته" }); await expect(section).toBeVisible(); await section.getByText("مراجعة تنفيذ حصة خاصة", { exact: true }).click();
  await section.getByLabel("المطالبة الموجودة").selectOption(documentId); await section.getByRole("button", { name: "تثبيت توزيع الحصص" }).click();
  const bound = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("private-services/packages")); await page.getByRole("button", { name: "تأكيد مراجعة الخدمة" }).click(); expect((await bound).status()).toBe(201);
  await expect(section.getByRole("status")).toBeVisible();
  // Read persisted state through the authenticated API, not a browser response
  // body that Chromium may release after the form immediately reloads its data.
  const boundSources = await (await page.request.get(`${root}/options`)).json(); const packageId = boundSources.packages.find((p: { documentId: string }) => p.documentId === documentId).id;
  const replay = await page.request.post(`${root}/packages`, { headers, data: { documentId, sessionCount: 8 } }); expect(replay.status()).toBe(200); expect((await replay.json()).id).toBe(packageId);
  await section.getByLabel("باقة الخدمة").selectOption(packageId); await section.getByLabel("الحصة المنفذة").selectOption(sessionId); await section.getByLabel("رقم الحصة في الباقة").fill("1");
  await section.getByRole("button", { name: "مراجعة واعتماد التنفيذ" }).click(); await expect(page.getByRole("dialog")).toContainText("الحضور وحده ليس الإثبات");
  const posted = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`${packageId}/deliveries`)); await page.getByRole("button", { name: "تأكيد مراجعة الخدمة" }).click(); expect((await posted).status()).toBe(201);
  await expect(section.getByText("التقييم المستوفي الحالي")).toBeVisible(); const after = await (await page.request.get(root)).json(); expect(after.items.find((r: { documentId: string }) => r.documentId === documentId)).toMatchObject({ earned: 0, contingent: 100, variableValue: 40, postedAmount: 0, postingCount: 0 });
  await page.getByRole("button", { name: "إغلاق رسالة النجاح" }).click();
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 1000 }); await section.evaluate(el => el.scrollIntoView({ block: "start" })); await page.evaluate(() => scrollBy(0, -80)); await expect(section.getByRole("heading", { name: "تنفيذ الخاص وعمولاته", exact: true })).toBeInViewport(); await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await section.screenshot({ path: info.outputPath(`private-services-${width}.png`), animations: "disabled" }); }
  const mobileToken = await mobileDemoToken(page.request, "+201099900020", "رحلة عرض اصطناعية");
  const own = await page.request.get("/api/v1/staff/payroll/private-services", { headers: { Authorization: `Bearer ${mobileToken}` } }); expect(own.status()).toBe(200); const ownData = await own.json(); expect(ownData.items.length).toBe(after.items.length); expect(ownData.items.every((r: object) => !("player" in r) && !("documentId" in r) && !("serviceNet" in r))).toBe(true);
});
