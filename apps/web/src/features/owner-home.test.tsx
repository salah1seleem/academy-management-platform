import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { DonutChart, PremiumOwnerDashboard } from "./owner-home";
import { AcademySettings } from "./academy-settings";
const incoming=new URLSearchParams();
vi.mock("next/navigation",()=>({useSearchParams:()=>incoming}));
afterEach(()=>{cleanup();for(const key of [...incoming.keys()])incoming.delete(key);});
const response=(v:unknown)=>Promise.resolve(new Response(JSON.stringify(v),{headers:{"Content-Type":"application/json"}}));
const summary={from:"2026-09-20",to:"2026-10-20",localToday:"2026-10-20",collectionDay:"2026-10-20",collectionMonthFrom:"2026-10-01",currency:"EGP",collections:{today:1800,month:5600,todayCash:800,todayOnline:1000,monthCash:1600,monthOnline:4000,todayUnspecified:0,monthUnspecified:0},subscriptions:{total:30,active:20,expiring:5,expired:10},paymentRequests:{total:4,newEnrollments:1,renewals:1,products:1,tournaments:1},operations:{coachPerformance:[],groupAttention:[],openSafetyFeedback:0}};
const cockpit={currency:"EGP",collections:{net:5600,changePercentage:12},target:{amount:null,achievement:null},overdue:{amount:43000,families:32},risk:{players:10},attendance:{percentage:80},capacity:{seats:90},followups:{overdue:0,unassigned:0}};
const statement={currency:"EGP",surplus:3500,income:[{key:"subscription",label:"الاشتراكات",amount:5600,sourceUrl:"/dashboard/reports/financial?collectionType=Subscription"}],expenses:[{key:"Rent",label:"إيجار المنشأة",amount:2100,sourceUrl:"/dashboard/finance/operating-expenses"}],totalIncome:5600,totalExpenses:2100};
function source(s:unknown=summary,c:unknown=cockpit){const fn=vi.fn((url:unknown)=>{const path=String(url);
 if(path.endsWith("/me"))return response({role:"AcademyOwner"});
 if(path.includes("/academy/experience"))return response({displayName:"أكاديميتي",logoUrl:null});
 if(path.includes("/academy/calendar"))return response({today:"2026-10-20",trailingMonthFrom:"2026-09-20"});
 if(path.includes("/structure/options"))return response({branches:[{id:"b1",arabicName:"فرع اصطناعي"}]});
 if(path.includes("owner-summary"))return response(s);
 if(path.includes("owner-cockpit"))return response(c);
 if(path.includes("operating-statement"))return response(statement);
 if(path.includes("vip-members")||path.includes("featured-players"))return response({items:[]});
 if(path.includes("/notifications"))return response({items:[],unread:1,total:1});
 throw Error("Unexpected source "+path);
 });global.fetch=fn as typeof fetch;return fn;}
it("incoming date/branch scope initializes compact controls and actual source requests",async()=>{
 incoming.set("from","2026-09-01");incoming.set("to","2026-09-28");incoming.set("branchId","b1");const fetcher=source();render(<PremiumOwnerDashboard/>);
 expect(await screen.findByLabelText("من تاريخ")).toHaveValue("2026-09-01");expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("2026-09-28");
 await waitFor(()=>expect(fetcher.mock.calls.some(([p])=>String(p).includes("owner-summary?from=2026-09-01&to=2026-09-28&branchId=b1"))).toBe(true));
});
it("payment request categories and real reminder recipients remain distinct actions",async()=>{
 source();render(<PremiumOwnerDashboard/>);const renewal=await screen.findByRole("link",{name:/تجديد الاشتراك/});expect(renewal).toHaveAttribute("href",expect.stringContaining("type=Renewal"));
 expect(screen.getByRole("link",{name:/تحضير تذكيرات التحصيل/})).toHaveAttribute("href",expect.stringContaining("kind=Overdue"));
 expect(screen.getByRole("link",{name:/عرض خطة الاحتفاظ بهم/})).toHaveAttribute("href",expect.stringContaining("kind=Renewal"));
 expect(screen.queryByText(/أولوية 1/)).not.toBeInTheDocument();expect(screen.getAllByRole("heading",{level:3}).length).toBeGreaterThan(0);
});
it("monthly target is displayed only when the canonical source qualifies its scope",async()=>{
 source(summary,{...cockpit,target:{amount:50000,achievement:11.2}});render(<PremiumOwnerDashboard/>);expect(await screen.findByLabelText("تحقيق هدف التحصيل")).toHaveAttribute("value","11.2");
 expect(screen.getByRole("link",{name:/ضبط هدف التحصيل/})).toHaveAttribute("href","/dashboard/settings#collection-target");
});
it("new home has no duplicated welcome/latest/payroll blocks and keeps subscription/report drilldowns",async()=>{
 source();render(<PremiumOwnerDashboard/>);expect(await screen.findByRole("heading",{name:/^الاشتراكات$/})).toBeVisible();
 expect(screen.getByRole("heading",{name:"اشتراكات فعالة"}).closest("a")).toHaveAttribute("href",expect.stringContaining("/players/subscriptions?state=active"));
 expect(screen.getByRole("link",{name:/بطولات/})).toHaveAttribute("href",expect.stringContaining("type=Tournament"));
 expect(screen.queryByText("أحدث التحصيلات")).not.toBeInTheDocument();expect(screen.queryByText(/أهلًا/)).not.toBeInTheDocument();expect(screen.queryByText("مرجع التحليل")).not.toBeInTheDocument();
 expect(screen.queryByLabelText("تحقيق هدف التحصيل")).not.toBeInTheDocument();expect(screen.queryByText(/لم يحدد هدف/)).not.toBeInTheDocument();
 fireEvent.click(screen.getByRole("button",{name:"مسح الفلاتر"}));expect(screen.getByLabelText("من تاريخ")).toHaveValue("");expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("");
});
it("does not invent a pie when there are no stored records",()=>{
 render(<DonutChart center="0" caption="لاعبًا" rows={[{name:"فعال",value:0,href:"/players"}]}/>);expect(screen.queryByRole("img")).not.toBeInTheDocument();expect(screen.getByText("لا توجد سجلات ضمن الفلاتر الحالية.")).toBeVisible();
});
it("keeps quick actions and pies while ranking actual assignments independently of ratings",async()=>{
 const coaches=[
  {id:"c1",name:"مدرب التقييم الأعلى",branches:["b1"],sports:["كرة القدم"],groupCount:1,sessionCount:9,parentRating:5,parentRatingCount:2},
  {id:"c2",name:"مدرب المجموعات الأكثر",branches:["b1"],sports:["كرة القدم"],groupCount:4,sessionCount:6,parentRating:null,parentRatingCount:0},
  {id:"c3",name:"مدرب بلا إسناد",branches:[],sports:[],groupCount:0,sessionCount:0,parentRating:null,parentRatingCount:0},
 ];
 source({...summary,operations:{...summary.operations,coachPerformance:coaches}});render(<PremiumOwnerDashboard/>);
 const table=await screen.findByRole("table",{name:"ترتيب المدربين حسب عدد المجموعات المسندة"});
 const records=within(table).getAllByRole("row").slice(1);
 expect(records.map(r=>within(r).getByRole("rowheader").textContent)).toEqual([expect.stringContaining("مدرب المجموعات الأكثر"),expect.stringContaining("مدرب التقييم الأعلى"),expect.stringContaining("مدرب بلا إسناد")]);
 expect(within(records[0]).getByText("4")).toBeVisible();
 expect(within(records[0]).getByRole("link",{name:/ملف المدرب/})).toHaveAttribute("href",expect.stringContaining("/academy/coaches/c2?from=2026-09-20&to=2026-10-20"));
 expect(coaches.map(c=>c.id)).toEqual(["c1","c2","c3"]);
 const shortcuts=screen.getByRole("navigation",{name:"الإجراءات التشغيلية السريعة"});
 expect(within(shortcuts).getByRole("link",{name:/تسجيل لاعب/})).toHaveAttribute("href","/dashboard/players/new");
 expect(within(shortcuts).getByRole("link",{name:/تجديد اشتراك/})).toHaveAttribute("href","/dashboard/subscriptions/renew/new");
 const pies=screen.getByRole("region",{name:"الرسوم الدائرية"});
 expect(within(pies).getAllByRole("img")).toHaveLength(2);
 expect(within(pies).getByRole("img",{name:/فعالة: 20، منتهية: 10/})).toBeVisible();
});
it("shows real coach ratings and groups requiring intervention without inventing AI scores",async()=>{
 source({...summary,operations:{...summary.operations,coachPerformance:[{id:"c1",name:"محمود سليمان",branches:["b1"],sports:["كرة القدم"],groupCount:2,sessionCount:7,parentRating:4.5,parentRatingCount:2}],groupAttention:[{id:"g1",name:"مجموعة تحتاج متابعة"}]}});
 render(<PremiumOwnerDashboard/>);expect(await screen.findByText("4.5 ★ · 2 تقييمات")).toBeVisible();expect(screen.getByRole("link",{name:/محمود سليمان/})).toHaveAttribute("href",expect.stringContaining("/academy/coaches/c1"));
 expect(screen.getByRole("link",{name:/مراجعة المجموعة وفتح متابعة/})).toHaveAttribute("href",expect.stringContaining("/academy/groups/g1"));
 expect(screen.queryByText(/مؤشر AI/)).not.toBeInTheDocument();expect(screen.queryByRole("heading",{name:"المدربون وتقييم الأسر"})).not.toBeInTheDocument();
});
it("does not relabel an undocumented historical refund as cash or online",async()=>{
 source({...summary,collections:{...summary.collections,today:-40,todayCash:0,todayOnline:0,todayUnspecified:-40}});render(<PremiumOwnerDashboard/>);
 const link=await screen.findByRole("link",{name:/طريقة صرف قديمة غير موثقة/});expect(link).toHaveAttribute("href",expect.stringContaining("provider=Unspecified"));expect(link).toHaveTextContent("-40");
});
it("an owner manages attendance/transfers without receiving platform branding controls", async () => {
  global.fetch = vi.fn(input => String(input).includes("experience") ? response({ displayName: "أكاديمية اختبار", platformBrandOperator: false, coachAttendanceEnabled: true }) : String(input).includes("my-permissions") ? response(["academy.settings.manage"]) : String(input).endsWith("/me") ? response({ role: "AcademyOwner" }) : response({ configured: false })) as typeof fetch;
  render(<AcademySettings />); expect(await screen.findByRole("checkbox")).toBeVisible(); expect(screen.queryByText("هوية الأكاديميات — مشغّل المنصة")).not.toBeInTheDocument(); expect(screen.queryByLabelText("رفع شعار")).not.toBeInTheDocument();
});
