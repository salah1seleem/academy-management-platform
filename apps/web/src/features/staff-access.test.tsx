import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StaffAccessEditor } from "./staff-access";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("sensitive payroll actions start denied, require CRUD, and are explicitly reviewed rather than inherited from a template", async () => {
  const payroll = { module: "payroll", read: true, add: true, edit: true, delete: false };
  const fixture = { ...value, modules: ["payroll"], actions: [payroll], sensitivePermissions: ["payroll.pay", "payroll.addition.review", "payroll.salary.manage", "payroll.statement.approve"], selectedSensitivePermissions: [], templates: [{ key: "finance", name: "متابعة رواتب", actions: [payroll] }] };
  const fetcher = vi.fn().mockImplementation(async (url: string) => ({ ok: true, status: 200, json: async () => url.endsWith("/csrf") ? { token: "csrf" } : fixture }));
  global.fetch = fetcher; const close = vi.fn(); render(<StaffAccessEditor id="admin" name="إداري رواتب" onClose={close} />);
  const pay = await screen.findByRole("switch", { name: "تسجيل صرف الرواتب" });
  expect(pay).not.toBeChecked(); fireEvent.change(screen.getByRole("combobox", { name: "قالب الصلاحيات" }), { target: { value: "finance" } });
  const salary=screen.getByRole("switch",{name:"اعتماد الراتب وتغييراته المؤرخة"});expect(salary).not.toBeChecked();fireEvent.click(salary);expect(salary).toBeChecked();
  const statement=screen.getByRole("switch",{name:"اعتماد كشف المستحقات الشهري"});expect(statement).not.toBeChecked();fireEvent.click(statement);expect(statement).toBeChecked();
  expect(pay).not.toBeChecked(); fireEvent.click(screen.getByRole("radio", { name: "كل فروع الأكاديمية" }));
  fireEvent.click(pay); expect(pay).toBeChecked();
  fireEvent.click(screen.getByRole("switch", { name: "إضافة الموظفون والرواتب" })); expect(pay).not.toBeChecked(); expect(pay).toBeDisabled();
  expect(salary).not.toBeChecked();expect(salary).toBeDisabled();
  expect(statement).toBeChecked();fireEvent.click(screen.getByRole("switch",{name:"تعديل الموظفون والرواتب"}));expect(statement).not.toBeChecked();expect(statement).toBeDisabled();fireEvent.click(screen.getByRole("switch",{name:"تعديل الموظفون والرواتب"}));expect(statement).not.toBeChecked();
  fireEvent.click(screen.getByRole("switch", { name: "اعتماد أو رفض المبالغ الإضافية" }));
  fireEvent.click(screen.getByRole("button", { name: "اعتماد الصلاحيات" }));
  expect(fetcher.mock.calls.some(([, init]) => init?.method === "PUT")).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "تأكيد الاعتماد" })); await waitFor(() => expect(close).toHaveBeenCalledOnce());
  expect(JSON.parse(fetcher.mock.calls.find(([, init]) => init?.method === "PUT")![1].body).sensitivePermissions).toEqual(["payroll.addition.review"]);
});
it("sends the selected custom template revision while retaining explicit branch and sport review", async () => {
  const custom = { key: "custom:role-a", name: "منسق الحضور", description: "قالب مخصص", custom: true, revision: 4, actions: [{ module: "players", read: true, add: false, edit: false, delete: false }, { module: "attendance", read: true, add: true, edit: true, delete: false }] };
  const fetcher = vi.fn().mockImplementation(async (url: string) => ({ ok: true, status: 200, json: async () => url.endsWith("/csrf") ? { token: "csrf" } : { ...value, templates: [custom], source: { name: "منسق سابق", appliedRevision: 1, currentRevision: 2, customized: true, isActive: false } } }));
  global.fetch = fetcher; const close = vi.fn(); render(<StaffAccessEditor id="admin" name="إداري تجربة" onClose={close} />);
  expect(await screen.findByText(/المنح الحالية لم تتغير/)).toBeVisible();
  fireEvent.change(screen.getByRole("combobox", { name: "قالب الصلاحيات" }), { target: { value: custom.key } });
  fireEvent.click(screen.getByRole("checkbox", { name: "فرع الاختبار" }));
  fireEvent.click(screen.getByRole("button", { name: "اعتماد الصلاحيات" })); fireEvent.click(screen.getByRole("button", { name: "تأكيد الاعتماد" }));
  await waitFor(() => expect(close).toHaveBeenCalledOnce());
  expect(JSON.parse(fetcher.mock.calls.find(([, init]) => init?.method === "PUT")![1].body)).toMatchObject({ templateKey: custom.key, templateRevision: 4, allBranches: false, branches: ["branch-a"], actions: custom.actions });
});
it("requires sports, previews the correlated scope and saves it without widening", async () => {
  const fetcher = vi.fn().mockImplementation(async (url: string) => ({ ok: true, status: 200, json: async () => url.endsWith("/csrf") ? { token: "csrf" } : { ...value, allSports: false, sports: [{ id: "football", name: "كرة القدم" }, { id: "swimming", name: "السباحة" }], selectedSports: ["football"] } }));
  global.fetch = fetcher; const close = vi.fn(); render(<StaffAccessEditor id="admin" name="إداري كرة القدم" onClose={close} />);
  const selected = await screen.findByRole("checkbox", { name: "كرة القدم" });
  expect(selected).toBeChecked(); expect(screen.getByRole("radio", { name: "رياضات محددة" })).toBeChecked();
  fireEvent.click(selected); expect(screen.getByRole("button", { name: "اعتماد الصلاحيات" })).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox", { name: "السباحة" }));
  expect(screen.getByRole("region", { name: "معاينة أثر الصلاحيات" })).toHaveTextContent("كل فروع الأكاديمية · السباحة");
  fireEvent.click(screen.getByRole("button", { name: "اعتماد الصلاحيات" }));
  fireEvent.click(screen.getByRole("button", { name: "تأكيد الاعتماد" }));
  await waitFor(() => expect(close).toHaveBeenCalledOnce());
  expect(JSON.parse(fetcher.mock.calls.find(([, init]) => init?.method === "PUT")![1].body)).toMatchObject({ allBranches: true, branches: [], allSports: false, sports: ["swimming"] });
});
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
it("applies a template without saving, restricts branches, previews changes and retains hidden matrix rows", async () => {
  const preset = { key: "branch-operator", name: "إداري فرع", description: "قالب تشغيل بلا رواتب", actions: [{ module: "players", read: true, add: true, edit: true, delete: false }, { module: "attendance", read: true, add: true, edit: true, delete: false }] };
  const fetcher = vi.fn().mockImplementation(async (url: string) => ({ ok: true, status: 200, json: async () => url.endsWith("/csrf") ? { token: "csrf" } : { ...value, templates: [preset] } }));
  global.fetch = fetcher; const close = vi.fn(); render(<StaffAccessEditor id="admin" name="إداري تجريبي" onClose={close} />);
  fireEvent.change(await screen.findByRole("combobox", { name: "قالب الصلاحيات" }), { target: { value: "branch-operator" } });
  expect(screen.getByRole("radio", { name: "فروع محددة" })).toBeChecked();
  expect(screen.getByRole("switch", { name: "إضافة اللاعبون وأولياء الأمور" })).toBeChecked();
  expect(screen.getByRole("button", { name: "اعتماد الصلاحيات" })).toBeDisabled();
  expect(fetcher.mock.calls.some(([, init]) => init?.method === "PUT")).toBe(false);
  fireEvent.click(screen.getByRole("checkbox", { name: "فرع الاختبار" }));
  fireEvent.change(screen.getByLabelText("البحث عن وحدة"), { target: { value: "الحضور" } });
  expect(screen.queryByRole("switch", { name: "إضافة اللاعبون وأولياء الأمور" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "اعتماد الصلاحيات" }));
  fireEvent.click(screen.getByRole("button", { name: "تأكيد الاعتماد" }));
  await waitFor(() => expect(close).toHaveBeenCalledOnce());
  const sent = fetcher.mock.calls.find(([, init]) => init?.method === "PUT"); const body = JSON.parse(sent![1].body);
  expect(body).toMatchObject({ allBranches: false, branches: ["branch-a"], templateKey: "branch-operator" });
  expect(body.actions).toEqual(preset.actions);
});
