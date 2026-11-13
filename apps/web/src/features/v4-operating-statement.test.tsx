import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {OperatingStatementReport} from "./operating-statement";
const initialQuery="from=2026-10-01&to=2026-10-19&branchId=b";
const navigation=vi.hoisted(()=>({query:""}));
vi.mock("next/navigation",()=>({useSearchParams:()=>new URLSearchParams(navigation.query||initialQuery)}));
afterEach(()=>{cleanup();navigation.query="";vi.restoreAllMocks();});
it("shows cash basis, actual source figures and matching export scope without claiming accrual profit",async()=>{
 global.fetch=vi.fn(async path=>Response.json(String(path).includes("structure")?{branches:[],sports:[]}:{currency:"EGP",basis:"Cash",income:[{key:"subscription",label:"اشتراكات محصلة",amount:110000,sourceUrl:"/dashboard/reports/financial"}],expenses:[{key:"cost",label:"مدفوعات تشغيلية",amount:69000,sourceUrl:"/dashboard/finance/operating-expenses"}],totalIncome:110000,totalExpenses:69000,surplus:41000,refunds:0,discounts:0,receivables:3000,approvedPayrollPayable:1000,unpaidOperatingExpenses:2000,advanceCashOut:500,advanceApplied:100,earnedRevenue:null,warnings:["بيانات اصطناعية من استجابة الاختبار؛ ليس إثبات ديمو مالي."]})) as typeof fetch;
 render(<OperatingStatementReport/>);await screen.findByText("ليس ربحًا نهائيًا على أساس الاستحقاق");expect(screen.getByRole("link",{name:"تصدير القائمة نفسها"})).toHaveAttribute("href",expect.stringContaining("branchId=b"));expect(screen.getByRole("link",{name:/اشتراكات محصلة/})).toHaveAttribute("href",expect.stringContaining("from=2026-10-01"));fireEvent.click(screen.getByText("اكتمال البيانات وأساس التقرير"));expect(screen.getByText(/بيانات اصطناعية من استجابة الاختبار/)).toBeVisible();
});

it("updates the report on monthly link navigation and preserves all-time links",async()=>{
 const calls:string[]=[];
 global.fetch=vi.fn(async path=>{
  calls.push(String(path));
  return Response.json(String(path).includes("structure")?{branches:[],sports:[]}:{
   currency:"EGP",basis:"Cash",income:[],expenses:[],totalIncome:0,totalExpenses:0,surplus:0,warnings:[],
  });
 }) as typeof fetch;
 const {rerender}=render(<OperatingStatementReport/>);
 expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("2026-10-19");
 navigation.query="from=2026-09-01&to=2026-09-30&branchId=b";
 rerender(<OperatingStatementReport/>);
 expect(screen.getByLabelText("من تاريخ")).toHaveValue("2026-09-01");
 expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("2026-09-30");
 await waitFor(()=>expect(calls).toContain("/api/v1/reports/operating-statement?from=2026-09-01&to=2026-09-30&branchId=b&includeMonthly=true"));
 navigation.query="allDates=true&branchId=b";
 rerender(<OperatingStatementReport/>);
 expect(screen.getByLabelText("من تاريخ")).toHaveValue("");
 expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("");
 await waitFor(()=>expect(calls).toContain("/api/v1/reports/operating-statement?branchId=b&allDates=true&includeMonthly=true"));
});

it("withholds results and export for invalid or unread ranges and restores the exact source scopes", async () => {
 const requests: string[] = [];
 let fail = false;
 global.fetch = vi.fn(async path => {
  const url = String(path);
  if (url.includes("structure")) return Response.json({ branches: [], sports: [] });
  requests.push(url);
  if (fail) { fail = false; return new Response(null, { status: 503 }); }
  return Response.json({
   from: "2026-10-01", to: "2026-10-20", currency: "EGP", income: [], expenses: [],
   totalIncome: 950, totalExpenses: 125, surplus: 825, warnings: [], unpaidOperatingExpenses: 75, refunds: 20,
  });
 });
 render(<OperatingStatementReport />);
 const exportLink = () => screen.queryByRole("link", { name: "تصدير القائمة نفسها" });
 expect(exportLink()).toBeNull();
 await screen.findByRole("link", { name: /مصروفات مرحلة غير مسددة/ });
 const reads = requests.length;
 fireEvent.change(screen.getByLabelText("إلى تاريخ"), { target: { value: "2026-09-30" } });
 expect(screen.getByRole("alert")).toHaveTextContent("تاريخ البداية يجب أن يسبق تاريخ النهاية أو يساويه.");
 expect(requests).toHaveLength(reads);
 expect(exportLink()).toBeNull();
 expect(screen.queryByText("825 جنيه")).not.toBeInTheDocument();
 fail = true;
 fireEvent.change(screen.getByLabelText("إلى تاريخ"), { target: { value: "2026-10-20" } });
 const retry = await screen.findByRole("button", { name: "إعادة المحاولة" });
 expect(exportLink()).toBeNull();
 fireEvent.click(retry);
 const unpaid = await screen.findByRole("link", { name: /مصروفات مرحلة غير مسددة/ });
 const params = new URL(unpaid.getAttribute("href")!, "https://demo.invalid").searchParams;
 expect(Object.fromEntries(params)).toEqual({ unpaid: "true", status: "Posted", to: "2026-10-20", branchId: "b" });
 expect(screen.getByRole("link", { name: /استردادات — خُصمت/ })).toHaveAttribute("href", expect.stringContaining("collectionType=Refund"));
 expect(requests.at(-1)).toBe(requests.at(-2));
 expect(exportLink()).toHaveAttribute("href", expect.stringContaining("to=2026-10-20"));
});
