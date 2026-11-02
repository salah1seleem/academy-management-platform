import { cleanup, fireEvent, render, screen } from "@testing-library/react";
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
