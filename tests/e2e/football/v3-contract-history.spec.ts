import { expect, test } from "@playwright/test";

test("Dated employment contract preserves approved month, prorates next month and is read-only in mobile", async ({ page }, info) => {
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const csrf = (await (await page.request.get("/api/v1/auth/csrf")).json()).token, stamp = Date.now().toString(), phone = `0107${stamp.slice(-7)}`;
  const post = async (path: string, data: unknown) => { const r = await page.request.post(path, { headers: { "X-CSRF-TOKEN": csrf, "Idempotency-Key": crypto.randomUUID() }, data }); expect(r.ok(), await r.text()).toBe(true); return r.json(); };
  const get = async (path: string) => { const r = await page.request.get(path); expect(r.ok(), await r.text()).toBe(true); return r.json(); };
  const coach = (await post("/api/v1/manage/staff", { name: "مدرب عقد مؤرخ اصطناعي", email: `contract.${stamp}@example.test`, phone, temporaryPassword: "Demo-Only-123!", role: "Coach", jobTitle: "مدرب عرض" })).id;
  const options = await get("/api/v1/manage/employees");
  const employee = (await post("/api/v1/manage/employees", { name: "مدرب عقد مؤرخ اصطناعي", phone, jobTitle: "مدرب", branchId: options.branches[0].id, monthlySalary: 3000, contractStart: "2000-01-01", contractEnd: null, membershipId: coach, photoAssetId: null, isActive: true })).id;
  const root = `/api/v1/manage/employees/${employee}/contract-versions`, terms = await get(root), month = terms.currentMonth;
  const nextDate = new Date(`${month}T12:00:00Z`); nextDate.setUTCMonth(nextDate.getUTCMonth() + 1);
  const next = nextDate.toISOString().slice(0, 10), end = `${next.slice(0, 7)}-15`, days = new Date(Date.UTC(nextDate.getUTCFullYear(), nextDate.getUTCMonth() + 1, 0)).getUTCDate();
  const statementRoot = `/api/v1/manage/payroll/${employee}/statement`, before = await get(`${statementRoot}?month=${month}`);
  await post(`${statementRoot}/approve`, { month, quoteHash: before.quoteHash, note: null });
  const original = await get(`${statementRoot}?month=${month}`);
  await page.goto(`/dashboard/staff/employees/${employee}/contract`); await expect(page.getByRole("heading", { name: "سجل العقد وتغييراته", exact: true })).toBeVisible();
  await page.getByLabel("شهر سريان العقد").fill(next.slice(0, 7)); await page.getByLabel("عقد محدد المدة").check(); await page.getByLabel("تاريخ نهاية العقد").fill(end); await page.getByLabel("ملاحظة العقد — اختياري").fill("ملاحظة إدارية خاصة — لا تظهر للمدرب");
  await page.getByRole("button", { name: "مراجعة تغيير العقد" }).click(); await expect(page.getByRole("region", { name: "تأكيد العقد المؤرخ" })).toContainText("النسخة السابقة لا تُحذف");
  const saved = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`/employees/${employee}/contract-versions`));
  await page.getByRole("button", { name: "تأكيد اعتماد العقد" }).click(); expect((await saved).status()).toBe(201); await expect(page.getByRole("status").filter({ hasText: "الكشوف السابقة محفوظة" })).toBeVisible(); await page.getByRole("button", { name: "إغلاق رسالة النجاح" }).click();
  const after = await get(`${statementRoot}?month=${month}`); expect(after.statement.sourceHash).toBe(original.statement.sourceHash); expect(after.row.netDue).toBe(3000);
  const future = await get(`${statementRoot}?month=${next}`); expect(future.row.contractDays).toBe(15); expect(future.row.baseDue).toBe(Math.round(3000 * 15 / days * 100) / 100); expect(future.row.monthlySalary).toBe(3000); expect(future.canApprove).toBe(false);
  for (const width of [390, 1440]) { await page.setViewportSize({ width, height: 1000 }); await page.evaluate(() => scrollTo(0, 0)); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); expect(await page.locator(".contract-history-form > .check-inline").evaluate(el => getComputedStyle(el).display)).toBe("flex"); await expect(page.getByLabel("شهر سريان العقد")).toHaveValue(next.slice(0,7)); await page.screenshot({ path: info.outputPath(`contract-history-${width}.png`), fullPage: true, animations: "disabled" }); }
  await page.goto(`/dashboard/staff/employees/${employee}`); await expect(page.getByRole("link", { name: "تغيير مدة العقد من السجل المؤرخ" })).toHaveAttribute("href", `/dashboard/staff/employees/${employee}/contract`); await expect(page.getByLabel("تاريخ الانضمام")).toHaveAttribute("readonly", "");
  const otp = await page.request.post("/api/v1/mobile/auth/otp/request", { data: { phoneNumber: phone } }); expect(otp.status()).toBe(202);
  const verified = await page.request.post("/api/v1/mobile/auth/otp/verify", { data: { phoneNumber: phone, challengeId: (await otp.json()).challengeId, code: "246810", deviceName: "مراجعة عقد الموظف" } }); expect(verified.status()).toBe(200);
  const own = await page.request.get("/api/v1/staff/payroll/contract-versions", { headers: { Authorization: `Bearer ${(await verified.json()).accessToken}` } }); expect(own.status()).toBe(200); const mine = await own.json(); expect(mine.canChange).toBe(false); expect(mine.contractEnd).toBeNull(); expect(mine.items[0].contractEnd).toBe(end); expect(mine.items[0].note).toBeNull(); expect(JSON.stringify(mine)).not.toContain("ملاحظة إدارية خاصة");
});
