import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import NewPlayerPage from "../app/dashboard/players/new/page";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn(), back: vi.fn() }) }));
beforeEach(() => {
  global.fetch = vi.fn(input => Promise.resolve(new Response(JSON.stringify(
    String(input).includes("structure/options") ? {
      branches: [], categories: [], groups: [],
      sports: [{ id: "football", arabicName: "كرة القدم" }, { id: "swimming", arabicName: "سباحة" }, { id: "tennis", arabicName: "تنس" }],
    } : String(input).includes("discount-rules") ? { items: [] } : [
      { id: "monthly", arabicName: "اشتراك شهري", sportId: "football", price: 900, currency: "EGP", isActive: true },
      { id: "swim", arabicName: "باقة سباحة", sportId: "swimming", price: 500, currency: "EGP", isActive: true },
      { id: "inactive", arabicName: "باقة متوقفة", sportId: "football", price: 100, currency: "EGP", isActive: false },
    ]
  )))) as typeof fetch;
});
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("keeps compatible group and package selected together and filters private training from group offers", async () => {
  global.fetch = vi.fn(input => Promise.resolve(Response.json(String(input).includes("structure/options") ? {
    branches: [{ id: "b", arabicName: "فرع اختبار" }], categories: [{ id: "c", arabicName: "عمرية اختبار" }], sports: [{ id: "swim", arabicName: "سباحة" }], groups: [
      { id: "group", arabicName: "جماعي اختبار", branchId: "b", sportId: "swim", ageCategoryId: "c", registrationFormat: "Group" },
      { id: "private", arabicName: "خاص اختبار", branchId: "b", sportId: "swim", ageCategoryId: "c", registrationFormat: "Private" },
    ],
  } : String(input).includes("discount-rules") ? { items: [] } : [
    { id: "season", arabicName: "موسم اختبار", sportId: "swim", price: 2400, currency: "EGP", isActive: true, trainingFormat: "Season" },
    { id: "private-plan", arabicName: "باقة خاص اختبار", sportId: "swim", price: 900, currency: "EGP", isActive: true, trainingFormat: "Private" },
  ]))) as typeof fetch;
  render(<NewPlayerPage />); await screen.findByRole("option", { name: /موسم اختبار/ }); fireEvent.change(screen.getByLabelText("الفرع"), { target: { value: "b" } }); fireEvent.change(screen.getByLabelText("الرياضة"), { target: { value: "swim" } }); fireEvent.change(screen.getByLabelText("الفئة العمرية"), { target: { value: "c" } });
  fireEvent.change(screen.getByLabelText("الباقة"), { target: { value: "season" } }); expect(screen.queryByRole("option", { name: "خاص اختبار" })).not.toBeInTheDocument(); fireEvent.change(screen.getByLabelText("المجموعة"), { target: { value: "group" } }); expect(screen.getByLabelText("الباقة")).toHaveValue("season"); expect(screen.getByLabelText("المجموعة")).toHaveValue("group"); expect(screen.queryByRole("option", { name: /باقة خاص اختبار/ })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("المجموعة"), { target: { value: "" } }); fireEvent.change(screen.getByLabelText("الباقة"), { target: { value: "private-plan" } }); fireEvent.change(screen.getByLabelText("المجموعة"), { target: { value: "private" } }); expect(screen.getByLabelText("الباقة")).toHaveValue("private-plan"); expect(screen.getByLabelText("المجموعة")).toHaveValue("private");
});

it("shows active packages before choosing a sport and selects the package sport automatically", async () => {
  render(<NewPlayerPage />);
  expect(await screen.findByRole("option", { name: /اشتراك شهري/ })).toBeVisible();
  expect(screen.getByRole("option", { name: /باقة سباحة/ })).toBeVisible();
  expect(screen.queryByRole("option", { name: /باقة متوقفة/ })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("الباقة"), { target: { value: "monthly" } });
  expect(screen.getByLabelText("الرياضة")).toHaveValue("football");
  expect(screen.getByLabelText("الباقة")).toHaveValue("monthly");
  expect(screen.queryByRole("option", { name: /باقة سباحة/ })).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("الرياضة"), { target: { value: "swimming" } });
  expect(screen.getByLabelText("الباقة")).toHaveValue("");
  expect(screen.getByRole("option", { name: /باقة سباحة/ })).toBeVisible();
  expect(screen.queryByRole("option", { name: /اشتراك شهري/ })).not.toBeInTheDocument();
});

it("explains when the chosen sport has no active packages without allowing collection", async () => {
  render(<NewPlayerPage />);
  await screen.findByRole("option", { name: /اشتراك شهري/ });
  fireEvent.change(screen.getByLabelText("الرياضة"), { target: { value: "tennis" } });
  expect(screen.getByText(/لا توجد باقات نشطة لهذه الرياضة/)).toBeVisible();
  expect(screen.getByRole("button", { name: "تسجيل وتأكيد التحصيل" })).toBeDisabled();
});

it.each([[2,'100',900],[1,'15',850],[1,'100',0]])('exceptional type %s value %s uses canonical quote and confirms the actual financial effect',async(type,value,final)=>{
 const posts:{path:string;body:Record<string,unknown>}[]=[];vi.spyOn(window,'confirm').mockReturnValue(true);
 global.fetch=vi.fn((input,init)=>{const path=String(input);if(init?.method==='POST'){const body=JSON.parse(String(init.body));posts.push({path,body});return Promise.resolve(Response.json(path.endsWith('registration-quote')?{originalAmount:1000,discountAmount:1000-final,finalAmount:final,currency:'EGP'}:{playerId:'synthetic-player'}));}
 const data=path.endsWith('/me')?{role:'AcademyOwner'}:path.includes('/auth/csrf')?{token:'synthetic-csrf'}:path.includes('structure/options')?{branches:[{id:'b',arabicName:'فرع اصطناعي'}],sports:[{id:'s',arabicName:'رياضة اختبار'}],categories:[{id:'c',arabicName:'فئة اختبار'}],groups:[{id:'g',arabicName:'مجموعة اختبار',branchId:'b',sportId:'s',ageCategoryId:'c',registrationFormat:'Group'}]}:path.includes('discount-rules')?{items:[]}:[{id:'p',arabicName:'باقة اختبار',sportId:'s',price:1000,currency:'EGP',isActive:true,trainingFormat:'Group'}];return Promise.resolve(Response.json(data));}) as typeof fetch;
 render(<NewPlayerPage/>);await screen.findByRole('checkbox',{name:'إضافة خصم استثنائي'});
 for(const [label,entry] of [['اسم اللاعب بالعربية','طفل اصطناعي'],['تاريخ الميلاد','2016-01-01'],['اسم ولي الأمر','ولي أمر اصطناعي'],['رقم الهاتف','01000000000'],['الفرع','b'],['الرياضة','s'],['الفئة العمرية','c'],['المجموعة','g'],['الباقة','p']])fireEvent.change(screen.getByLabelText(label),{target:{value:entry}});
 fireEvent.click(screen.getByRole('checkbox',{name:'إضافة خصم استثنائي'}));fireEvent.change(screen.getByLabelText('نوع الخصم'),{target:{value:String(type)}});fireEvent.change(screen.getByLabelText('قيمة الخصم'),{target:{value}});fireEvent.change(screen.getByLabelText('سبب الخصم'),{target:{value:'سبب اصطناعي للمراجعة'}});
 fireEvent.click(screen.getByRole('button',{name:'حساب المبلغ المستحق'}));await screen.findByText(/المبلغ المطلوب تحصيله/);
 if(final!==0)fireEvent.change(screen.getByRole('combobox',{name:'طريقة التحصيل'}),{target:{value:'Online'}});
 fireEvent.click(screen.getByRole('button',{name:final===0?'اعتماد الإعفاء والتسجيل':'تسجيل وتأكيد التحصيل'}));
 const {waitFor}=await import('@testing-library/react');await waitFor(()=>expect(posts.some(p=>p.path.endsWith('/registrations'))).toBe(true));
 const quote=posts.find(p=>p.path.endsWith('registration-quote'))!,registration=posts.find(p=>p.path.endsWith('/registrations'))!;
 expect(quote.body.exceptionalDiscount).toEqual({type,value,reason:'سبب اصطناعي للمراجعة',replaceExistingDiscount:false});expect(registration.body.exceptionalDiscount).toEqual(quote.body.exceptionalDiscount);expect(registration.body.expectedAmount).toBe(final);expect(registration.body.paymentMethod).toBe('Online');expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining(final===0?'دون تحصيل':'تأكيد استلام'));
});
