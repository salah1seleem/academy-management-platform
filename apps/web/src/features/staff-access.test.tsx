import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StaffAccessEditor } from "./staff-access";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const value = { allBranches: true, useActionGrants: false, modules: ["players", "attendance"], branches: [{ id: "branch-a", name: "فرع الاختبار" }], selectedBranches: [], actions: [], history: [] };
function setup() {
  const fetcher = vi.fn().mockImplementation(async (url: string) => ({ ok: true, status: 200, json: async () => url.endsWith("/csrf") ? { token: "csrf" } : value }));
  global.fetch = fetcher; const close = vi.fn(); render(<StaffAccessEditor id="admin" name="إداري تجريبي" onClose={close} />); return { fetcher, close };
}
it("requires read for write and clears dependent grants when read is revoked", async () => {
  setup(); const add = await screen.findByRole("switch", { name: "إضافة اللاعبون وأولياء الأمور" });
  const read = screen.getByRole("switch", { name: "قراءة اللاعبون وأولياء الأمور" });
  expect(read).not.toBeChecked(); fireEvent.click(add); expect(read).toBeChecked();
  fireEvent.click(read); expect(add).not.toBeChecked(); expect(read).not.toBeChecked();
});
it("requires an explicit branch and confirms before sending the complete matrix", async () => {
  const { fetcher, close } = setup(); await screen.findByRole("radio", { name: "فروع محددة" });
  fireEvent.click(screen.getByRole("radio", { name: "فروع محددة" }));
  const save = screen.getByRole("button", { name: "اعتماد الصلاحيات" }); expect(save).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox", { name: "فرع الاختبار" }));
  fireEvent.click(screen.getByRole("switch", { name: "قراءة الحضور" })); fireEvent.click(save);
  expect(fetcher.mock.calls.some(([, init]) => init?.method === "PUT")).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "تأكيد الاعتماد" }));
  await waitFor(() => expect(close).toHaveBeenCalledOnce());
  expect(fetcher).toHaveBeenCalledWith("/api/v1/manage/staff/admin/access", expect.objectContaining({ method: "PUT", body: JSON.stringify({ allBranches: false, branches: ["branch-a"], actions: [{ module: "players", read: false, add: false, edit: false, delete: false }, { module: "attendance", read: true, add: false, edit: false, delete: false }] }) }));
});
it("does not offer save when the server denies the access editor", async () => {
  global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 403 }); render(<StaffAccessEditor id="no" name="ممنوع" onClose={() => {}} />);
  expect(await screen.findByText("تعذر تحميل صلاحيات الموظف.")).toBeVisible();
  expect(screen.queryByRole("button", { name: "اعتماد الصلاحيات" })).not.toBeInTheDocument();
});
