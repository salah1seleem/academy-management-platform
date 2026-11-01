import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,beforeEach,expect,it,vi} from "vitest";
import {FinanceWorkspace} from "./finance-workspace";

let query=new URLSearchParams();
const replace=vi.fn();
vi.mock("next/navigation",()=>({useSearchParams:()=>query,usePathname:()=>"/dashboard/finance",useRouter:()=>({replace})}));
beforeEach(()=>{query=new URLSearchParams();replace.mockReset();global.fetch=vi.fn(async input=>Response.json(String(input).includes("my-permissions")?["reports.financial.read","payroll.read"]:{navigation:{cashEnabled:false}}));});
afterEach(()=>{cleanup();vi.restoreAllMocks();});

it("opens a shared link at the intended finance section rather than resetting to collections",async()=>{
  query=new URLSearchParams("tab=payroll&branchId=branch-demo");
  render(<FinanceWorkspace/>);
  expect(await screen.findByRole("link",{name:"كشف المستحقات والصرف"})).toHaveAttribute("href","/dashboard/payroll");
  expect(screen.getByRole("tab",{name:"الرواتب والمستحقات"})).toHaveAttribute("aria-selected","true");
  expect(screen.queryByRole("link",{name:"المطالبات والأرصدة"})).not.toBeInTheDocument();
});

it("keeps existing query context when changing the selected finance section",async()=>{
  query=new URLSearchParams("branchId=branch-demo");
  render(<FinanceWorkspace/>);
  fireEvent.click(await screen.findByRole("tab",{name:"التقارير المالية"}));
  await waitFor(()=>expect(replace).toHaveBeenCalledWith("/dashboard/finance?branchId=branch-demo&tab=reports",{scroll:false}));
});

it("does not reveal payroll links when an unauthorized payroll tab is requested",async()=>{
  query=new URLSearchParams("tab=payroll");
  global.fetch=vi.fn(async input=>Response.json(String(input).includes("my-permissions")?["reports.financial.read"]:{navigation:{cashEnabled:false}}));
  render(<FinanceWorkspace/>);
  expect(await screen.findByRole("link",{name:"المطالبات والأرصدة"})).toBeVisible();
  expect(screen.queryByRole("tab",{name:"الرواتب والمستحقات"})).not.toBeInTheDocument();
  expect(screen.queryByRole("link",{name:"كشف المستحقات والصرف"})).not.toBeInTheDocument();
});
