import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,beforeEach,expect,it,vi} from "vitest";
import {PayrollFinalPayment} from "./payroll-final-payment";
beforeEach(()=>{Object.defineProperty(HTMLDialogElement.prototype,"showModal",{configurable:true,value:function(this:HTMLDialogElement){this.setAttribute("open","");}});Object.defineProperty(HTMLDialogElement.prototype,"close",{configurable:true,value:function(this:HTMLDialogElement){this.removeAttribute("open");}});});
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const reply=(body:unknown,status=200)=>Promise.resolve(new Response(JSON.stringify(body),{status}));
it("confirms actual partial payout separately from approval with exact source, expected balances and key",async()=>{
  global.fetch=vi.fn((url)=>String(url).includes("csrf")?reply({token:"csrf"}):reply({id:"p"},201)) as typeof fetch;const changed=vi.fn();render(<PayrollFinalPayment employeeId="employee" sourceId="source" paid={20} remaining={30} currency="EGP" onChanged={changed}/>);
  expect(screen.getByLabelText("مبلغ الصرف")).toHaveAttribute("max","30");fireEvent.change(screen.getByLabelText("مبلغ الصرف"),{target:{value:"10"}});fireEvent.click(screen.getByRole("button",{name:"مراجعة صرف العمولة"}));expect(fetch).not.toHaveBeenCalled();expect(screen.getByRole("dialog")).toHaveTextContent("لا ينفذ تحويلًا بنكيًا أو يسجل مصروفًا ثانيًا");fireEvent.click(screen.getByRole("button",{name:"تأكيد تسجيل الصرف"}));await waitFor(()=>expect(changed).toHaveBeenCalledOnce());const call=vi.mocked(fetch).mock.calls.find(([,i])=>i?.method==="POST")!;expect(call[0]).toBe("/api/v1/manage/payroll/employee/final-settlements/source/disbursements");expect(JSON.parse(String(call[1]?.body))).toEqual({amount:10,reference:"",expectedPaid:20,expectedRemaining:30});expect((call[1]?.headers as Record<string,string>)["Idempotency-Key"]).toHaveLength(36);
});
it("does not claim payout when the reviewed balance has changed",async()=>{
  global.fetch=vi.fn((url)=>String(url).includes("csrf")?reply({token:"csrf"}):reply({title:"تغير المتبقي؛ حدّث الحساب."},409)) as typeof fetch;const changed=vi.fn();render(<PayrollFinalPayment employeeId="e" sourceId="s" paid={0} remaining={50} currency="EGP" onChanged={changed}/>);fireEvent.change(screen.getByLabelText("مبلغ الصرف"),{target:{value:"20"}});fireEvent.click(screen.getByRole("button",{name:"مراجعة صرف العمولة"}));fireEvent.click(screen.getByRole("button",{name:"تأكيد تسجيل الصرف"}));await screen.findByRole("alert");expect(changed).not.toHaveBeenCalled();
});
