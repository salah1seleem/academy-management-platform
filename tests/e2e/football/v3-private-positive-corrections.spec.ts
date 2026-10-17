import { expect, test } from "@playwright/test";
import { endedFootballSession } from "../support/ended-football-session";

test("Positive private correction preserves paid statement and adds only the reviewed delta to the selected month", async ({ page }, info) => {
  test.setTimeout(120_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const csrf = (await (await page.request.get("/api/v1/auth/csrf")).json()).token;
  const post = async (path: string, data: unknown) => { const r = await page.request.post(path, { headers: { "X-CSRF-TOKEN": csrf, "Idempotency-Key": crypto.randomUUID() }, data }); expect(r.ok(), await r.text()).toBe(true); return r.json(); };
  const get = async (path: string) => { const r = await page.request.get(path); expect(r.ok(), await r.text()).toBe(true); return r.json(); };
  const options = await get("/api/v1/manage/sessions/options");
  const seededCoach = options.coaches.find((c: { name: string }) => c.name === "محمود طارق سليمان").id;
  const seededRoot = `/api/v1/manage/payroll/coaches/${seededCoach}/private-services`;
  const seed = (await get(seededRoot)).items.find((r: { serviceDate: string; ordinal: number }) => r.serviceDate === "2026-10-01" && r.ordinal === 1);
  const enrollment = (await get(`${seededRoot}/options`)).packages.find((p: { id: string }) => p.id === seed.packageId).sportEnrollmentId;
  const plan = (await get(`/api/v1/finance/receivables/${seed.documentId}`)).document.planId;
  const terms = (await get(`/api/v1/manage/payroll/coaches/${seededCoach}/agreements`)).items.find((r: { branch: string; sport: string; approvedAtUtc: string }) => r.branch === seed.branch && r.sport === seed.sport && r.approvedAtUtc);
  const stamp = Date.now().toString(), phone = `0108${stamp.slice(-7)}`;
  const coach = (await post("/api/v1/manage/staff", { name: "مدرب زيادة عمولة اصطناعي", email: `positive.${stamp}@example.test`, phone, temporaryPassword: "Demo-Only-123!", role: "Coach", jobTitle: "مدرب عرض" })).id;
  const employee = (await post("/api/v1/manage/employees", { name: "مدرب زيادة عمولة اصطناعي", phone, jobTitle: "مدرب", branchId: terms.branchId, monthlySalary: 3000, contractStart: "2000-01-01", contractEnd: null, membershipId: coach, photoAssetId: null, isActive: true })).id;
  const agreementRoot = `/api/v1/manage/payroll/coaches/${coach}/agreements`;
  const serviceDate = (await get(agreementRoot)).today;
  const agreement = (await post(agreementRoot, { branchId: terms.branchId, sportId: terms.sportId, effectiveFrom: serviceDate, effectiveTo: null, variableMethod: "Percent", variableValue: 40, eligibility: "DeliveredCollected", baseCoverage: "GroupOnly" })).id;
  await post(`${agreementRoot}/${agreement}/approve`, { note: null });
  const group = options.groups.find((g: { name: string }) => g.name === "خاص — مصادر أجر اصطناعية").id;
  await post(`/api/v1/manage/groups/${group}/coaches`, { membershipId: coach });
  const document = (await post("/api/v1/finance/receivables/", { sportEnrollmentId: enrollment, planId: plan, amount: "2000.00", dueDate: "2026-10-01", source: "ManualCharge", description: "مصدر تصحيح عمولة اصطناعي بعد الصرف" })).id;
  const slot = await endedFootballSession();
  expect(slot.sessionDate >= serviceDate).toBe(true);
  const session = (await post("/api/v1/attendance/sessions", { trainingGroupId: group, ...slot })).id;
  const root = `/api/v1/manage/payroll/coaches/${coach}/private-services`;
  const pack = (await post(`${root}/packages`, { documentId: document, sessionCount: 8 })).id;
  await post(`${root}/${pack}/deliveries`, { trainingSessionId: session, ordinal: 1 });
  const delivery = (await get(root)).items[0].id, path = `${root}/${delivery}`;
  for (let i = 0; i < 1; i++) {
    const previous = new Set((await get(`/api/v1/finance/receivables/${document}/collections`)).map((c: { id: string }) => c.id));
    const renewal = await post("/api/v1/subscriptions/admin-renewals", { sportEnrollmentId: enrollment, subscriptionPlanId: plan });
    await post(`/api/v1/subscriptions/payments/${renewal.paymentId}/simulate`, { outcome: "Success" });
    const funds = (await get(`/api/v1/finance/receivables/${document}/collections`)).find((c: { id: string; available: string }) => !previous.has(c.id) && Number(c.available) >= 1000);
    expect(funds).toBeTruthy(); await post(`/api/v1/finance/receivables/${document}/movements`, { kind: "PaymentAllocation", amount: "1000.00", collectionId: funds.id, reason: "تخصيص اختبار داخلي" });
  }
  const q = await get(`${path}/posting-preview`); await post(`${path}/entries`, { action: q.action, quoteHash: q.quoteHash });
  const clock = await get(`/api/v1/manage/employees/${employee}/salary-versions`), month = clock.currentMonth;
  const statementRoot = `/api/v1/manage/payroll/${employee}/statement`;
  const statement = await get(`${statementRoot}?month=${month}`); expect(statement.row.netDue).toBe(3050);
  await post(`${statementRoot}/approve`, { month, quoteHash: statement.quoteHash, note: null });
  await post(`${statementRoot}/disbursements`, { month, amount: 3050, expectedPaid: 0, expectedRemaining: 3050, reference: "صرف اصطناعي — لا تحويل حقيقي" });
  const original = await get(`${statementRoot}?month=${month}`);
  for (let i = 0; i < 1; i++) {
    const previous = new Set((await get(`/api/v1/finance/receivables/${document}/collections`)).map((c: { id: string }) => c.id));
    const renewal = await post("/api/v1/subscriptions/admin-renewals", { sportEnrollmentId: enrollment, subscriptionPlanId: plan });
    await post(`/api/v1/subscriptions/payments/${renewal.paymentId}/simulate`, { outcome: "Success" });
    const funds = (await get(`/api/v1/finance/receivables/${document}/collections`)).find((c: { id: string; available: string }) => !previous.has(c.id) && Number(c.available) >= 1000);
    expect(funds).toBeTruthy(); await post(`/api/v1/finance/receivables/${document}/movements`, { kind: "PaymentAllocation", amount: "1000.00", collectionId: funds.id, reason: "تخصيص اختبار داخلي" });
  }
  await page.goto(`/dashboard/academy/coaches/${coach}`);
  await page.getByRole("tab", {name:"الاتفاقات والمستحقات",exact:true}).click();
  const row = page.getByRole("region", { name: "تنفيذ الخاص وعمولاته" }).locator(".linked-list > article").filter({ hasText: document.slice(0, 8) });
  await row.getByRole("button", { name: "مراجعة الاستحقاق" }).click();
  const review = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`${delivery}/entries`));
  await page.getByRole("button", { name: "حفظ استثناء للمراجعة" }).click(); expect((await review).status()).toBe(201); await expect(page.getByRole("dialog")).toHaveCount(0);
  await page.getByRole("button", { name: "إغلاق رسالة النجاح" }).click();
  await row.getByRole("button", { name: "مراجعة الاستحقاق" }).click();
  const modal = page.getByRole("dialog", { name: "مراجعة عمولة الخاص" });
  await expect(modal.getByRole("heading", { name: "تصحيح عمولة بعد الاعتماد" })).toBeVisible();
  await modal.getByRole("button", { name: "معاينة شهر التصحيح" }).click();
  const correction = await get(`${path}/correction-preview`); expect(correction).toMatchObject({ canCorrect: true, difference: 50 });
  await expect(modal.getByLabel("شهر تصحيح العمولة")).toHaveValue(correction.month.slice(0, 7));
  await modal.getByLabel("ملاحظة التصحيح — اختياري").fill("مراجعة خاصة بالمالك — لا تعرض للمدرب");
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    await expect(modal).toBeInViewport(); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    const form = modal.getByRole("region", { name: "تصحيح عمولة بعد الاعتماد" });
    expect(await form.locator("label").first().evaluate(el => getComputedStyle(el).display)).toBe("grid");
    expect(await form.locator("textarea").evaluate(el => el.clientWidth <= el.parentElement!.clientWidth)).toBe(true);
    await form.screenshot({ path: info.outputPath(`private-positive-correction-${width}.png`), animations: "disabled" });
  }
  const confirm = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`${delivery}/corrections`));
  await modal.getByRole("button", { name: "تأكيد تصحيح العمولة للشهر المختار" }).click(); expect((await confirm).status()).toBe(201); await expect(modal).toHaveCount(0);
  const preserved = await get(`${statementRoot}?month=${month}`); expect(preserved.statement.sourceHash).toBe(original.statement.sourceHash); expect(preserved.row).toMatchObject({ netDue: 3050, paidAmount: 3050, remainingAmount: 0, deductions: 0 });
  const next = await get(`${statementRoot}?month=${correction.month}`); expect(next.row).toMatchObject({ monthlySalary: 3000, deductions: 0, additions: 50, netDue: 3050, privateCompensation: 50 }); expect(next.canApprove).toBe(false);
  const history = await get(`${path}/entries`); expect(history.map((r: { delta: number }) => r.delta)).toEqual([50, 0, 50]); expect(history[0].reviewEntryId).toBe(history[1].id);
  const otp = await page.request.post("/api/v1/mobile/auth/otp/request", { data: { phoneNumber: phone } }); expect(otp.status()).toBe(202);
  const verified = await page.request.post("/api/v1/mobile/auth/otp/verify", { data: { phoneNumber: phone, challengeId: (await otp.json()).challengeId, code: "246810", deviceName: "مراجعة تصحيح العمولة" } }); expect(verified.status()).toBe(200);
  const own = await page.request.get("/api/v1/staff/payroll/private-services", { headers: { Authorization: `Bearer ${(await verified.json()).accessToken}` } }); expect(own.status()).toBe(200);
  const mine = (await own.json()).items[0]; expect(mine).toMatchObject({ postedAmount: 100, pendingDelta: 0, corrections: [{ dueDate: correction.month, delta: 50 }] }); expect(JSON.stringify(mine)).not.toContain("مراجعة خاصة بالمالك"); expect(mine).not.toHaveProperty("player"); expect(mine).not.toHaveProperty("documentId");
});
