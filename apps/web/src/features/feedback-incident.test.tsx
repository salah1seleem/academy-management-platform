import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GuardianFeedbackForm } from "./guardian-feedback-form";
import { FeedbackIncidentEvidence } from "./feedback-incident";
vi.mock("next/navigation", () => ({ usePathname: () => "/guardian", useRouter: () => ({ push: vi.fn() }) }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("uses a dated occurrence from the child's former group, never its current coach, and preserves an optional unknown selection", async () => {
  const posts: object[] = [];
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith("/csrf")) return Response.json({ token: "qa" });
    if (init?.method === "POST") { posts.push(JSON.parse(String(init.body))); return Response.json({ id: "f" }); }
    if (String(path).includes("feedback-incidents?")) return Response.json({ sessions: [{ id: "s", groupId: "old", groupName: "مجموعة سابقة اصطناعية", date: "2026-09-28", startTime: "16:00:00", endTime: "17:00:00", coaches: [{ id: "replacement", name: "مدرب الواقعة الاصطناعي", source: "OccurrenceDecision" }] }], notice: "لا نخمن المدرب الحالي", hasMore: false });
    return Response.json({ asOfDate: "2026-10-19", groups: [{ id: "current", name: "مجموعة حالية", coaches: [{ id: "now", name: "مدرب حالي ليس دليل الواقعة" }] }], mine: [] });
  }) as typeof fetch;
  render(<GuardianFeedbackForm playerId="child"/>); await screen.findByRole("option", { name: "مجموعة حالية" });
  fireEvent.change(screen.getByLabelText("تاريخ الواقعة (اختياري)"), { target: { value: "2026-09-28" } }); await screen.findByLabelText("الحصة المرتبطة (اختياري)");
  fireEvent.change(screen.getByLabelText("الحصة المرتبطة (اختياري)"), { target: { value: "s" } }); expect(screen.getByLabelText("المجموعة")).toHaveValue("old"); expect(screen.getByLabelText("المجموعة")).toBeDisabled();
  expect(screen.queryByRole("option", { name: "مدرب حالي ليس دليل الواقعة" })).toBeNull(); expect(screen.getByRole("option", { name: "مدرب الواقعة الاصطناعي" })).toBeVisible(); expect(screen.getByLabelText("المدرب المعني (اختياري)")).toHaveValue("");
  fireEvent.change(screen.getByLabelText("اكتب الملاحظة"), { target: { value: "ملاحظة اصطناعية دون اتهام أو اختيار مدرب" } }); fireEvent.click(screen.getByRole("button", { name: "إرسال للإدارة" })); await waitFor(() => expect(posts).toHaveLength(1)); expect(posts[0]).toMatchObject({ trainingGroupId: "old", incidentSessionId: "s", incidentOn: "2026-09-28", coachMembershipId: null });
});
it("owner sees the saved occurrence source and its limits, not an invented current assignment", () => {
  render(<FeedbackIncidentEvidence evidence={JSON.stringify({ groupName: "مجموعة سابقة", date: "2026-09-28", startTime: "16:00:00", endTime: "17:00:00", selectedCoachId: "coach", coaches: [{ id: "coach", name: "اسم وقت التقديم", source: "OccurrenceDecision" }] })}/>);
  expect(screen.getByRole("region", { name: "سياق الحصة وقت التقديم" })).toHaveTextContent("اسم وقت التقديم · قرار مدرب للحصة · اختاره مقدم الملاحظة"); expect(screen.getByText(/ليست إثبات حضور فعلي/)).toBeVisible();
});
