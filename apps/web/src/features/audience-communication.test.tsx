import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AudienceCommunication } from "./audience-communication";
const write = vi.hoisted(() => vi.fn());
vi.mock("./dashboard-api", () => ({ csrfRequest: write }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); write.mockReset(); });
const data = { name: "مجموعة خيالية", playerCount: 2, recipientCount: 1, canSend: true, canManageLink: true, version: 0, groupName: "أكاديمية · مجموعة", invitationText: "دعوة اختيارية" };
describe("group communication", () => {
 it("previews server counts then sends the same request without client recipient IDs", async () => {
   vi.stubGlobal("fetch", vi.fn(async () => Response.json(data)));
   write.mockImplementation(async (path: string) => Response.json(path.endsWith("preview") ? { previewHash: "quote", name: data.name, message: "تذكير عام", playerCount: 2, recipientCount: 1 } : { sent: 1 }));
   render(<AudienceCommunication id="group-1" />);
   await screen.findByText("2 لاعب · 1 حساب ولي أمر مميز");
   fireEvent.change(screen.getByLabelText("رسالة عامة لأولياء الأمور"), { target: { value: "تذكير عام" } });
   fireEvent.click(screen.getByText("معاينة الجمهور والرسالة"));
   await screen.findByText("1 مستلم مميز · 2 لاعب");
   fireEvent.click(screen.getByText("تأكيد الإرسال"));
   await screen.findByRole("status");
   expect(write).toHaveBeenCalledTimes(2);
   const preview = write.mock.calls[0][2], sent = write.mock.calls[1][2];
   expect(sent.requestId).toBe(preview.requestId); expect(sent.previewHash).toBe("quote"); expect(sent.recipients).toBeUndefined();
   expect((screen.getByText("معاينة الجمهور والرسالة") as HTMLButtonElement).disabled).toBe(true);
 });
 it("invalidates confirmation when the message changes and shows manual WhatsApp guidance", async () => {
   vi.stubGlobal("fetch", vi.fn(async () => Response.json(data)));
   write.mockResolvedValue(Response.json({ previewHash: "q", name: data.name, playerCount: 2, recipientCount: 1, message: "أ" }));
   render(<AudienceCommunication id="group-2" />);
   const input = await screen.findByLabelText("رسالة عامة لأولياء الأمور");
   fireEvent.change(input, { target: { value: "أ" } }); fireEvent.click(screen.getByText("معاينة الجمهور والرسالة")); await screen.findByText("تأكيد الإرسال");
   fireEvent.change(input, { target: { value: "ب" } }); expect(screen.queryByText("تأكيد الإرسال")).toBeNull();
   expect(screen.getByText(/أرقام الأعضاء قد تظهر/)).toBeTruthy();
 });
 it("hides sending when the server withholds permission", async () => {
   vi.stubGlobal("fetch", vi.fn(async () => Response.json({ ...data, canSend: false, canManageLink: false })));
   render(<AudienceCommunication id="group-3" />);
   await screen.findByText("إرسال الإشعارات يحتاج تفويضًا من إدارة الأكاديمية.");
   expect(screen.queryByText("معاينة الجمهور والرسالة")).toBeNull(); expect(screen.queryByText("حفظ رابط الدعوة")).toBeNull();
   await waitFor(() => expect(write).not.toHaveBeenCalled());
 });
});
