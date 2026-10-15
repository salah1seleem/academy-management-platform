import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeAll, expect, it, vi } from "vitest";
import { PrivatePostingAction } from "./private-posting-action";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
// jsdom has no native modal implementation; actual focus/overlay is covered in Chromium E2E.
beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute("open",""); };
  HTMLDialogElement.prototype.close = function () { this.removeAttribute("open"); };
});
const response = (body: unknown,status=200) => Promise.resolve(new Response(JSON.stringify(body),{status}));
const quote = { action:"Accrual",quoteHash:"a".repeat(64),message:"فرق العمولة فقط، لا صرف.",currency:"EGP",dueDate:"2026-10-15",currentPosted:50,targetAmount:100,difference:50 };
function mockPreview(action="Accrual",targetAmount:number|null=100) {
  global.fetch=vi.fn((url,init)=>String(url).endsWith("csrf")?response({token:"csrf"}):init?.method==="POST"?response({id:"entry"},201):String(url).endsWith("posting-preview")?response({...quote,action,targetAmount,difference:targetAmount===null?null:quote.difference}):response([{id:"entry0",revision:1,kind:"Accrual",delta:50,currency:"EGP",dueDate:"2026-10-15",actor:"مالك العرض"}])) as typeof fetch;
}
it("requires explicit review, sends only server quote and action with idempotency, then refreshes payroll source",async()=>{
  mockPreview(); const changed=vi.fn(async()=>{}); render(<PrivatePostingAction coachId="coach" deliveryId="delivery" onChanged={changed}/>);
  expect(fetch).not.toHaveBeenCalled(); fireEvent.click(screen.getByRole("button",{name:"مراجعة الاستحقاق"}));
  await screen.findByText("الفرق — ليس دفعة"); expect(screen.getByRole("dialog")).toHaveTextContent("مالك العرض"); expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();
  expect(vi.mocked(fetch).mock.calls.some(([,init])=>init?.method==="POST")).toBe(false);
  fireEvent.click(screen.getByRole("button",{name:"ترحيل فرق العمولة"})); await screen.findByRole("status");
  const request=vi.mocked(fetch).mock.calls.find(([,init])=>init?.method==="POST")!; expect(JSON.parse(String(request[1]?.body))).toEqual({action:"Accrual",quoteHash:quote.quoteHash});
  expect((request[1]?.headers as Record<string,string>)["Idempotency-Key"]).toHaveLength(36); expect(changed).toHaveBeenCalledTimes(1); expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
});
it("preserves null assessment as review and never invents a zero-valued payroll accrual",async()=>{
  mockPreview("Review",null); render(<PrivatePostingAction coachId="coach" deliveryId="delivery" onChanged={async()=>{}}/>); fireEvent.click(screen.getByRole("button",{name:"مراجعة الاستحقاق"}));
  await screen.findByText("غير محسوب"); expect(screen.getByText("يتطلب مراجعة")).toBeVisible(); expect(screen.queryByRole("button",{name:"ترحيل فرق العمولة"})).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button",{name:"حفظ استثناء للمراجعة"})); expect(await screen.findByRole("status")).toHaveTextContent("دون خصم أو سداد");
});
it.each(["Blocked","ReviewPending","None"])("does not offer a write action for %s",async(action)=>{
  mockPreview(action); render(<PrivatePostingAction coachId="coach" deliveryId="delivery" onChanged={async()=>{}}/>); fireEvent.click(screen.getByRole("button",{name:"مراجعة الاستحقاق"}));
  await screen.findByText("سجل المصدر"); expect(screen.queryByRole("button",{name:"ترحيل فرق العمولة"})).not.toBeInTheDocument(); expect(screen.queryByRole("button",{name:"تأكيد عكس العمولة غير المسددة"})).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button",{name:"إغلاق المراجعة"})); expect(vi.mocked(fetch).mock.calls.some(([,init])=>init?.method==="POST")).toBe(false);
});
it("displays stale-source denial, keeps the confirmation open and never claims success",async()=>{
  mockPreview(); const original=global.fetch;
  global.fetch=vi.fn((url,init)=>init?.method==="POST"?response({title:"تغيرت المصادر؛ أعد المراجعة."},409):original(url,init)) as typeof fetch;
  const changed=vi.fn(async()=>{}); render(<PrivatePostingAction coachId="coach" deliveryId="delivery" onChanged={changed}/>); fireEvent.click(screen.getByRole("button",{name:"مراجعة الاستحقاق"}));
  fireEvent.click(await screen.findByRole("button",{name:"ترحيل فرق العمولة"})); await waitFor(()=>expect(screen.getByRole("alert")).toHaveTextContent("تغيرت المصادر"));
  expect(changed).not.toHaveBeenCalled(); expect(screen.queryByRole("status")).not.toBeInTheDocument(); expect(screen.getByRole("dialog")).toBeVisible();
});
