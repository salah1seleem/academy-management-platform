import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { SubscriptionHome } from "./subscription-home";
import { OperationalAdminHome } from "./operational-admin-home";
const overview={academy:"أكاديمية خيالية",asOf:"2026-10-25",total:12,active:8,expiring:3,expired:4,players:10,dimensions:["packages","groups","days","ages"],branches:[{id:"b",name:"فرع واحد"}],sports:[{id:"s",name:"سباحة",kind:"Swimming"}]};
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it("an invalid overview response is an actionable error instead of a zero total or a render crash",async()=>{
 vi.stubGlobal("fetch",vi.fn(async()=>Response.json({})));
 render(<SubscriptionHome/>);
 expect(await screen.findByRole("button",{name:"إعادة تحميل الاشتراكات"})).toBeVisible();
 expect(screen.queryByText("إجمالي التسجيلات")).toBeNull();
});
it("requests only the chosen breakdown and preserves filters in real list links",async()=>{
 const fetcher=vi.fn(async(url:unknown)=>Response.json(String(url).includes("/breakdown")?{items:[{key:"p",name:"باقة الحصص",total:12,detail:"12 حصة · جماعي"}]}:overview));vi.stubGlobal("fetch",fetcher);
 render(<SubscriptionHome branchId="b" asOf="2026-10-25"/>);await screen.findByText("إجمالي التسجيلات");expect(fetcher.mock.calls.filter(([u])=>String(u).includes("/breakdown"))).toHaveLength(0);
 expect(screen.queryByRole("button",{name:"الفروع"})).toBeNull();fireEvent.click(screen.getByRole("button",{name:"الباقات"}));await screen.findByText("باقة الحصص");expect(fetcher.mock.calls.filter(([u])=>String(u).includes("/breakdown"))).toHaveLength(1);
 const link=screen.getByRole("link",{name:/باقة الحصص/});expect(link.getAttribute("href")).toContain("branchId=b");expect(link.getAttribute("href")).toContain("asOf=2026-10-25");expect(link.getAttribute("href")).toContain("dimension=packages");
});
it("a scoped administrator sees ready subscriptions while payment data is pending, with no financial reads",async()=>{
 let release:((value:Response)=>void)|undefined;const payment=new Promise<Response>(resolve=>release=resolve);
 const fetcher=vi.fn(async(url:unknown)=>{const path=String(url);if(path==="/api/v1/my-permissions")return Response.json(["subscription.read","payment.read"]);if(path==="/api/v1/my-access")return Response.json({useActionGrants:true,actions:[{module:"subscriptions",read:true},{module:"payments",read:true}],workflowActions:[]});if(path.includes("payment-requests"))return payment;if(path.includes("/subscriptions/home"))return Response.json(overview);throw Error("unexpected private source");});vi.stubGlobal("fetch",fetcher);
 render(<OperationalAdminHome/>);await screen.findByText("اشتراكات نطاقك");expect(screen.queryByText("مراجعة الطلبات")).toBeNull();expect(fetcher.mock.calls.some(([u])=>String(u).includes("/reports/"))).toBe(false);release!(Response.json({pending:7}));await screen.findByText("مراجعة الطلبات");await waitFor(()=>expect(screen.getByText(/7 طلب معلق/)).toBeTruthy());
});
