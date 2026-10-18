import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { FeedbackAssignment, PrivateInvestigations } from "./feedback-investigations";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("assigns only an explicitly reviewed brief to an eligible member and preserves the retry key", async () => {
  const sent: Record<string, unknown>[] = []; const saved = vi.fn();
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith("/csrf")) return Response.json({ token: "qa" });
    if (init?.method === "POST") { sent.push(JSON.parse(String(init.body))); return sent.length === 1 ? Response.json({ message: "تعذر حفظ الإسناد الآن." }, { status: 503 }) : Response.json({ id: "f" }); }
    return Response.json({ items: [{ id: "admin", name: "إداري مخول اصطناعي" }] });
  }) as typeof fetch;
  render(<FeedbackAssignment id="f" version={9} saved={saved}/>);
  await screen.findByRole("option", { name: "إداري مخول اصطناعي" }); expect(screen.getByLabelText("موجز يراجعه المالك")).toHaveValue("");
  fireEvent.change(screen.getByLabelText("مسؤول التحقيق"), { target: { value: "admin" } }); fireEvent.change(screen.getByLabelText("موجز يراجعه المالك"), { target: { value: "راجع تنظيم وقت التدريب" } });
  fireEvent.click(screen.getByRole("button", { name: "حفظ تكليف التحقيق" })); await screen.findByText("تعذر حفظ الإسناد الآن."); expect(saved).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "حفظ تكليف التحقيق" })); await waitFor(() => expect(saved).toHaveBeenCalledOnce());
  expect(sent).toHaveLength(2); expect(sent[1]).toEqual(sent[0]); expect(sent[0]).toEqual({ investigatorMembershipId: "admin", brief: "راجع تنظيم وقت التدريب", version: 9, requestId: expect.any(String) });
});
it("sends findings only to its opaque assignment and removes content on live revocation", async () => {
  let revoked = false, saved = false; const bodies: object[] = [];
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith("/csrf")) return Response.json({ token: "qa" });
    if (init?.method === "POST") { expect(String(path)).toBe("/api/v1/my-followups/investigations/opaque/transition"); bodies.push(JSON.parse(String(init.body))); saved = true; return Response.json({ assignmentId: "opaque" }); }
    return revoked ? Response.json({}, { status: 404 }) : Response.json({ assignmentId: "opaque", version: saved ? 8 : 7, brief: "راجع وقت التدريب دون أسماء", status: "InProgress", dueOn: null, canEdit: true, findings: saved ? [{ id: "result", note: "نتيجة خاصة للمالك", createdAtUtc: "2026-10-19T00:00:00Z" }] : [] });
  }) as typeof fetch;
  render(<PrivateInvestigations id="opaque"/>); await screen.findByText("راجع وقت التدريب دون أسماء");
  expect(screen.queryByRole("link", { name: "ملف اللاعب نفسه" })).not.toBeInTheDocument(); expect(screen.queryByText("إرسال الرد وحل الملاحظة")).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("نتيجة التحقيق للمالك"), { target: { value: "نتيجة خاصة للمالك" } }); fireEvent.click(screen.getByRole("button", { name: "حفظ النتيجة الخاصة" }));
  expect(await screen.findByText("تم حفظ النتيجة الخاصة للمالك فقط.")).toBeVisible(); expect(await screen.findByText("نتيجة خاصة للمالك")).toBeVisible(); expect(bodies[0]).toMatchObject({ note: "نتيجة خاصة للمالك", version: 7, requestId: expect.any(String) });
  revoked = true; fireEvent.focus(window); await screen.findByRole("alert"); expect(screen.queryByText("راجع وقت التدريب دون أسماء")).not.toBeInTheDocument(); expect(screen.queryByLabelText("نتيجة التحقيق للمالك")).not.toBeInTheDocument();
});
it("read-only investigators cannot write and inbox links never use source IDs", async () => {
  global.fetch = vi.fn(async () => Response.json({ items: [{ assignmentId: "opaque", brief: "موجز مستقل", status: "Open", dueOn: null }] })) as typeof fetch;
  const view = render(<PrivateInvestigations/>); expect(await screen.findByRole("link", { name: "فتح تكليف التحقيق" })).toHaveAttribute("href", "/dashboard/my-followups/investigations/opaque"); view.unmount();
  global.fetch = vi.fn(async () => Response.json({ assignmentId: "opaque", version: 2, brief: "موجز مستقل", status: "Open", dueOn: null, canEdit: false, findings: [] })) as typeof fetch;
  render(<PrivateInvestigations id="opaque"/>); await screen.findByText("للقراءة فقط وفق صلاحياتك الحالية."); expect(screen.queryByRole("button", { name: "حفظ النتيجة الخاصة" })).not.toBeInTheDocument();
});
