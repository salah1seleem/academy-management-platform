import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StaffManagement } from "./staff-management";

vi.mock("./staff-access",()=>({StaffAccessEditor:({id,simple,onClose}:{id:string;simple?:boolean;onClose:()=>void})=><section aria-label="المحرر"><p>{id} · {simple?"بسيط":"متقدم"}</p><button onClick={onClose}>حفظ محرر الاختبار</button></section>}));
vi.mock("./staff-access-contexts",()=>({StaffAccessContexts:({id,onConfigure}:{id:string;onConfigure:(id:string,name:string)=>void})=><section aria-label="السياقات"><p>الإداري الأصلي {id}</p><button onClick={()=>onConfigure("extra-context","متابعة السباحة")}>تعديل سياق الاختبار</button></section>}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});
function setup(){global.fetch=vi.fn(async()=>new Response(JSON.stringify({members:[{id:"primary-admin",name:"إداري اصطناعي",email:"scope@example.test",role:"AcademyAdmin",grants:[]}]})));render(<StaffManagement/>);}

it("keeps ordinary action permissions simple but preserves an explicit advanced editor without changing the selected identity",async()=>{
  setup();fireEvent.change(await screen.findByRole("combobox",{name:"اختر الإداري"}),{target:{value:"primary-admin"}});
  expect(await screen.findByText("primary-admin · بسيط")).toBeVisible();
  fireEvent.click(screen.getByRole("checkbox",{name:"عرض القوالب والصلاحيات المتقدمة"}));
  expect(screen.getByText("primary-admin · متقدم")).toBeVisible();
  fireEvent.click(screen.getByRole("button",{name:"حفظ محرر الاختبار"}));
  expect(screen.queryByRole("region",{name:"المحرر"})).not.toBeInTheDocument();
  expect(screen.getByRole("combobox",{name:"اختر الإداري"})).toHaveValue("");
});

it("returns from a context permission save to the original administrator's contexts, not the extra identity",async()=>{
  setup();fireEvent.change(await screen.findByRole("combobox",{name:"اختر الإداري"}),{target:{value:"primary-admin"}});
  fireEvent.click(screen.getByText("إدارة متقدمة وسجل الصلاحيات"));
  fireEvent.click(screen.getByRole("button",{name:"نطاقات عمل إضافية لهذا الإداري"}));
  expect(screen.getByText("الإداري الأصلي primary-admin")).toBeVisible();
  fireEvent.click(screen.getByRole("button",{name:"تعديل سياق الاختبار"}));
  expect(screen.getByText("extra-context · متقدم")).toBeVisible();
  fireEvent.click(screen.getByRole("button",{name:"حفظ محرر الاختبار"}));
  expect(screen.getByText("الإداري الأصلي primary-admin")).toBeVisible();
  expect(screen.queryByRole("region",{name:"المحرر"})).not.toBeInTheDocument();
});

it("creates a phone-only coach with an explicit group link and no web credentials", async () => {
  const payloads: unknown[] = [];
  global.fetch = vi.fn(async (url, options) => {
    if (String(url).endsWith("/csrf")) return Response.json({ token: "demo-csrf" });
    if (options?.method === "POST") {
      payloads.push(JSON.parse(String(options.body)));
      return Response.json({ id: "coach-demo" });
    }
    return Response.json({ members: [] });
  });
  render(<StaffManagement />);
  fireEvent.click(await screen.findByRole("button", { name: "إضافة حساب دخول" }));
  fireEvent.change(screen.getByRole("combobox", { name: "الدور" }), { target: { value: "Coach" } });
  expect(screen.queryByLabelText("كلمة مرور الويب")).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("الاسم", { exact: true }), { target: { value: "مدرب اختباري" } });
  const phone = screen.getByLabelText("رقم الهاتف المصري");
  fireEvent.change(phone, { target: { value: "01399900100" } });
  fireEvent.click(screen.getByRole("button", { name: "إنشاء الحساب" }));
  expect(await screen.findByText("اكتب رقم موبايل مصري صحيح.")).toBeVisible();
  expect(payloads).toHaveLength(0);
  fireEvent.change(phone, { target: { value: "+٢٠ (٠) ١٠ ٩٩٩٠٠١٠٠" } });
  fireEvent.click(screen.getByRole("button", { name: "إنشاء الحساب" }));
  expect(await screen.findByRole("link", { name: "عرض مجموعات المدرب" })).toHaveAttribute("href", "/dashboard/academy/coaches/coach-demo");
  expect(payloads).toEqual([{ name: "مدرب اختباري", email: null, phone: "+201099900100", role: "Coach", jobTitle: null, temporaryPassword: null }]);
});

it("reads the directory after an uncertain write without sending another create", async () => {
  const member = { id: "coach-demo", name: "مدرب بعد إعادة القراءة", role: "Coach", isActive: true, phoneNumber: "+201099900100", email: "staff.2ab477f01ac742e3884f1d176c0862e1@accounts.invalid" };
  let writes = 0;
  global.fetch = vi.fn(async (url, options) => {
    if (String(url).endsWith("/csrf")) return Response.json({ token: "demo-csrf" });
    if (options?.method === "POST") {
      writes++;
      return new Response(null, { status: 503 });
    }
    return Response.json({ members: writes ? [member] : [] });
  });
  render(<StaffManagement />);
  fireEvent.click(await screen.findByRole("button", { name: "إضافة حساب دخول" }));
  fireEvent.change(screen.getByRole("combobox", { name: "الدور" }), { target: { value: "Coach" } });
  fireEvent.change(screen.getByLabelText("الاسم", { exact: true }), { target: { value: member.name } });
  fireEvent.change(screen.getByLabelText("رقم الهاتف المصري"), { target: { value: member.phoneNumber } });
  fireEvent.click(screen.getByRole("button", { name: "إنشاء الحساب" }));
  const review = await screen.findByRole("button", { name: "عرض حسابات الفريق" });
  expect(screen.queryByRole("button", { name: "إنشاء الحساب" })).not.toBeInTheDocument();
  fireEvent.click(review);
  const table = await screen.findByRole("table", { name: "حسابات الفريق" });
  expect(within(table).getByText(member.name)).toBeVisible();
  expect(within(table).queryByText(member.email)).not.toBeInTheDocument();
  expect(writes).toBe(1);
});
