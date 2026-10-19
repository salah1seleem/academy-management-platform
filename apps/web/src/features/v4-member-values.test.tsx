import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {AdminDataTable} from "../components/dashboard-shell";
import GuardiansPage from "../app/dashboard/guardians/page";
import {VipMembers} from "./vip-members";
vi.mock("next/navigation",()=>({useSearchParams:()=>new URLSearchParams(),usePathname:()=>"/dashboard/guardians",useRouter:()=>({push:vi.fn()})}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it("server pages are not sliced again and sort direction is accessible",()=>{
  const change=vi.fn(),sort=vi.fn();render(<AdminDataTable label="أعضاء" rows={[{id:"a"},{id:"b"}]} rowKey={r=>r.id} serverPagination={{page:1,pageSize:25,total:27,onPageChange:change,onPageSizeChange:vi.fn()}} columns={[{key:"value",header:"قيمة الاشتراك",primary:true,onSort:sort,sortDirection:"descending",render:r=>r.id}]}/>);
  expect(screen.getByRole("cell",{name:"a"})).toBeInTheDocument();expect(screen.getByRole("columnheader")).toHaveAttribute("aria-sort","descending");fireEvent.click(screen.getByRole("button",{name:/قيمة الاشتراك/}));expect(sort).toHaveBeenCalledOnce();fireEvent.click(screen.getByRole("button",{name:"الصفحة السابقة"}));expect(change).toHaveBeenCalledWith(0);
});
it("guardian table separates phone, children and associated value and requests server sorting",async()=>{
  const fetcher=vi.fn(async(path)=>Response.json(String(path).includes("options")?{branches:[],sports:[]}:{items:[{id:"g",displayName:"ولي أمر اصطناعي",contactPhone:"01099900101",linkedChildren:2,monthlySubscriptionValue:2000,unpricedSubscriptions:0,currency:"EGP"}],total:1}));global.fetch=fetcher as typeof fetch;
  render(<GuardiansPage/>);await screen.findByText("ولي أمر اصطناعي");expect(screen.getByRole("columnheader",{name:"رقم الهاتف"})).toBeInTheDocument();expect(screen.getByRole("columnheader",{name:"عدد الأبناء"})).toBeInTheDocument();fireEvent.change(screen.getByLabelText("ترتيب أولياء الأمور"),{target:{value:"value_desc"}});await waitFor(()=>expect(fetcher.mock.calls.some(([p])=>String(p).includes("sort=value_desc")&&String(p).includes("page=1"))).toBe(true));
});
it("VIP shows actual returned values and no early reward action",async()=>{
  global.fetch=vi.fn(async()=>Response.json({items:[{id:"g",displayName:"أسرة اصطناعية",monthlySubscriptionValue:3000,currency:"SAR"}]})) as typeof fetch;
  render(<VipMembers/>);expect(await screen.findByRole("link",{name:"أسرة اصطناعية"})).toHaveAttribute("href","/dashboard/guardians/g");expect(screen.getByRole("link",{name:"عرض الكل"})).toHaveAttribute("href",expect.stringContaining("sort=value_desc"));expect(screen.queryByRole("button",{name:"إرسال مكافأة"})).not.toBeInTheDocument();
});
