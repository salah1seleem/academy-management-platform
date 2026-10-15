import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {PrivateFinalAction} from "./private-final-action";
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const q={quoteHash:"f".repeat(64),canFinalize:true,contractEnd:"2026-09-30",difference:50,currency:"EGP",message:"استحقاق مستقل بلا راتب أو صرف."};
const response=(data:unknown,status=200)=>Promise.resolve(new Response(JSON.stringify(data),{status}));
it("previews and explicitly approves computed post-contract compensation without amount or month inputs",async()=>{
  global.fetch=vi.fn((url,init)=>String(url).endsWith("csrf")?response({token:"csrf"}):init?.method==="POST"?response({id:"final"},201):response(q)) as typeof fetch;
  const changed=vi.fn(async()=>{});render(<PrivateFinalAction path="/private" onChanged={changed}/>);expect(fetch).not.toHaveBeenCalled();fireEvent.click(screen.getByRole("button",{name:"معاينة مستحق نهاية العقد"}));await screen.findByText(q.message);expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();expect(screen.queryByLabelText(/شهر/)).not.toBeInTheDocument();
  expect(vi.mocked(fetch).mock.calls.some(([,i])=>i?.method==="POST")).toBe(false);fireEvent.click(screen.getByRole("button",{name:"تأكيد اعتماد العمولة دون صرف"}));await waitFor(()=>expect(changed).toHaveBeenCalledOnce());const call=vi.mocked(fetch).mock.calls.find(([,i])=>i?.method==="POST")!;expect(call[0]).toBe("/private/final-settlement");expect(JSON.parse(String(call[1]?.body))).toEqual({quoteHash:q.quoteHash,note:null});
});
it("never approves an ineligible contract and preserves stale failures without a success claim",async()=>{
  global.fetch=vi.fn(()=>response({...q,canFinalize:false})) as typeof fetch;render(<PrivateFinalAction path="/private" onChanged={async()=>{}}/>);fireEvent.click(screen.getByRole("button",{name:"معاينة مستحق نهاية العقد"}));await screen.findByText(q.message);expect(screen.queryByRole("button",{name:"تأكيد اعتماد العمولة دون صرف"})).not.toBeInTheDocument();
});
it("reuses the same idempotency key on unchanged failed retry",async()=>{
  global.fetch=vi.fn((url,init)=>String(url).endsWith("csrf")?response({token:"csrf"}):init?.method==="POST"?response({title:"تغير المصدر؛ أعد المراجعة."},409):response(q)) as typeof fetch;const changed=vi.fn(async()=>{});render(<PrivateFinalAction path="/private" onChanged={changed}/>);fireEvent.click(screen.getByRole("button",{name:"معاينة مستحق نهاية العقد"}));await screen.findByText(q.message);
  fireEvent.click(screen.getByRole("button",{name:"تأكيد اعتماد العمولة دون صرف"}));await screen.findByRole("alert");fireEvent.click(screen.getByRole("button",{name:"تأكيد اعتماد العمولة دون صرف"}));await waitFor(()=>expect(vi.mocked(fetch).mock.calls.filter(([,i])=>i?.method==="POST")).toHaveLength(2));const posts=vi.mocked(fetch).mock.calls.filter(([,i])=>i?.method==="POST");expect(posts[0][1]?.headers).toEqual(posts[1][1]?.headers);expect(changed).not.toHaveBeenCalled();
});
