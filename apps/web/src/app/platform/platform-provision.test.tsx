import {afterEach,expect,it,vi} from "vitest";
import {cleanup,fireEvent,render,screen} from "@testing-library/react";
import PlatformHome from "./page";
import {platformRequest,PlatformSessionExpiredError} from "../../features/platform-api";
vi.mock("next/navigation",()=>({useRouter:()=>({replace:vi.fn()})}));
vi.mock("../../features/platform-api",async importOriginal=>({...await importOriginal<typeof import("../../features/platform-api")>(),platformRequest:vi.fn()}));
vi.mock("./platform-academy-editor",()=>({PlatformAcademyEditor:()=> <p>تم فتح تجهيز الأكاديمية</p>}));
afterEach(()=>{cleanup();vi.restoreAllMocks();vi.clearAllMocks();});
async function openForm(){
 global.fetch=vi.fn(async()=>new Response(JSON.stringify({totals:{all:0,active:0,preparing:0,suspended:0,reminders:0},items:[],page:1,count:0,pageSize:15}),{status:200}));
 render(<PlatformHome/>);fireEvent.click(await screen.findByRole("button",{name:/إنشاء أكاديمية/}));
}
it("explains slug examples and rejects a full URL before provisioning",async()=>{
 await openForm();const slug=screen.getByLabelText(/^كود الرابط/);
 expect(screen.queryByText(/رمز فريد مثل arm/)).not.toBeInTheDocument();
 expect(slug).not.toHaveAttribute("aria-describedby");
 fireEvent.change(slug,{target:{value:"http://localhost:3379"}});expect((slug as HTMLInputElement).validity.patternMismatch).toBe(true);fireEvent.invalid(slug);
 expect((slug as HTMLInputElement).validationMessage).toContain("دون http://");
 fireEvent.submit(slug.closest("form")!);
 expect(await screen.findByRole("alert")).toHaveTextContent("وليس عنوان موقع");
 expect(platformRequest).not.toHaveBeenCalled();
});
it.each(["arm","arm-academy","wave-swim","Arm-Professionals-Academy"])("accepts a short unique slug %s without any owner-link control",async(value)=>{
 await openForm();vi.mocked(platformRequest).mockResolvedValue(new Response(JSON.stringify({id:"synthetic-academy"}),{status:200}));
 const slug=screen.getByLabelText(/^كود الرابط/);fireEvent.input(slug,{target:{value}});
 expect((slug as HTMLInputElement).checkValidity()).toBe(true);
 expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();expect(screen.queryByRole("radio")).not.toBeInTheDocument();
 for(const [label,v] of [["اسم الأكاديمية","أكاديمية اختبار خيالية"],["اسم المالك","مالك اختبار خيالي"],["هاتف المالك","01084720997"],["بريد المالك","synthetic-owner@example.test"],["كلمة مرور أولية","Synthetic-Test-Only-2026!"]])fireEvent.change(screen.getByLabelText(label),{target:{value:v}});
 fireEvent.submit(slug.closest("form")!);await screen.findByText("تم فتح تجهيز الأكاديمية");
 expect(platformRequest).toHaveBeenCalledWith("/api/v1/platform/academies","POST",expect.objectContaining({slug:value.toLowerCase()}));
});

it("keeps a new academy form and request id when the session expires, then saves after reauthentication",async()=>{
 await openForm();vi.mocked(platformRequest).mockRejectedValueOnce(new PlatformSessionExpiredError()).mockResolvedValueOnce(Response.json({id:"recovered-academy"}));
 const slug=screen.getByLabelText(/^كود الرابط/);
 for(const [label,value] of [["اسم الأكاديمية","أكاديمية استعادة خيالية"],["كود الرابط","session-recovery"],["اسم المالك","مالك استعادة خيالي"],["هاتف المالك","01084720998"],["بريد المالك","recovery@example.test"],["كلمة مرور أولية","Synthetic-Test-Only-2026!"]])fireEvent.change(screen.getByLabelText(new RegExp("^"+label)),{target:{value}});
 fireEvent.submit(slug.closest("form")!);
 expect(await screen.findByRole("alert")).toHaveTextContent("انتهت جلسة إدارة المنصة");
 expect(screen.getByRole("link",{name:"تسجيل الدخول في نافذة أخرى"})).toHaveAttribute("target","_blank");
 expect(slug).toHaveValue("session-recovery");expect(screen.getByLabelText("اسم المالك")).toHaveValue("مالك استعادة خيالي");
 fireEvent.submit(slug.closest("form")!);await screen.findByText("تم فتح تجهيز الأكاديمية");
 expect(vi.mocked(platformRequest).mock.calls[1][2]).toEqual(vi.mocked(platformRequest).mock.calls[0][2]);
});
it("explains all password requirements before a new-owner write",async()=>{
 await openForm();fireEvent.change(screen.getByLabelText(/^كود الرابط/),{target:{value:"valid-code"}});fireEvent.change(screen.getByLabelText(/^كلمة مرور أولية/),{target:{value:"abcdefghijkl"}});
 fireEvent.submit(screen.getByLabelText(/^كود الرابط/).closest("form")!);
 expect(await screen.findByRole("alert")).toHaveTextContent("حرف إنجليزي كبير وصغير ورقم ورمز");expect(platformRequest).not.toHaveBeenCalled();
});

it("never sends an owner linking flag",async()=>{
 await openForm();vi.mocked(platformRequest).mockResolvedValue(Response.json({id:"synthetic-academy"}));
 for(const [label,value] of [["اسم الأكاديمية","أكاديمية اختبار خيالية"],["كود الرابط","simple-flow"],["اسم المالك","مالك اختبار خيالي"],["هاتف المالك","01084720930"],["بريد المالك","owner.simple@example.test"],["كلمة مرور أولية","Synthetic-Demo-2026!"]])fireEvent.change(screen.getByLabelText(new RegExp("^"+label)),{target:{value}});
 fireEvent.submit(screen.getByLabelText(/^كود الرابط/).closest("form")!);await screen.findByText("تم فتح تجهيز الأكاديمية");
 expect(vi.mocked(platformRequest).mock.calls[0][2]).not.toHaveProperty("associateExistingOwner");
});
