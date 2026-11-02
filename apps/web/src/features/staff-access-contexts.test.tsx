import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StaffAccessContexts } from "./staff-access-contexts";
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const item = { id: "context", name: "منسق فرع ب", isActive: true, revision: 3, allBranches: false, allSports: false, branches: ["b"], sports: ["swimming"] };
it("creates a stopped empty context and opens the existing permission editor instead of duplicating the user", async () => {
  const configure = vi.fn(); const writes: unknown[] = [];
  vi.stubGlobal("fetch", vi.fn(async (url, init) => String(url).endsWith("/csrf") ? Response.json({ token: "test" }) : init?.method === "POST" ? (writes.push(JSON.parse(init.body)), Response.json({ id: "new-context" }, { status: 201 })) : Response.json({ items: [] })));
  render(<StaffAccessContexts id="primary" name="إداري اصطناعي" onConfigure={configure} onClose={vi.fn()} />);
  fireEvent.change(screen.getByLabelText("اسم السياق الجديد"), { target: { value: "مشرف تدريب ب" } }); fireEvent.click(screen.getByRole("button", { name: "إنشاء وتحديد الصلاحيات" }));
  await waitFor(() => expect(configure).toHaveBeenCalledWith("new-context", "إداري اصطناعي · مشرف تدريب ب")); expect(writes).toEqual([{ name: "مشرف تدريب ب" }]);
});
it("requires reviewed confirmation with exact revision before stopping a context", async () => {
  const writes: unknown[] = []; vi.stubGlobal("fetch", vi.fn(async (url, init) => String(url).endsWith("/csrf") ? Response.json({ token: "test" }) : init?.method === "PUT" ? (writes.push(JSON.parse(init.body)), new Response(null, { status: 204 })) : Response.json({ items: [item] })));
  render(<StaffAccessContexts id="primary" name="إداري اصطناعي" onConfigure={vi.fn()} onClose={vi.fn()} />); fireEvent.click(await screen.findByRole("button", { name: "الاسم والتفعيل" })); fireEvent.click(screen.getByLabelText("سياق فعال")); fireEvent.click(screen.getByRole("button", { name: "مراجعة الحفظ" }));
  expect(writes).toHaveLength(0); expect(screen.getByRole("dialog")).toHaveTextContent("إيقاف وصول الجلسات"); fireEvent.click(screen.getByRole("button", { name: "تأكيد حالة السياق" }));
  await waitFor(() => expect(writes).toEqual([{ name: item.name, isActive: false, revision: 3 }]));
});
it("preserves edits when a newer scope revision prevents saving", async () => {
  vi.stubGlobal("fetch", vi.fn(async (url, init) => String(url).endsWith("/csrf") ? Response.json({ token: "test" }) : init?.method === "PUT" ? Response.json({ title: "تغير السياق؛ أعد تحميله" }, { status: 409 }) : Response.json({ items: [item] })));
  render(<StaffAccessContexts id="primary" name="إداري اصطناعي" onConfigure={vi.fn()} onClose={vi.fn()} />); fireEvent.click(await screen.findByRole("button", { name: "الاسم والتفعيل" })); fireEvent.change(screen.getByLabelText("اسم السياق"), { target: { value: "اسم مراجع" } }); fireEvent.click(screen.getByRole("button", { name: "مراجعة الحفظ" })); fireEvent.click(screen.getByRole("button", { name: "تأكيد حالة السياق" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("تغير السياق"); expect(screen.getByLabelText("اسم السياق")).toHaveValue("اسم مراجع");
});
