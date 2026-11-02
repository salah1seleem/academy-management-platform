import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlayerCardLookup, PlayerCardPanel } from "./player-card";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const code = "academity:player:v1:" + "A".repeat(64);
for (const [add, edit] of [[false,false], [true,false], [false,true], [true,true]]) it(`uses independent issue (${add}) and revoke (${edit}) grants`, async () => {
  global.fetch = vi.fn(async url => ({ ok:true, json:async () => String(url).endsWith("/my-permissions") ? ["attendance.manage"] : String(url).endsWith("/my-access") ? { useActionGrants:true, actions:[{ module:"attendance", read:true, add, edit }] } : { active:true, version:1, pngBase64:"YWJj", message:"بطاقة" } })) as unknown as typeof fetch;
  render(<PlayerCardPanel playerId="p" />); await screen.findByAltText("QR بطاقة اللاعب — معرّف مبهم"); await screen.findByRole("button", { name:"طباعة البطاقة" });
  await waitFor(() => { expect(fetch).toHaveBeenCalledTimes(3); expect(!!screen.queryByRole("button", { name:"إصدار بديل وإلغاء القديمة" })).toBe(add); expect(!!screen.queryByRole("button", { name:"إلغاء البطاقة" })).toBe(edit); });
});
it("shows an opaque card for the linked guardian without issuing or revoking controls", async () => {
  global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ active: true, version: 13, pngBase64: "YWJj", code, expiresAtUtc: "2027-10-12T00:00:00Z", message: "بطاقة" }) });
  render(<PlayerCardPanel playerId="p" guardian name="لاعب تجريبي" />); await screen.findByAltText("QR بطاقة اللاعب — معرّف مبهم"); expect(screen.queryByText("إلغاء البطاقة")).not.toBeInTheDocument(); expect(screen.queryByText(code)).not.toBeInTheDocument(); expect(vi.mocked(fetch).mock.calls[0][0]).toBe("/api/v1/guardian/children/p/card");
});
it("keeps manual lookup available when there is no active card", async () => {
  global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ active: false, version: 0, message: "لا توجد بطاقة فعالة" }) }); render(<PlayerCardPanel playerId="p" guardian />); await screen.findByText("لا توجد بطاقة فعالة"); expect(screen.getByText(/بديل يدوي/)).toBeVisible(); expect(screen.queryByRole("button", { name: "إصدار بطاقة" })).not.toBeInTheDocument();
});
it("resolves only a selected session, clears the code and never announces saved attendance", async () => {
  const onResolved = vi.fn(), announced = vi.fn(); window.addEventListener("academy-save-success", announced);
  global.fetch = vi.fn(async url => ({ ok: true, json: async () => String(url).endsWith("/csrf") ? { token: "test" } : { player: { arabicName: "لاعب اصطناعي", playerCode: "DEMO-1" }, message: "تحقق بصريًا ثم احفظ الحضور." } })) as unknown as typeof fetch;
  render(<PlayerCardLookup sessionId="session-only" onResolved={onResolved} />); fireEvent.click(screen.getByText("البحث ببطاقة اللاعب")); fireEvent.change(screen.getByLabelText("محتوى QR"), { target: { value: code } }); fireEvent.click(screen.getByRole("button", { name: "تحقق من البطاقة" })); await screen.findByText(/تحقق بصريًا ثم احفظ/); expect(onResolved).toHaveBeenCalledWith("DEMO-1"); expect(screen.getByLabelText("محتوى QR")).toHaveValue(""); expect(announced).not.toHaveBeenCalled(); expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url) === "/api/v1/attendance/sessions/session-only/player-card/resolve")).toBe(true); window.removeEventListener("academy-save-success", announced);
});
it("does not select a player or show success for a rejected code", async () => {
  const onResolved = vi.fn(); global.fetch = vi.fn(async url => ({ ok: String(url).endsWith("/csrf"), json: async () => String(url).endsWith("/csrf") ? { token: "test" } : { title: "البطاقة غير متاحة لهذه الحصة." } })) as unknown as typeof fetch;
  render(<PlayerCardLookup sessionId="s" onResolved={onResolved} />); fireEvent.click(screen.getByText("البحث ببطاقة اللاعب")); fireEvent.change(screen.getByLabelText("محتوى QR"), { target: { value: code } }); fireEvent.click(screen.getByRole("button", { name: "تحقق من البطاقة" })); await screen.findByText("البطاقة غير متاحة لهذه الحصة."); expect(onResolved).not.toHaveBeenCalled();
});
