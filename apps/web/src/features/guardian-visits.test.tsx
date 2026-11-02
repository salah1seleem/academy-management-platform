import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GuardianVisits } from "./guardian-visits";
vi.mock("../components/guardian-shell",()=>({GuardianShell:({children}:{children:React.ReactNode})=><>{children}</>}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it("shows linked-child visit state without attendance or checkout controls",async()=>{
  global.fetch=vi.fn(async()=>new Response(JSON.stringify({visits:[{id:"v",facility:"مرفق اصطناعي",resource:"A",enteredAtUtc:"2026-10-12T08:00:00Z",staleOpen:true}]}),{status:200})) as typeof fetch;
  render(<GuardianVisits playerId="p"/>);await screen.findByText("مرفق اصطناعي · A");expect(screen.getByText(/بانتظار مراجعة الأكاديمية/)).toBeVisible();expect(screen.queryByRole("button")).not.toBeInTheDocument();expect(screen.getByText(/مستقل عن حضور الحصة/)).toBeVisible();
});
it("does not show an empty success state when child-history authorization fails",async()=>{
  global.fetch=vi.fn(async()=>new Response("{}",{status:404})) as typeof fetch;render(<GuardianVisits playerId="other"/>);await screen.findByText("تعذر تحميل سجل زيارات الطفل.");expect(screen.queryByText("لا توجد زيارات مسجلة للطفل.")).not.toBeInTheDocument();
});
