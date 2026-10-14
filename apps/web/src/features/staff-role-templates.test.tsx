import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StaffRoleTemplates } from "./staff-role-templates";
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });
const actions = ["players", "attendance", "payroll"].map(module => ({ module, read: module !== "payroll", add: false, edit: false, delete: false }));
const preset = { key: "read-only", name: "متابعة التشغيل", description: "قراءة فقط", actions };
const row = { id: "role-a", isActive: true, appliedCopies: 1, template: { ...preset, key: "custom:role-a", name: "منسق الحضور", revision: 3, custom: true } };
const data = { modules: actions.map(a => a.module), presets: [preset], items: [row] };
function setup(fail = false) {
  const fetcher = vi.fn().mockImplementation(async (url: string, init?: RequestInit) => ({ ok: !(fail && init?.method === "PUT"), status: init?.method === "POST" ? 201 : fail && init?.method === "PUT" ? 409 : 200,
    json: async () => url.endsWith("/csrf") ? { token: "csrf" } : fail && init?.method === "PUT" ? { title: "تغير القالب؛ أعد تحميله" } : url.endsWith("/role-a") ? { ...row, applied: [{ id: "staff-a", name: "إداري فرع اصطناعي", revision: 2, customized: true, isActive: true }] } : data }));
  vi.stubGlobal("fetch", fetcher); render(<StaffRoleTemplates onClose={() => {}} />); return fetcher;
}
it("copies a preset and requires confirmation; creates a template without applying it to an account", async () => {
  const fetcher = setup(); fireEvent.click(await screen.findByRole("button", { name: "إنشاء قالب مخصص" }));
  fireEvent.change(screen.getByLabelText("اسم القالب"), { target: { value: "مساعد التدريب" } });
  fireEvent.change(screen.getByRole("combobox", { name: "نسخ صلاحيات من" }), { target: { value: "read-only" } });
  fireEvent.click(screen.getByText("التشغيل والتدريب", { selector: "summary" }));
  const add = screen.getByRole("switch", { name: "إضافة اللاعبون وأولياء الأمور" }); fireEvent.click(add);
  expect(screen.getByRole("switch", { name: "قراءة اللاعبون وأولياء الأمور" })).toBeChecked();
  fireEvent.click(screen.getByRole("switch", { name: "قراءة اللاعبون وأولياء الأمور" })); expect(add).not.toBeChecked();
  fireEvent.click(screen.getByRole("button", { name: "مراجعة وحفظ القالب" }));
  expect(fetcher.mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "تأكيد حفظ القالب" }));
  await waitFor(() => expect(screen.getByRole("button", { name: "إنشاء قالب مخصص" })).toBeVisible());
  const sent = fetcher.mock.calls.find(([, init]) => init?.method === "POST");
  expect(sent![0]).toBe("/api/v1/manage/staff/role-templates");
  expect(JSON.parse(sent![1].body)).toMatchObject({ name: "مساعد التدريب", revision: 0, isActive: true });
  expect(fetcher.mock.calls.some(([url]) => url.includes("/access"))).toBe(false);
});
it("shows saved source copies and archives only the template using the current revision", async () => {
  const fetcher = setup(); fireEvent.click(await screen.findByRole("button", { name: "عرض وتعديل" }));
  expect(await screen.findByText(/إداري فرع اصطناعي/)).toBeVisible();
  expect(screen.getByText(/لن تتغير صلاحيات الحسابات الحالية تلقائيًا/)).toBeVisible();
  fireEvent.click(screen.getByRole("checkbox", { name: "متاح للاختيار الجديد" }));
  fireEvent.click(screen.getByRole("button", { name: "مراجعة وحفظ القالب" }));
  expect(screen.getByRole("dialog")).toHaveTextContent("سيؤرشف من الاختيار الجديد");
  fireEvent.click(screen.getByRole("button", { name: "تأكيد حفظ القالب" }));
  await waitFor(() => expect(fetcher.mock.calls.some(([, init]) => init?.method === "PUT")).toBe(true));
  const sent = fetcher.mock.calls.find(([, init]) => init?.method === "PUT");
  expect(JSON.parse(sent![1].body)).toMatchObject({ isActive: false, revision: 3 });
  expect(fetcher.mock.calls.some(([url]) => url.includes("/access"))).toBe(false);
});
it("preserves the edit on stale revision failure and does not silently apply or reload grants", async () => {
  setup(true); fireEvent.click(await screen.findByRole("button", { name: "عرض وتعديل" }));
  fireEvent.change(await screen.findByLabelText("اسم القالب"), { target: { value: "منسق معدل" } });
  fireEvent.click(screen.getByRole("button", { name: "مراجعة وحفظ القالب" })); fireEvent.click(screen.getByRole("button", { name: "تأكيد حفظ القالب" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("تغير القالب");
  expect(screen.getByLabelText("اسم القالب")).toHaveValue("منسق معدل");
});
it("does not offer creation when the server rejects template access", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false, status: 403 })); render(<StaffRoleTemplates onClose={() => {}} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("متاحة لمالك الأكاديمية فقط");
  expect(screen.queryByRole("button", { name: "إنشاء قالب مخصص" })).not.toBeInTheDocument();
});
