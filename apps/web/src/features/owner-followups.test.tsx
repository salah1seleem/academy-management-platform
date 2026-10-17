import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { OwnerFollowUps, OwnerFollowUpDetail } from "./owner-followups";
import { DateFilterDefaults } from "../components/date-filter-defaults";
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const item = { id: "task", version: 42, state: "Closed", condition: "GroupLowOccupancy", conditionActive: true, assignedToMembershipId: null, dueOn: null, note: "", resolutionReason: "متابعة بشرية فقط", sourceUrl: "/dashboard/academy/groups/group", evidence: { observedAtUtc: "2026-10-17T02:30:00Z" }, source: { groupId: "group", name: "مجموعة اختبار", branch: "فرع اختبار", enrollments: 3, capacity: 20 } };
const options = { owners: [{ id: "owner", name: "مالك اصطناعي" }], branches: [{id:"branch",name:"فرع اختبار"}], candidates: [{ source: item.source, condition: item.condition, activeFollowUpId: "task" }], rule: "تسجيلات مسندة وليست قيمة ربح" };
it("coverage evidence identifies the real session and does not call the coach absent or deduct salary",async()=>{
  const covered={...item,condition:"GroupSessionCoverage",source:{...item.source,coverageGaps:[{id:"real-session",sessionDate:"2026-10-18",startTime:"17:00:00",endTime:"18:00:00"}]}};
  global.fetch=vi.fn(async()=>Response.json({item:covered,owners:options.owners,history:[]})) as typeof fetch;render(<OwnerFollowUpDetail id="task"/>);
  expect(await screen.findByRole("link",{name:/مراجعة الحصة/})).toHaveAttribute("href","/dashboard/attendance/sessions/real-session/staff");expect(screen.getByText(/ليست غيابًا مسجلًا أو خصمًا/)).toBeVisible();expect(screen.getByRole("status")).toHaveTextContent("إغلاق المتابعة لا يحلها تلقائيًا");
});
it("defaults to month-to-date and clearing removes even the default dates, with exact source and existing task links", async () => {
  global.fetch = vi.fn(async input => Response.json(String(input).endsWith("options") ? options : { items: [item], hasMore: false })) as typeof fetch;
  render(<DateFilterDefaults range={{from:"2026-10-01",to:"2026-10-17"}}><OwnerFollowUps /></DateFilterDefaults>);
  expect(await screen.findByRole("link",{name:"فتح المجموعة نفسها"})).toHaveAttribute("href",item.sourceUrl);
  expect(await screen.findByRole("link",{name:"فتح المتابعة القائمة"})).toHaveAttribute("href","/dashboard/operations/follow-ups/task");
  fireEvent.click(screen.getByRole("button",{name:"مسح"}));
  await waitFor(()=>expect(vi.mocked(fetch).mock.calls.some(([url])=>String(url).includes("allDates=true")&&!String(url).includes("from="))).toBe(true));
  expect(screen.getByLabelText("من تاريخ")).toHaveValue(""); expect(screen.getByRole("status")).toHaveTextContent("الحالة الأصلية ما زالت قائمة");
});
it("closing is a documented human action, not a resolved source or a settlement", async () => {
  global.fetch = vi.fn(async ()=>Response.json({item,owners:options.owners,history:[{id:"event",actorName:"مالك اصطناعي",createdAtUtc:"2026-10-17T02:30:00Z",after:JSON.stringify({State:"Closed",ResolutionReason:"متابعة بشرية فقط"})}]})) as typeof fetch;
  render(<OwnerFollowUpDetail id="task" />);
  expect(await screen.findByLabelText("سبب إغلاق أو رفض المتابعة")).toBeRequired();
  expect(screen.getByRole("status")).toHaveTextContent("إغلاق المتابعة لا يحلها تلقائيًا");
  fireEvent.change(screen.getByLabelText("حالة المتابعة"),{target:{value:"Assigned"}});expect(screen.getByLabelText("المالك المسؤول")).toBeRequired();
  expect(screen.queryByLabelText("سبب إغلاق أو رفض المتابعة")).not.toBeInTheDocument();
});
it("does not manufacture success when the owner endpoint is forbidden", async ()=> {
  global.fetch=vi.fn(async()=>Response.json({},{status:403})) as typeof fetch;render(<OwnerFollowUps />);
  expect(await screen.findByRole("alert")).toHaveTextContent("للمالك فقط");expect(screen.queryByRole("button",{name:"إنشاء متابعة"})).not.toBeInTheDocument();
});
it("narrows current source candidates to the selected branch too", async ()=>{
  const o={...options,branches:[{id:"branch",name:"فرع أول"},{id:"other",name:"فرع ثانٍ"}],candidates:[{source:{...item.source,branchId:"branch"},condition:item.condition,activeFollowUpId:"task"},{source:{...item.source,groupId:"other-group",branchId:"other"},condition:item.condition,activeFollowUpId:null}]};
  global.fetch=vi.fn(async input=>Response.json(String(input).endsWith("options")?o:{items:[],hasMore:false})) as typeof fetch;render(<OwnerFollowUps />);
  expect(await screen.findByRole("button",{name:"إنشاء متابعة"})).toBeVisible();fireEvent.change(screen.getByLabelText("الفرع"),{target:{value:"branch"}});
  expect(screen.queryByRole("button",{name:"إنشاء متابعة"})).not.toBeInTheDocument();expect(screen.getByRole("link",{name:"فتح المتابعة القائمة"})).toBeVisible();
});
