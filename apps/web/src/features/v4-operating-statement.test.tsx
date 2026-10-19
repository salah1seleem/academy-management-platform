import {cleanup,render,screen} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {OperatingStatementReport} from "./operating-statement";
vi.mock("next/navigation",()=>({useSearchParams:()=>new URLSearchParams("from=2026-10-01&to=2026-10-19&branchId=b")}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it("shows cash basis, actual source figures and matching export scope without claiming accrual profit",async()=>{
 global.fetch=vi.fn(async path=>Response.json(String(path).includes("structure")?{branches:[],sports:[]}:{currency:"EGP",basis:"Cash",income:[{key:"subscription",label:"اشتراكات محصلة",amount:110000,sourceUrl:"/dashboard/reports/financial"}],expenses:[{key:"cost",label:"مدفوعات تشغيلية",amount:69000,sourceUrl:"/dashboard/finance/operating-expenses"}],totalIncome:110000,totalExpenses:69000,surplus:41000,refunds:0,discounts:0,receivables:3000,approvedPayrollPayable:1000,unpaidOperatingExpenses:2000,advanceCashOut:500,advanceApplied:100,earnedRevenue:null,warnings:["بيانات اصطناعية من استجابة الاختبار؛ ليس إثبات ديمو مالي."]})) as typeof fetch;
 render(<OperatingStatementReport/>);await screen.findByText("ليس ربحًا نهائيًا على أساس الاستحقاق");expect(screen.getByRole("link",{name:"تصدير القائمة نفسها"})).toHaveAttribute("href",expect.stringContaining("branchId=b"));expect(screen.getByRole("link",{name:/اشتراكات محصلة/})).toHaveAttribute("href",expect.stringContaining("from=2026-10-01"));expect(screen.getByText(/بيانات اصطناعية من استجابة الاختبار/)).toBeVisible();
});
