import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SubscriptionSessionGrant, type SessionGrantPeriod } from "./subscription-session-grant";
import { GuardianSubscriptionStatus, SubscriptionPeriodDetail } from "./subscriptions";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

const period: SessionGrantPeriod = { id: "period", planType: "Combined", status: "Active", startDate: "2026-10-01", endDate: "2026-10-30", initialSessions: 10, remainingSessions: 3, version: 12, canGrantSessions: true };
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
async function review() {
  fireEvent.click(screen.getByRole("button", { name: "إضافة حصص وتحديد نهاية الاشتراك" }));
  fireEvent.change(screen.getByLabelText("عدد الحصص الإضافية"), { target: { value: "4" } });
  fireEvent.change(screen.getByLabelText("نهاية الاشتراك الجديدة"), { target: { value: "2026-11-05" } });
  fireEvent.change(screen.getByLabelText("سبب الإضافة"), { target: { value: "قبول اصطناعي" } });
  fireEvent.click(screen.getByRole("button", { name: "مراجعة الإضافة" }));
  expect(screen.getByRole("dialog")).toHaveTextContent("نهاية الفترة كلها");
  fireEvent.click(screen.getByRole("button", { name: "تأكيد إضافة الحصص" }));
}
describe("whole-period session grant", () => {
  for (const fault of ["none", "lost-read", "wrong-audit", "unknown-post"]) {
    it(`verifies persistence and keeps one frozen intent: ${fault}`, async () => {
      let posts = 0, reads = 0;
      const bodies: string[] = [], keys: string[] = [];
      const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
        if (url.endsWith("/csrf")) return response({ token: "test" });
        if (init?.method === "POST") {
          posts++; bodies.push(String(init.body)); keys.push((init.headers as Record<string, string>)["Idempotency-Key"]);
          if (fault === "unknown-post" && posts === 1) throw new TypeError("اتصال منقطع");
          return response({ adjustmentId: "audit" });
        }
        reads++;
        if (fault === "lost-read" && reads === 1) return response({}, 503);
        return response({ ...period, endDate: "2026-11-05", remainingSessions: 7, adjustments: [{ id: "audit", type: "SessionsAdded", sessionsDelta: fault === "wrong-audit" && reads === 1 ? 99 : 4, oldRemainingSessions: 3, newRemainingSessions: 7, newEndDate: "2026-11-05" }] });
      });
      global.fetch = fetchMock as typeof fetch;
      const saved = vi.fn(), announced = vi.fn();
      window.addEventListener("academy-save-success", announced);
      render(<SubscriptionSessionGrant period={period} onSaved={saved} />);
      await review();
      if (fault !== "none") {
        await screen.findByRole("alert");
        expect(saved).not.toHaveBeenCalled(); expect(announced).not.toHaveBeenCalled();
        expect(screen.getByLabelText("عدد الحصص الإضافية")).toBeDisabled();
        fireEvent.click(screen.getByRole("button", { name: "إعادة التحقق من الإضافة نفسها" }));
      }
      await waitFor(() => expect(saved).toHaveBeenCalledOnce());
      expect(announced).toHaveBeenCalledOnce();
      expect(posts).toBe(fault === "unknown-post" ? 2 : 1);
      expect(new Set(keys).size).toBe(1); expect(new Set(bodies).size).toBe(1);
      expect(JSON.parse(bodies[0])).toEqual({ sessions: 4, endDate: "2026-11-05", reason: "قبول اصطناعي", expectedVersion: 12 });
      window.removeEventListener("academy-save-success", announced);
    });
  }
  it("requires server permission and an actual session balance", () => {
    const { rerender } = render(<SubscriptionSessionGrant period={{ ...period, canGrantSessions: false }} onSaved={vi.fn()} />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    rerender(<SubscriptionSessionGrant period={{ ...period, planType: "Duration", remainingSessions: null }} onSaved={vi.fn()} />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    rerender(<SubscriptionSessionGrant period={{ ...period, status: "Cancelled" }} onSaved={vi.fn()} />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
  it("a definite forbidden response permits correction and never announces success", async () => {
    global.fetch = vi.fn(async (url: string) => url.endsWith("/csrf") ? response({ token: "test" }) : response({ message: "لا تملك الصلاحية" }, 403)) as typeof fetch;
    const saved = vi.fn(); render(<SubscriptionSessionGrant period={period} onSaved={saved} />);
    await review(); expect(await screen.findByRole("alert")).toHaveTextContent("لا تملك الصلاحية");
    expect(screen.getByLabelText("عدد الحصص الإضافية")).toBeEnabled(); expect(saved).not.toHaveBeenCalled();
  });
  it("connects the server-authorized grant in the existing period screen without restoring Add Days", async () => {
    global.fetch = vi.fn().mockImplementation(async () => response({ ...period, player: "طفل اصطناعي", playerCode: "TEST", sport: "كرة القدم", plan: "باقة اختبار", adjustments: [] }));
    render(<SubscriptionPeriodDetail id="period" />);
    expect(await screen.findByRole("button", { name: "إضافة حصص وتحديد نهاية الاشتراك" })).toBeVisible();
    expect(screen.queryByRole("button", { name: "إضافة أيام" })).not.toBeInTheDocument();
  });
  it("shows the linked guardian the added balance and whole-period end without financial fields", async () => {
    global.fetch = vi.fn().mockImplementation(async () => response({ ...period, plan: "باقة اختبار", remainingSessions: 7, endDate: "2026-11-05", adjustments: [{ type: "SessionsAdded", effectiveDate: "2026-10-21", sessionsDelta: 4, oldRemainingSessions: 3, newRemainingSessions: 7, newEndDate: "2026-11-05" }] }));
    render(<GuardianSubscriptionStatus id="period" />);
    expect(await screen.findByText(/أضيفت 4 حصة إلى الرصيد/)).toHaveTextContent("نهاية الاشتراك كله");
    expect(screen.queryByText(/السعر|التحصيل|المبلغ/)).not.toBeInTheDocument();
  });
  it("clears the previous period's grant before a different period finishes loading", async () => {
    let finish: ((value: Response) => void) | undefined;
    global.fetch = vi.fn(async (url) => String(url).endsWith("/second") ? new Promise<Response>(resolve => { finish = resolve; }) : response({ ...period, id: "first", player: "طفل أول", playerCode: "TEST", adjustments: [] }));
    const { rerender } = render(<SubscriptionPeriodDetail id="first" />);
    await screen.findByRole("button", { name: "إضافة حصص وتحديد نهاية الاشتراك" });
    rerender(<SubscriptionPeriodDetail id="second" />);
    expect(screen.queryByRole("button", { name: "إضافة حصص وتحديد نهاية الاشتراك" })).not.toBeInTheDocument();
    finish!(response({ ...period, id: "second", player: "طفل ثان", playerCode: "TEST", canGrantSessions: false, adjustments: [] }));
    expect(await screen.findByText("طفل ثان · TEST")).toBeVisible();
    expect(screen.queryByRole("button", { name: "إضافة حصص وتحديد نهاية الاشتراك" })).not.toBeInTheDocument();
  });
  it("clears the previous child's period history when the guardian selects a different period", async () => {
    let finish: ((value: Response) => void) | undefined;
    global.fetch = vi.fn(async (url) => String(url).includes("/second/") ? new Promise<Response>(resolve => { finish = resolve; }) : response({ ...period, id: "first", plan: "باقة أولى", adjustments: [] }));
    const { rerender } = render(<GuardianSubscriptionStatus id="first" />);
    await screen.findByText("باقة أولى");
    rerender(<GuardianSubscriptionStatus id="second" />);
    expect(screen.queryByText("باقة أولى")).not.toBeInTheDocument();
    finish!(response({ ...period, id: "second", plan: "باقة ثانية", adjustments: [] }));
    expect(await screen.findByText("باقة ثانية")).toBeVisible();
  });
});
