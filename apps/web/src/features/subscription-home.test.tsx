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
 expect(screen.queryByText("إجمالي الاشتراكات السارية")).toBeNull();
});
it("requests only the chosen breakdown and preserves filters in real list links",async()=>{
 const fetcher=vi.fn(async(url:unknown)=>Response.json(String(url).includes("/breakdown")?{items:[{key:"p",name:"باقة الحصص",total:12,detail:"12 حصة · جماعي"}]}:overview));vi.stubGlobal("fetch",fetcher);
 render(<SubscriptionHome branchId="b" asOf="2026-10-25"/>);await screen.findByText("إجمالي الاشتراكات السارية");expect(fetcher.mock.calls.filter(([u])=>String(u).includes("/breakdown"))).toHaveLength(0);
 expect(screen.queryByRole("button",{name:"الفروع"})).toBeNull();fireEvent.click(screen.getByRole("button",{name:"الباقات"}));await screen.findByText("باقة الحصص");expect(fetcher.mock.calls.filter(([u])=>String(u).includes("/breakdown"))).toHaveLength(1);
 const link=screen.getByRole("link",{name:/باقة الحصص/});expect(link.getAttribute("href")).toContain("branchId=b");expect(link.getAttribute("href")).toContain("asOf=2026-10-25");expect(link.getAttribute("href")).toContain("dimension=packages");
});
it("links active counts, unique players and cumulative history to the matching scope", async () => {
  vi.stubGlobal("fetch", vi.fn(async url => Response.json(String(url).includes("/breakdown")
    ? { items: [{ key: "2026-09-30", name: "سبتمبر", total: 10 }] }
    : { ...overview, dimensions: ["months"] })));
  render(<SubscriptionHome branchId="b" from="2026-10-01" asOf="2026-10-25" />);
  const active = await screen.findByRole("link", { name: "إجمالي الاشتراكات السارية، ٨" });
  expect(active).toHaveAttribute("href", expect.stringContaining("from=2026-10-01&state=active"));
  expect(screen.getByRole("link", { name: "لا تنتهي قريبًا، ٥" })).toHaveAttribute("href", expect.stringContaining("state=current"));
  expect(screen.getByRole("link", { name: "تنتهي قريبًا، ٣" })).toHaveAttribute("href", expect.stringContaining("state=expiring"));
  expect(screen.getByRole("link", { name: "قائمة اللاعبين دون تكرار، ١٠" })).toHaveAttribute("href", expect.stringContaining("state=all&uniquePlayers=true"));
  fireEvent.click(screen.getByRole("button", { name: "نمو الاشتراكات" }));
  const point = await screen.findByRole("link", { name: /سبتمبر.*١٠/ });
  expect(point).toHaveAttribute("href", "/dashboard/players/subscriptions?branchId=b&asOf=2026-10-25&state=all&dimension=months&key=2026-09-30");
});
it("a scoped administrator sees ready subscriptions while payment data is pending, with no financial reads",async()=>{
 let release:((value:Response)=>void)|undefined;const payment=new Promise<Response>(resolve=>release=resolve);
 const fetcher=vi.fn(async(url:unknown)=>{const path=String(url);if(path==="/api/v1/my-permissions")return Response.json(["subscription.read","payment.read"]);if(path==="/api/v1/my-access")return Response.json({useActionGrants:true,actions:[{module:"subscriptions",read:true},{module:"payments",read:true}],workflowActions:[]});if(path.includes("payment-requests"))return payment;if(path.includes("/subscriptions/home"))return Response.json(overview);throw Error("unexpected private source");});vi.stubGlobal("fetch",fetcher);
 render(<OperationalAdminHome/>);await screen.findByText("اشتراكات نطاقك");expect(screen.queryByText("مراجعة الطلبات")).toBeNull();expect(fetcher.mock.calls.some(([u])=>String(u).includes("/reports/"))).toBe(false);release!(Response.json({pending:7}));await screen.findByText("مراجعة الطلبات");await waitFor(()=>expect(screen.getByText("بانتظار المراجعة: 7")).toBeVisible());
});

it("shows a payment load error separately from a real empty queue", async () => {
  let payments = 0;
  vi.stubGlobal("fetch", vi.fn(async (url: unknown) => {
    const path = String(url);
    if (path === "/api/v1/my-permissions") return Response.json(["payment.read"]);
    if (path === "/api/v1/my-access") return Response.json({ useActionGrants: false });
    if (path.includes("payment-requests")) return Response.json(++payments === 1 ? {} : { pending: 0 });
    return Response.json([]);
  }));
  render(<OperationalAdminHome />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل طلبات الدفع.");
  expect(screen.queryByText("لا توجد طلبات دفع معلقة.")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل الطلبات" }));
  expect(await screen.findByText("لا توجد طلبات دفع معلقة.")).toBeVisible();
  expect(screen.queryByRole("link", { name: "مراجعة الطلبات" })).not.toBeInTheDocument();
});

it.each([true, false])("loads product sizes on demand for an authorized administrator (action grants: %s)", async (useActionGrants) => {
  const fetcher = vi.fn(async (url: unknown) => {
    const path = String(url);
    if (path === "/api/v1/my-permissions") return Response.json(["content.catalog.manage"]);
    if (path === "/api/v1/my-access") return Response.json({ useActionGrants, ...(useActionGrants && { actions: [{ module: "catalog", read: true }] }) });
    if (path === "/api/v1/my-match-access") return Response.json({ canRead: false });
    if (path === "/api/v1/news") return Response.json([]);
    if (path === "/api/v1/content/catalog/?active=true") return Response.json([{ id: "shirt", arabicName: "طقم خيالي", stockQuantity: 7, usesVariants: true }]);
    if (path === "/api/v1/content/catalog/shirt/inventory") return Response.json({
      stockQuantity: 7,
      variants: [{ id: "m", size: "M", color: "أحمر", quantity: 3 }, { id: "l", size: "L", color: "أحمر", quantity: 4 }],
    });
    throw new Error("unexpected source");
  });
  vi.stubGlobal("fetch", fetcher);
  render(<OperationalAdminHome />);
  expect(await screen.findByText("المتبقي 7")).toBeVisible();
  expect(fetcher.mock.calls.some(([url]) => String(url).endsWith("/inventory"))).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "المتبقي حسب المقاس" }));
  expect(await screen.findByText("أحمر · مقاس M")).toBeVisible();
  expect(screen.getByText("3")).toBeVisible();
  expect(screen.getByText("4")).toBeVisible();
});
