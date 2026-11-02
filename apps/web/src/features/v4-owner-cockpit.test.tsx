import {cleanup,render,screen} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {OwnerCockpit} from "./owner-cockpit";
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const fixture={currency:"EGP",collections:{net:248000,previous:200000,previousFrom:"2026-08-01",previousTo:"2026-08-28",changePercentage:24,comparable:true},target:{amount:320000,achievement:77.5,basis:"هدف معتمد"},overdue:{amount:1200,families:2,asOf:"2026-09-28"},risk:{players:3,asOf:"2026-09-28",basis:"تأخر تجديد مثبت، لا مغادرة مؤكدة"},attendance:{percentage:80,recorded:10,attended:8,notRecorded:2},capacity:{seats:4,unknownGroups:1,unpricedGroups:1,conditionalValue:3600,items:[]}};
it("uses real comparison and target response with scoped actionable priorities and no automatic send",async()=>{
 global.fetch=vi.fn(async()=>Response.json(fixture)) as typeof fetch;
 render(<OwnerCockpit from="2026-09-01" to="2026-09-28" branchId="b1"/>);
 expect(await screen.findByText("77.5%")).toBeVisible();expect(screen.getByRole("progressbar",{name:"تحقيق هدف التحصيل"})).toHaveAttribute("value","77.5");expect(screen.getByText("24%")).toBeVisible();
 expect(screen.getByRole("link",{name:"مراجعة كشف التحصيل للتحضير ←"})).toHaveAttribute("href",expect.stringContaining("branchId=b1&state=overdue"));expect(screen.getByRole("link",{name:"استعراض الأماكن المتاحة ←"})).toHaveAttribute("href",expect.stringContaining("branchId=b1"));
 expect(screen.getByText(/بلا إرسال أو خصم تلقائي/)).toBeVisible();expect(screen.getByText(/مجموعات بلا سعر شهري مؤهل/)).toBeVisible();expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining("from=2026-09-01&to=2026-09-28&branchId=b1"),expect.any(Object));
});
it("does not invent a goal ratio prior-period growth or attendance when samples are missing",async()=>{
 global.fetch=vi.fn(async()=>Response.json({...fixture,collections:{...fixture.collections,previous:0,changePercentage:null},target:{amount:null,achievement:null},attendance:{...fixture.attendance,percentage:null}})) as typeof fetch;
 render(<OwnerCockpit from="" to=""/>);expect(await screen.findByText(/لا هدف سارٍ/)).toBeVisible();expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();expect(screen.getByText("عينة غير كافية")).toBeVisible();expect(screen.getByText(/لا تحصيل موجب في الفترة السابقة/)).toBeVisible();expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining("allDates=true"),expect.any(Object));
});
