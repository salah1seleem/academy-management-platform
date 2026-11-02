import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GuardianDayPass } from "./day-pass";

afterEach(() => { cleanup(); vi.restoreAllMocks(); sessionStorage.clear(); });
const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });
it("shows a linked-child day pass and sends one stable order key on repeated click", async () => {
  const keys: string[] = [];
  global.fetch = vi.fn(async (url, init) => {
    const path = String(url);
    if (path === "/api/v1/help/availability") return json({ available: false });
    if (path.endsWith("/csrf")) return json({ token: "csrf" });
    if (path.endsWith("/options")) return json({ children: [{ id: "child", name: "طفل اصطناعي" }], bookings: [{ bookingId: "booking", date: "2026-10-20", startTime: "17:00:00", endTime: "18:00:00", resource: "حارة A", reservedSeats: 2, plans: [{ id: "plan", name: "زيارة يومية", amount: "125.00", currency: "EGP" }] }] });
    if (path.endsWith("/orders") && init?.method !== "POST") return json([]);
    if (path.endsWith("/quote")) return json({ player: "طفل اصطناعي", bookingDate: "2026-10-20", startTime: "17:00:00", endTime: "18:00:00", resource: "حارة A", plan: "زيارة يومية", amount: "125.00", currency: "EGP", quoteHash: "a".repeat(64), transfer: { instaPayAddress: "demo@example.test" } });
    if (!path.endsWith("/orders") || init?.method !== "POST") throw new Error(`Unexpected fixture request: ${init?.method ?? "GET"} ${path}`);
    keys.push(new Headers(init.headers).get("Idempotency-Key")!); return json({ id: "ticket", playerId: "child", resourceBookingId: "booking", status: "PendingProof", planName: "زيارة يومية", amount: "125.00", currency: "EGP" });
  }) as typeof fetch;
  render(<GuardianDayPass />);
  fireEvent.change(await screen.findByLabelText("الطفل"), { target: { value: "child" } });
  fireEvent.change(screen.getByLabelText("الموعد"), { target: { value: "booking" } });
  fireEvent.change(screen.getByLabelText("باقة الزيارة"), { target: { value: "plan" } });
  fireEvent.click(screen.getByRole("button", { name: "مراجعة السعر والموعد" }));
  await screen.findByText("demo@example.test");
  fireEvent.click(screen.getByRole("button", { name: "إنشاء طلب الزيارة" }));
  await waitFor(() => expect(keys).toHaveLength(1));
  fireEvent.click(screen.getByRole("button", { name: "إنشاء طلب الزيارة" }));
  await waitFor(() => expect(keys).toHaveLength(2));
  expect(keys[0]).toBe(keys[1]);
  expect(screen.getByText(/لا توجد تذكرة دخول قبل موافقة الإدارة/)).toBeVisible();
});
