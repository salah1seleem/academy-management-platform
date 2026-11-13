import { webcrypto } from "node:crypto";
import { cleanup,fireEvent,render,screen,waitFor } from "@testing-library/react";
import {afterEach,beforeEach,expect,it,vi} from "vitest";
import {ExternalAgreementDetail,ExternalAgreements} from "./external-agreements";
import {csrfRequest,DashboardRequestError} from "./dashboard-api";
vi.mock("next/navigation",()=>({useSearchParams:()=>new URLSearchParams(),useRouter:()=>({push:vi.fn()})}));
vi.mock("./dashboard-api",async importOriginal=>({
  ...await importOriginal<typeof import("./dashboard-api")>(),
  csrfRequest:vi.fn(),
}));
vi.mock("../components/date-filter-defaults",()=>({useDateFilterDefaults:()=>({from:"2026-10-01",to:"2026-10-19"})}));
afterEach(()=>{cleanup();sessionStorage.clear();vi.unstubAllGlobals();});
const agreement={id:"contract",version:1,title:"اتفاق تجريبي",counterpartyName:"طرف اصطناعي",kind:"ExternalProgram",fromDate:"2026-10-01",toDate:"2026-10-31",dueOn:"2026-10-19",amount:1000,currency:"EGP",paid:400,remaining:600,state:"Open"};
beforeEach(()=>{vi.clearAllMocks();vi.stubGlobal("fetch",vi.fn(async(input)=>({ok:true,json:async()=>String(input).includes("/options")?{canManage:true,currency:"EGP",branches:[],sports:[],employees:[],resources:[]}:String(input).includes("/contract")?{agreement,membershipId:"qa-member",paid:400,remaining:600,canManage:true,cash:{cashEnabled:false,sessions:[]},bookings:[],receipts:[{id:"receipt",receiptNumber:"EXT-demo",amount:400,currency:"EGP",paymentMethod:"Online",receivedAtUtc:"2026-10-19T08:00:00Z"}]}:{canManage:true,count:1,page:1,pageSize:25,items:[agreement]}})))});
it("opens list first and clears default dates without resetting them",async()=>{render(<ExternalAgreements/>);expect(await screen.findByText("اتفاق تجريبي")).toBeInTheDocument();expect(screen.getByRole("link",{name:/إنشاء اتفاق/})).toBeInTheDocument();expect(screen.queryByRole("button",{name:"حفظ الاتفاق"})).not.toBeInTheDocument();fireEvent.click(screen.getByRole("button",{name:"مسح"}));await waitFor(()=>expect(vi.mocked(fetch).mock.calls.some(([p])=>String(p).includes("allDates=true"))).toBe(true));});
it("shows actual remaining, receipt navigation and prevents paid cancellation",async()=>{render(<ExternalAgreementDetail id="contract"/>);expect(await screen.findByText("طرف اصطناعي")).toBeInTheDocument();expect(screen.getByText("المتبقي")).toBeInTheDocument();expect(screen.getByRole("link",{name:"عرض الإيصال EXT-demo"})).toHaveAttribute("href","/dashboard/finance/external-agreements/contract/receipts/receipt");expect(screen.queryByRole("button",{name:"إلغاء الاتفاق"})).not.toBeInTheDocument();expect(screen.getByRole("checkbox",{name:/أؤكد استلام/})).toBeRequired();});

function trackReceipts(loseFirstResponse = false) {
  vi.stubGlobal("crypto", webcrypto);
  const rows: { key: string; id: string; receiptNumber: string; amount: number }[] = [];
  vi.stubGlobal("fetch", vi.fn(async (_path, options?: RequestInit) => {
    const key = new Headers(options?.headers).get("Idempotency-Key");
    const paid = rows.reduce((sum, row) => sum + row.amount, 0);
    return new Response(JSON.stringify({
      agreement, membershipId: "owner-a", paid, remaining: agreement.amount - paid, canManage: true,
      cash: { cashEnabled: false, sessions: [] }, bookings: [],
      receipts: rows.map(row => ({
        id: row.id, receiptNumber: row.receiptNumber, amount: row.amount, currency: "EGP",
        paymentMethod: "Online", receivedAtUtc: "2026-10-19T08:00:00Z", matchesAttempt: row.key === key,
      })),
    }));
  }));
  vi.mocked(csrfRequest).mockImplementation(async (_path, _method, payload) => {
    const { requestId, amount } = payload as { requestId: string; amount: number };
    const row = { key: requestId, id: `r-${rows.length + 1}`, receiptNumber: `EXT-${rows.length + 1}`, amount };
    rows.push(row);
    if (loseFirstResponse && rows.length === 1) throw new TypeError("interrupted");
    return new Response(JSON.stringify({ id: row.id }), { status: 201 });
  });
  return rows;
}

async function collect125(action = "تسجيل التحصيل وإصدار الإيصال") {
  fireEvent.change(await screen.findByRole("spinbutton", { name: /المبلغ/ }), { target: { value: "125.50" } });
  fireEvent.click(screen.getByRole("checkbox", { name: /أؤكد استلام/ }));
  fireEvent.submit(screen.getByRole("button", { name: action }).closest("form")!);
}

it("creates a new receipt after a verified payment even when the amount and method are identical", async () => {
  const rows = trackReceipts();
  render(<ExternalAgreementDetail id="contract" />);
  await collect125();
  expect(await screen.findByRole("link", { name: "فتح الإيصال المسجّل" })).toHaveAttribute("href", expect.stringContaining("/r-1"));
  await collect125();
  await waitFor(() => expect(screen.getByRole("link", { name: "فتح الإيصال المسجّل" })).toHaveAttribute("href", expect.stringContaining("/r-2")));
  expect(rows).toHaveLength(2);
  expect(rows[1].key).not.toBe(rows[0].key);
});

it("restores the exact receipt on reload after a lost response without writing again", async () => {
  const rows = trackReceipts(true);
  const view = render(<ExternalAgreementDetail id="contract" />);
  await collect125();
  expect(await screen.findByRole("button", { name: "تحقّق من التحصيل" })).toBeEnabled();
  expect(screen.queryByRole("spinbutton", { name: /المبلغ/ })).not.toBeInTheDocument();
  view.unmount();
  render(<ExternalAgreementDetail id="contract" />);
  expect(await screen.findByRole("link", { name: "فتح الإيصال المسجّل" })).toHaveAttribute("href", expect.stringContaining("/r-1"));
  expect(rows).toHaveLength(1);
  expect(vi.mocked(csrfRequest)).toHaveBeenCalledTimes(1);
});

it("can settle the same pending request when the cash shift closes before reload", async () => {
  vi.stubGlobal("crypto", webcrypto);
  const shift = "65e129a2-66bc-4fa2-b6c6-913fc3d02ea3";
  let open = true;
  vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({
    agreement, membershipId: "owner-a", paid: 0, remaining: 1000, canManage: true,
    cash: { cashEnabled: true, sessions: open ? [{ id: shift, name: "صندوق الفرع" }] : [] },
    bookings: [], receipts: [],
  }))));
  const request = vi.mocked(csrfRequest);
  request.mockRejectedValueOnce(new TypeError("interrupted"));
  request.mockRejectedValueOnce(new DashboardRequestError("الوردية مقفلة.", 409));
  const view = render(<ExternalAgreementDetail id="contract" />);
  fireEvent.change(await screen.findByRole("combobox", { name: "طريقة الدفع" }), { target: { value: "Cash" } });
  fireEvent.change(screen.getByRole("combobox", { name: "ورديتك المفتوحة" }), { target: { value: shift } });
  await collect125();
  await waitFor(() => expect(screen.getByRole("button", { name: "تحقّق من التحصيل" })).toBeEnabled());
  const original = request.mock.calls[0][2];
  open = false;
  view.unmount();
  render(<ExternalAgreementDetail id="contract" />);
  expect(await screen.findByRole("button", { name: "إعادة إرسال نفس التحصيل" })).toBeEnabled();
  expect(screen.getByRole("combobox", { name: "طريقة الدفع" })).toHaveValue("Cash");
  await collect125("إعادة إرسال نفس التحصيل");
  expect(await screen.findByText("الوردية مقفلة.")).toBeVisible();
  expect(request.mock.calls.map(call => call[2])).toEqual([original, original]);
  expect(screen.queryByRole("button", { name: "تحقّق من التحصيل" })).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "تسجيل التحصيل وإصدار الإيصال" })).toBeDisabled();
});
