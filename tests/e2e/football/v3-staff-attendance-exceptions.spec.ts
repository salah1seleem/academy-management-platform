import { expect, test } from "@playwright/test";
import { mobileDemoToken } from "../support/mobile-demo-session";

test("Staff claim is pending until independent Web review, rereads in mobile/report, rejects checkout without financial effect", async ({ page }, info) => {
  test.setTimeout(120_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.football@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!"); await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const csrf = (await (await page.request.get("/api/v1/auth/csrf")).json()).token;
  const stamp = Date.now(), phone = `015${String(stamp).slice(-8)}`, name = `موظف مراجعة حضور اصطناعي ${stamp}`;
  const post = async (path: string, data: unknown) => { const r = await page.request.post(path, { headers: { "X-CSRF-TOKEN": csrf, "Idempotency-Key": crypto.randomUUID() }, data }); expect(r.ok(), await r.text()).toBe(true); return r.json(); };
  const created = await post("/api/v1/manage/coaches", { displayName: name, phoneNumber: phone, email: `claim.${stamp}@example.test`, isActive: true, groupIds: [] });
  const options = await (await page.request.get("/api/v1/manage/structure/options")).json(); const branchId = options.branches[0].id;
  await post("/api/v1/manage/employees/", { name, phone, jobTitle: "موظف عرض بلا راتب مالي", branchId, membershipId: created.membershipId, monthlySalary: 0, contractStart: "2026-09-01", contractEnd: null, isActive: true });
  const token = await mobileDemoToken(page.request, phone, "رحلة إثبات حضور اصطناعية"); const mobileHeaders = { Authorization: `Bearer ${token}` };
  const ownOptions = await (await page.request.get("/api/v1/staff/attendance-exceptions/options", { headers: mobileHeaders })).json(); expect(ownOptions.configured).toBe(true); expect(ownOptions.branches.map((b: { id: string }) => b.id)).toContain(branchId);
  const before = await (await page.request.get("/api/v1/reports/owner-summary")).json();
  const requestKey = crypto.randomUUID(), claimed = new Date(Date.parse(ownOptions.nowUtc) - 20 * 60_000).toISOString();
  const body = { branchId, operation: "CheckIn", claimedAtUtc: claimed, reason: "تعذر الموقع أثناء حضور اصطناعي لاختبار المراجعة" };
  const first = await page.request.post("/api/v1/staff/attendance-exceptions/", { headers: { ...mobileHeaders, "Idempotency-Key": requestKey }, data: body }); expect(first.status()).toBe(201); const r = await first.json(); expect(r.status).toBe("Pending"); expect(r.attendanceId).toBeNull();
  const replay = await page.request.post("/api/v1/staff/attendance-exceptions/", { headers: { ...mobileHeaders, "Idempotency-Key": requestKey }, data: body }); expect((await replay.json()).id).toBe(r.id);
  expect((await page.request.post(`/api/v1/manage/staff-attendance-exceptions/${r.id}/review`, { headers: mobileHeaders, data: { approve: true, note: null, version: r.version } })).status()).toBe(403);
  await page.goto(`/dashboard/attendance/staff/exceptions/${r.id}`); await expect(page.getByRole("heading", { name })).toBeVisible(); await expect(page.getByText("لا يوجد سجل حضور لهذا اليوم.")).toBeVisible();
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    const button = await page.getByRole("button", { name: "قبول وتسجيل إداري" }).boundingBox();
    const note = await page.getByLabel("ملاحظة القرار — مطلوبة عند الرفض").boundingBox();
    expect(button).not.toBeNull(); expect(note).not.toBeNull();
    expect(button!.height).toBeGreaterThanOrEqual(44); expect(button!.height).toBeLessThan(80);
    expect(button!.y).toBeGreaterThanOrEqual(note!.y + note!.height);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: info.outputPath(`staff-claim-pending-${width}.png`), fullPage: true, animations: "disabled" });
  }
  const save = page.waitForResponse(x => x.request().method() === "POST" && x.url().endsWith(`/staff-attendance-exceptions/${r.id}/review`)); await page.getByRole("button", { name: "قبول وتسجيل إداري" }).click(); const saved = await save; expect(saved.status(), await saved.text()).toBe(200);
  await expect(page.getByText("مقبول إداريًا", { exact: true })).toBeVisible(); await expect(page.getByText(/حاضر · حضور/)).toBeVisible();
  const own = await (await page.request.get("/api/v1/staff/attendance-exceptions/", { headers: mobileHeaders })).json(); const approved = own.find((x: { id: string }) => x.id === r.id); expect(approved.status).toBe("Approved"); expect(approved.trustSource).toBe("AdministrativeException"); expect(approved.attendanceId).toBeTruthy();
  const report = await (await page.request.get("/api/v1/reports/staff-daily?allDates=true")).json(); expect(report.items.find((x: { id: string }) => x.id === approved.attendanceId).status).toBe("Present");
  await page.reload(); await expect(page.getByText("مقبول إداريًا", { exact: true })).toBeVisible(); await expect(page.getByRole("button", { name: "قبول وتسجيل إداري" })).toHaveCount(0);
  await page.screenshot({ path: info.outputPath("staff-claim-approved-1440.png"), fullPage: true, animations: "disabled" });
  const out = await page.request.post("/api/v1/staff/attendance-exceptions/", { headers: { ...mobileHeaders, "Idempotency-Key": crypto.randomUUID() }, data: { ...body, operation: "CheckOut", claimedAtUtc: new Date(Date.parse(ownOptions.nowUtc) - 5 * 60_000).toISOString() } }); expect(out.status()).toBe(201); const rejected = await out.json();
  await page.goto(`/dashboard/attendance/staff/exceptions/${rejected.id}`); await page.getByLabel("ملاحظة القرار — مطلوبة عند الرفض").fill("لم يتم التحقق من الانصراف المدعى — حالة عرض"); await page.getByRole("button", { name: "رفض الطلب", exact: true }).click(); await expect(page.getByText("مرفوض", { exact: true })).toBeVisible();
  const final = await (await page.request.get(`/api/v1/manage/staff-attendance-exceptions/${rejected.id}`)).json(); expect(final.currentAttendance.checkOut).toBeNull();
  const after = await (await page.request.get("/api/v1/reports/owner-summary")).json(); expect(after.collections).toEqual(before.collections); expect(after.paymentRequests).toEqual(before.paymentRequests);
});
