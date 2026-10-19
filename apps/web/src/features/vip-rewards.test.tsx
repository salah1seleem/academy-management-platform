import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {VipRewards} from "./vip-rewards";
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const terms={items:[{id:"guardian",name:"أسرة اصطناعية",monthlyValue:1000}],currency:"EGP",today:"2026-09-28",conditions:"باقة واحدة دون جمع العروض"};
it("owner must preview before issue; changing data invalidates confirmation and retry keeps the attempt id",async()=>{
  const writes:{path:string;body:Record<string,unknown>}[]=[];
  global.fetch=vi.fn(async(url,init)=>{const path=String(url);if(path.includes("auth/csrf"))return Response.json({token:"csrf"});if(init?.method==="POST"){const body=JSON.parse(String(init.body));writes.push({path,body});if(path.endsWith("/preview"))return Response.json({quoteHash:"hash",recipients:terms.items,percentage:body.percentage,message:body.message,expiresOn:body.expiresOn,conditions:terms.conditions,delivery:"إشعار داخلي فقط"});return Response.json({ids:["reward"]},{status:201});}return Response.json(path.endsWith("options")?terms:{items:[],count:0,page:1,conditions:terms.conditions});}) as typeof fetch;
  render(<VipRewards owner/>);fireEvent.click(await screen.findByRole("button",{name:"إرسال مكافأة"}));fireEvent.click(await screen.findByRole("checkbox"));fireEvent.change(screen.getByLabelText("نسبة الخصم %"),{target:{value:"20"}});fireEvent.click(screen.getByRole("button",{name:"معاينة المكافأة"}));await screen.findByRole("region",{name:"تأكيد المكافأة"});expect(writes).toHaveLength(1);expect(writes[0].path).toMatch(/preview$/);
  fireEvent.change(screen.getByLabelText("رسالة الأسرة"),{target:{value:"رسالة معدلة للأسرة"}});expect(screen.queryByRole("button",{name:"تأكيد الإصدار وحفظ الإشعار الداخلي"})).not.toBeInTheDocument();fireEvent.click(screen.getByRole("button",{name:"معاينة المكافأة"}));fireEvent.click(await screen.findByRole("button",{name:"تأكيد الإصدار وحفظ الإشعار الداخلي"}));await waitFor(()=>expect(writes).toHaveLength(3));expect(writes[0].body.requestId).toBe(writes[2].body.requestId);expect(writes[2].body).toMatchObject({guardianIds:["guardian"],percentage:20,quoteHash:"hash",message:"رسالة معدلة للأسرة"});
});
it("family sees actual state, expiry and conditions, but no owner issuance or cancellation",async()=>{
  global.fetch=vi.fn(async()=>Response.json({items:[{id:"v",guardian:"أسرة",percentage:20,message:"شكرًا لثقتكم",issuedOn:"2026-09-28",expiresOn:"2026-10-28",state:"Reserved",read:false,delivery:"StoredInApp",history:[{createdAtUtc:"2026-09-28T12:00:00Z"}]}],count:1,page:1,conditions:terms.conditions})) as typeof fetch;
  render(<VipRewards/>);expect(await screen.findByText(/محجوزة لطلب/)).toBeInTheDocument();expect(screen.getByText(terms.conditions)).toBeInTheDocument();expect(screen.queryByRole("button",{name:"إرسال مكافأة"})).not.toBeInTheDocument();expect(screen.queryByRole("button",{name:"إلغاء المكافأة"})).not.toBeInTheDocument();expect(screen.queryByRole("link",{name:"اختيار باقة التجديد"})).not.toBeInTheDocument();
});
it("failed ledger request shows an Arabic error instead of invented zero rewards",async()=>{
  global.fetch=vi.fn(async()=>new Response(null,{status:403})) as typeof fetch;render(<VipRewards/>);expect(await screen.findByText(/تعذر تحميل المكافآت/)).toBeInTheDocument();expect(screen.queryByText("لا توجد مكافآت مسجلة بعد.")).not.toBeInTheDocument();
});
