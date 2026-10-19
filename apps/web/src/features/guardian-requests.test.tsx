import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GuardianFeedbackForm } from "./guardian-feedback-form";
import { GuardianFeedbackInbox } from "./guardian-feedback";
import { OverdueFeedbackReview } from "./overdue-feedback-evidence";
import FeedbackPage from "../app/dashboard/feedback/page";
vi.mock("next/navigation", () => ({usePathname:()=>"/guardian",useRouter:()=>({push:vi.fn()})}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it("server page preserves a request deep link instead of silently resetting the filter",async()=>{
  const page=await FeedbackPage({searchParams:Promise.resolve({kind:"Request",status:"Unresolved"})});expect(page.props.initialKind).toBe("Request");expect(page.props.initialStatus).toBe("Unresolved");
});
it("family submits a request without inventing a payment or rating and retries the same intent",async()=>{
  const sent:{body:unknown;key:string}[]=[];
  global.fetch=vi.fn(async(path,init)=>{
    if(String(path).endsWith("/csrf"))return Response.json({token:"qa"});
    if(init?.method==="POST"){sent.push({body:JSON.parse(String(init.body)),key:(init.headers as Record<string,string>)["Idempotency-Key"]});return sent.length===1?Response.json({message:"تعذر الإرسال مؤقتًا"},{status:503}):Response.json({id:"r"});}
    return Response.json({asOfDate:"2026-10-19",groups:[{id:"g",name:"مجموعة اصطناعية",coaches:[]}],mine:[]});
  }) as typeof fetch;
  render(<GuardianFeedbackForm playerId="child"/>);await screen.findByRole("option",{name:"مجموعة اصطناعية"});
  fireEvent.change(screen.getByLabelText("المجموعة"),{target:{value:"g"}});fireEvent.change(screen.getByLabelText("نوع الملاحظة"),{target:{value:"Request"}});
  expect(screen.getByRole("note")).toHaveTextContent("لا ينفّذ دفعًا");expect(screen.queryByLabelText("التقييم")).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("اكتب الملاحظة"),{target:{value:"طلب توضيح مواعيد الطفل من الإدارة"}});
  fireEvent.click(screen.getByRole("button",{name:"إرسال للإدارة"}));await screen.findByText("تعذر الإرسال مؤقتًا");fireEvent.click(screen.getByRole("button",{name:"إرسال للإدارة"}));await screen.findByText("وصلت ملاحظتك لإدارة الأكاديمية.");
  expect(sent).toHaveLength(2);expect(sent[0]).toEqual(sent[1]);expect(sent[0].body).toMatchObject({kind:"Request",score:null,coachMembershipId:null,trainingGroupId:"g"});
});
it("owner filters requests separately, clear restores all kinds, and overdue evidence counts requests explicitly",async()=>{
  const fetcher=vi.fn(async(path)=>Response.json(String(path).includes("options")?{branches:[]}:[]));global.fetch=fetcher as typeof fetch;
  render(<GuardianFeedbackInbox initialKind="Request"/>);await waitFor(()=>expect(fetcher).toHaveBeenCalledWith(expect.stringContaining("kind=Request"),expect.objectContaining({signal:expect.any(AbortSignal)})));
  expect(screen.getByLabelText("النوع")).toHaveValue("Request");fireEvent.click(screen.getByRole("button",{name:"مسح"}));await waitFor(()=>expect(screen.getByLabelText("النوع")).toHaveValue(""));
  render(<OverdueFeedbackReview data={{complaintCount:0,suggestionCount:0,requestCount:2,submitterCount:1,oldestDueOn:"2026-10-10",asOfDate:"2026-10-19"}}/>);
  expect(screen.getByLabelText("ملخص الملاحظات المتأخرة")).toHaveTextContent("2 طلب");expect(screen.getByLabelText("ملخص الملاحظات المتأخرة")).toHaveTextContent("1 حساب مقدم فريد");
});
