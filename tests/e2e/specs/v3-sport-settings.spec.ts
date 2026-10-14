import { expect, test } from "@playwright/test";

test("Owner configures swimming per branch, retains saved history and disables new bookings", async ({ page }, info) => {
  test.setTimeout(60_000);
  await page.goto("/login"); await page.getByLabel("البريد الإلكتروني").fill("owner.nogoom@example.test"); await page.getByLabel("كلمة المرور").fill("Demo-Only-123!");
  await page.getByRole("button", { name: "دخول آمن" }).click(); await page.waitForURL("**/dashboard");
  const headers = { "X-CSRF-TOKEN": (await (await page.request.get("/api/v1/auth/csrf")).json()).token };
  const created = await page.request.post("/api/v1/manage/sports", { headers, data: { arabicName: `رياضة اختبار إعداد ${Date.now()}` } });
  expect(created.status()).toBe(201); const sport = await created.json();
  await page.goto("/dashboard/settings/sports"); await page.getByRole("combobox", { name: "الرياضة", exact: true }).selectOption(sport);
  await expect(page.getByRole("region", { name: "إعدادات السباحة" })).not.toBeVisible();
  await page.getByRole("combobox", { name: "نوع الرياضة", exact: true }).selectOption("Swimming"); await page.getByRole("button", { name: "حفظ نوع الرياضة" }).click();
  const section = page.getByRole("region", { name: "إعدادات السباحة" }); await expect(section).toBeVisible();
  const form = section.locator("form").filter({ has: page.getByRole("heading", { name: "فرع مدينة نصر", exact: true }) });
  await form.getByLabel("تشغيل مرافق السباحة في هذا الفرع").check(); await form.getByLabel("سباحة حرة", { exact: true }).check();
  await form.getByRole("button", { name: "حفظ إعدادات الفرع" }).click(); await expect(page.getByRole("status").filter({ hasText: "تم حفظ إعدادات فرع مدينة نصر" })).toContainText("تم حفظ إعدادات فرع مدينة نصر");
  await page.reload(); await page.getByRole("combobox", { name: "الرياضة", exact: true }).selectOption(sport);
  await expect(form.getByLabel("تشغيل مرافق السباحة في هذا الفرع")).toBeChecked();
  await expect(section.locator("form").filter({ has: page.getByRole("heading", { name: "فرع التجمع الخامس", exact: true }) }).getByLabel("تشغيل مرافق السباحة في هذا الفرع")).not.toBeChecked();
  for (const width of [360, 390, 430, 1440]) { await page.setViewportSize({ width, height: 900 }); await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true); await page.screenshot({ path: info.outputPath(`sport-settings-${width}.png`), animations: "disabled" }); }
  const settings = await (await page.request.get("/api/v1/manage/sport-settings")).json(); const branch = settings.branches.find((x: { name: string }) => x.name === "فرع مدينة نصر").id;
  const f = await page.request.post("/api/v1/facilities", { headers, data: { name: `مرفق إعداد V3 ${Date.now()}`, branchId: branch } }); expect(f.status()).toBe(200);
  const s = await page.request.post("/api/v1/facilities/spaces", { headers, data: { name: "مسبح اصطناعي", facilityId: (await f.json()).id, sportId: sport, kind: "Pool" } }); expect(s.status()).toBe(200);
  const r = await page.request.post("/api/v1/facilities/resources", { headers, data: { name: "A", spaceId: (await s.json()).id, capacity: 8, shared: false, days: [0, 1, 2, 3, 4, 5, 6], opensAt: "08:00", closesAt: "22:00", serviceKinds: ["OpenSwim"] } }); expect(r.status()).toBe(200); const resource = (await r.json()).id;
  expect((await page.request.post(`/api/v1/facilities/resources/${resource}/approve`, { headers, data: { qualifiedResponsibilityConfirmed: true, evidence: "اعتماد اصطناعي للاختبار فقط" } })).status()).toBe(200);
  const board = await (await page.request.get("/api/v1/facilities/board")).json(); const day = new Date(`${board.actualToday}T12:00:00Z`); day.setUTCDate(day.getUTCDate() + 10); const date = day.toISOString().slice(0, 10);
  const booking = { resourceId: resource, date, start: "17:00", end: "18:00", seats: 1, exclusive: true, serviceKind: "OpenSwim" };
  const booked = await page.request.post("/api/v1/facilities/bookings", { headers: { ...headers, "Idempotency-Key": crypto.randomUUID() }, data: booking }); expect(booked.status()).toBe(200); const id = (await booked.json()).id;
  await form.getByLabel("تشغيل مرافق السباحة في هذا الفرع").uncheck(); await form.getByRole("button", { name: "حفظ إعدادات الفرع" }).click(); await expect(page.getByRole("status").filter({ hasText: "تم حفظ إعدادات فرع مدينة نصر" })).toContainText("تم حفظ إعدادات فرع مدينة نصر");
  expect((await page.request.post("/api/v1/facilities/bookings", { headers: { ...headers, "Idempotency-Key": crypto.randomUUID() }, data: { ...booking, start: "18:00", end: "19:00" } })).status()).toBe(403);
  const history = await (await page.request.get(`/api/v1/facilities/board?date=${date}&branchId=${branch}`)).json(); expect(history.bookings.some((x: { id: string }) => x.id === id)).toBe(true);
  await form.getByRole("link", { name: "المرافق والمواعيد" }).click(); await expect(page.getByRole("combobox", { name: "الفرع", exact: true })).toHaveValue(branch);
});
