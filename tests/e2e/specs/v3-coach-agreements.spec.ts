import { expect, test } from "@playwright/test";

test("Owner versions private coaching terms from the coach profile; mobile coach reads approved own terms without creating payables", async ({ page }, info) => {
  test.setTimeout(90_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const stamp = Date.now(), phone = `010${String(stamp).slice(-8)}`;
  const headers = { "X-CSRF-TOKEN": (await (await page.request.get("/api/v1/auth/csrf")).json()).token };
  const coachResponse = await page.request.post("/api/v1/manage/staff", { headers, data: { name: `مدرب اتفاق عرض ${stamp}`, email: `coach.agreement.${stamp}@example.test`, phone, temporaryPassword: "Demo-Only-123!", role: "Coach" } }); expect(coachResponse.status()).toBe(200); const coach = (await coachResponse.json()).id;
  const options = await (await page.request.get("/api/v1/manage/structure/options")).json(); const branch = options.branches[0].id, sport = options.sports[0].id;
  const employeeResponse = await page.request.post("/api/v1/manage/employees", { headers, data: { name: `موظف اتفاق عرض ${stamp}`, phone, jobTitle: "مدرب", branchId: branch, monthlySalary: 3000, contractStart: "2026-01-01", contractEnd: null, membershipId: coach, photoAssetId: null } }); expect(employeeResponse.status()).toBe(201); const employee = (await employeeResponse.json()).id;
  const path = `/api/v1/manage/payroll/coaches/${coach}/agreements`;
  await page.goto(`/dashboard/academy/coaches/${coach}`); await page.getByRole("tab", { name: "الاتفاقات والمستحقات", exact: true }).click(); await expect(page.getByRole("heading", { name: "اتفاقات أجر الخاص", exact: true })).toBeVisible();
  const data = await (await page.request.get(path)).json(); const tomorrow = new Date(`${data.today}T12:00:00Z`); tomorrow.setUTCDate(tomorrow.getUTCDate() + 1); const nextDate = tomorrow.toISOString().slice(0, 10);
  async function draft(value: string, from: string) {
    await page.getByRole("button", { name: "نسخة اتفاق جديدة", exact: true }).click(); await page.getByLabel("فرع الاتفاق").selectOption(branch); await page.getByLabel("رياضة الاتفاق").selectOption(sport);
    await page.getByLabel("النسبة المئوية").fill(value); await page.getByLabel("ساري من").fill(from);
    await page.getByRole("button", { name: "حفظ مسودة الاتفاق" }).click(); await expect(page.getByText("تم حفظ مسودة الاتفاق؛ لا تصبح نافذة قبل الاعتماد.")).toBeVisible();
  }
  async function approve() {
    await page.getByRole("button", { name: "مراجعة واعتماد", exact: true }).click(); await page.getByRole("button", { name: "تأكيد اعتماد الاتفاق", exact: true }).click(); await expect(page.getByText("تم اعتماد نسخة الاتفاق وحفظ شروطها دون تغيير النسخ السابقة.")).toBeVisible();
  }
  await draft("40", data.today); expect((await (await page.request.get(`${path}/active?branchId=${branch}&sportId=${sport}&on=${data.today}`)).json()).configured).toBe(false);
  await approve(); const first = (await (await page.request.get(path)).json()).items.find((a: { version: number }) => a.version === 1); expect(first.variableValue).toBe(40);
  await draft("30", nextDate); await approve();
  const after = await (await page.request.get(path)).json(); expect(after.items.find((a: { id: string }) => a.id === first.id).variableValue).toBe(40); expect(after.items.filter((a: { version: number }) => a.version)).toHaveLength(2);
  const active = await (await page.request.get(`${path}/active?branchId=${branch}&sportId=${sport}&on=${data.today}`)).json(); expect(active.agreementId).toBe(first.id);
  await page.getByRole("button", { name: "إغلاق رسالة النجاح" }).click();
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 }); await page.getByRole("region", { name: "اتفاقات أجر الخاص" }).scrollIntoViewIfNeeded(); await page.evaluate(() => window.scrollBy(0, -80));
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`coach-agreements-${width}.png`), animations: "disabled" });
  }
  const request = await page.request.post("/api/v1/mobile/auth/otp/request", { data: { phoneNumber: phone } }); expect(request.status()).toBe(202);
  const verified = await page.request.post("/api/v1/mobile/auth/otp/verify", { data: { phoneNumber: phone, challengeId: (await request.json()).challengeId, code: "246810", deviceName: "اختبار اتفاق المدرب" } }); expect(verified.status()).toBe(200); let credential = await verified.json();
  if (credential.membershipId !== coach) { const selected = await page.request.post("/api/v1/mobile/auth/select-role", { headers: { Authorization: `Bearer ${credential.accessToken}` }, data: { membershipId: coach } }); expect(selected.status()).toBe(200); credential = await selected.json(); }
  const own = await page.request.get("/api/v1/staff/payroll/agreements", { headers: { Authorization: `Bearer ${credential.accessToken}` } }); expect(own.status()).toBe(200); const ownData = await own.json(); expect(ownData.items).toHaveLength(2); expect(ownData.items.every((a: { baseSalarySnapshot: unknown; note: unknown }) => a.baseSalarySnapshot === null && a.note === null)).toBe(true);
  const report = await (await page.request.get(`/api/v1/manage/payroll?month=${data.today.slice(0, 7)}-01`)).json(); expect(report.items.find((e: { employeeId: string }) => e.employeeId === employee)).toMatchObject({ monthlySalary: 3000, additions: 0, isPaid: false });
});
