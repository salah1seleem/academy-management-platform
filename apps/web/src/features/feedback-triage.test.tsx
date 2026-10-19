import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { FeedbackTriage } from "./feedback-triage";
import { GuardianFeedbackForm } from "./guardian-feedback-form";
import { GuardianFeedbackInbox } from "./guardian-feedback";
vi.mock("next/navigation", () => ({ usePathname: () => "/guardian", useRouter: () => ({ push: vi.fn() }) }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("owner confirms triage with an optional private note and a stable retry intent, never a public resolution", async () => {
  const sent: object[] = [], saved = vi.fn(); global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith("/csrf")) return Response.json({ token: "qa" });
    sent.push(JSON.parse(String(init?.body))); return sent.length === 1 ? Response.json({ message: "تعذر حفظ الفرز الآن." }, { status: 503 }) : Response.json({ id: "f" });
  }) as typeof fetch;
  render(<FeedbackTriage id="f" version={7} subject="Water" urgency="Safety" safety saved={saved}/>);
  expect(screen.getByText(/تبقى في قائمة السلامة/)).toBeVisible(); fireEvent.change(screen.getByLabelText("درجة الاستعجال المراجعة"), { target: { value: "Routine" } });
  fireEvent.click(screen.getByRole("button", { name: "تأكيد الفرز البشري" })); await screen.findByText("تعذر حفظ الفرز الآن."); expect(saved).not.toHaveBeenCalled(); fireEvent.click(screen.getByRole("button", { name: "تأكيد الفرز البشري" })); await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(sent[0]).toEqual(sent[1]); expect(sent[0]).toMatchObject({ subject: "Water", urgency: "Routine", version: 7, note: null, requestId: expect.any(String) });
});
it("guardian submits a safety report with incident date and no accusation, preserving the key on retry", async () => {
  const posts: { body: object; key: string | undefined }[] = [];
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).includes("feedback-incidents?")) return Response.json({ sessions: [], notice: "لا نخمن المدرب الحالي", hasMore: false });
    if (String(path).endsWith("/csrf")) return Response.json({ token: "qa" });
    if (init?.method === "POST") { expect(String(path)).toBe("/api/v1/guardian/children/child/feedback"); posts.push({ body: JSON.parse(String(init.body)), key: (init.headers as Record<string, string>)["Idempotency-Key"] }); return posts.length === 1 ? Response.json({ message: "تعذر الإرسال الآن." }, { status: 503 }) : Response.json({ id: "f" }); }
    return Response.json({ asOfDate: "2026-10-19", groups: [{ id: "g", name: "مجموعة اصطناعية", coaches: [] }], mine: [] });
  }) as typeof fetch;
  render(<GuardianFeedbackForm playerId="child"/>); await screen.findByRole("option", { name: "مجموعة اصطناعية" });
  fireEvent.change(screen.getByLabelText("المجموعة"), { target: { value: "g" } }); fireEvent.change(screen.getByLabelText("موضوع الملاحظة"), { target: { value: "Water" } }); fireEvent.change(screen.getByLabelText("درجة الاستعجال"), { target: { value: "Safety" } });
  expect(screen.getByRole("note")).toHaveTextContent("ليست طوارئ"); expect(screen.getByLabelText("تاريخ الواقعة (اختياري)")).toHaveAttribute("max", "2026-10-19"); fireEvent.change(screen.getByLabelText("تاريخ الواقعة (اختياري)"), { target: { value: "2026-10-18" } }); fireEvent.change(screen.getByLabelText("اكتب الملاحظة"), { target: { value: "بلاغ اصطناعي للمراجعة البشرية فقط" } });
  fireEvent.click(screen.getByRole("button", { name: "إرسال للإدارة" })); await screen.findByText("تعذر الإرسال الآن."); fireEvent.click(screen.getByRole("button", { name: "إرسال للإدارة" })); await screen.findByText(/وصل بلاغ السلامة إلى قائمة المراجعة/);
  expect(posts[0]).toEqual(posts[1]); expect(posts[0].key).toBeTruthy(); expect(posts[0].body).toMatchObject({ coachMembershipId: null, subject: "Water", urgency: "Safety", incidentOn: "2026-10-18" });
});
it("safety inbox clear removes the safety constraint rather than leaving a hidden filter", async () => {
  const requests: string[] = []; global.fetch = vi.fn(async path => { requests.push(String(path)); return Response.json(String(path).includes("structure/options") ? { branches: [] } : []); }) as typeof fetch;
  render(<GuardianFeedbackInbox initialSafetyOnly initialStatus="Unresolved"/>); await waitFor(() => expect(requests.some(p => p.includes("safetyOnly=true"))).toBe(true)); fireEvent.click(screen.getByRole("button", { name: "مسح" })); await waitFor(() => expect(requests.at(-1)).toBe("/api/v1/manage/feedback?")); expect(screen.getByLabelText("مسار السلامة")).toHaveValue("");
});
