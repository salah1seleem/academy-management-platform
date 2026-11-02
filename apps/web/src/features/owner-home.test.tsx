import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { DonutChart, PremiumOwnerDashboard } from "./owner-home";
import { AcademySettings } from "./academy-settings";
const incoming=new URLSearchParams();
vi.mock("next/navigation",()=>({useSearchParams:()=>incoming,usePathname:()=>"/dashboard/settings",useRouter:()=>({replace:vi.fn()})}));
afterEach(()=>{cleanup();for(const key of [...incoming.keys()])incoming.delete(key);});
const response=(v:unknown)=>Promise.resolve(new Response(JSON.stringify(v),{headers:{"Content-Type":"application/json"}}));
const summary={from:"2026-09-20",to:"2026-10-20",localToday:"2026-10-20",collectionDay:"2026-10-20",collectionMonthFrom:"2026-10-01",currency:"EGP",collections:{today:1800,month:5600,todayCash:800,todayOnline:1000,monthCash:1600,monthOnline:4000,todayUnspecified:0,monthUnspecified:0},subscriptions:{total:30,active:20,expiring:5,expired:10,branches:[{id:"b1",name:"فرع اصطناعي",count:30}]},paymentRequests:{total:4,newEnrollments:1,renewals:1,products:1,tournaments:1},operations:{coachPerformance:[],groupAttention:[],openSafetyFeedback:0}};
const cockpit={currency:"EGP",collections:{net:5600,changePercentage:12},target:{amount:null,achievement:null},overdue:{amount:43000,families:32},risk:{players:10},attendance:{percentage:80},capacity:{seats:90},followups:{overdue:0,unassigned:0}};
const statement={currency:"EGP",surplus:3500,income:[{key:"subscription",label:"الاشتراكات",amount:5600,sourceUrl:"/dashboard/reports/financial?collectionType=Subscription"}],expenses:[{key:"Rent",label:"إيجار المنشأة",amount:2100,sourceUrl:"/dashboard/finance/operating-expenses"}],totalIncome:5600,totalExpenses:2100};
function source(s:unknown=summary,c:unknown=cockpit){const fn=vi.fn((url:unknown)=>{const path=String(url);
 if(path.endsWith("/me"))return response({role:"AcademyOwner"});
 if(path.includes("/academy/experience"))return response({displayName:"أكاديميتي",logoUrl:null});
 if(path.includes("/academy/calendar"))return response({today:"2026-10-20",trailingMonthFrom:"2026-09-20"});
 if(path.includes("/structure/options"))return response({branches:[{id:"b1",arabicName:"فرع اصطناعي"}]});
 if(path.startsWith("/api/v1/subscriptions/home/breakdown")){const q=new URL(path,"http://localhost").searchParams;const v=s as typeof summary&{subscriptions:{trend?:{month:string;asOf:string;count:number}[]}};return response({items:q.get("dimension")==="months"?(v.subscriptions.trend??[]).map(p=>({key:p.asOf,name:p.asOf,total:p.count})):[]});}
 if(path.startsWith("/api/v1/subscriptions/home?"))return response({...((s as typeof summary).subscriptions),academy:"أكاديميتي",asOf:"2026-10-20",players:24,dimensions:["packages","groups","days","ages","months"],branches:[{id:"b1",name:"فرع اصطناعي"}],sports:[{id:"s",name:"كرة القدم",kind:"Football"}]});
 if(path.includes("owner-summary"))return response(s);
 if(path.includes("owner-cockpit"))return response(c);
 if(path.includes("operating-statement"))return response(statement);
 if(path.includes("vip-members")||path.includes("featured-players"))return response({items:[]});
 if(path.includes("/notifications"))return response({items:[],unread:1,total:1});
 throw Error("Unexpected source "+path);
 });global.fetch=fn as typeof fetch;return fn;}
it('weekly refunds and month subscription points retain their own dates and the selected branch',async()=>{
 incoming.set('branchId','b1');
 source({...summary,subscriptions:{...summary.subscriptions,trend:[{month:'2026-05-01',asOf:'2026-05-31',count:20},{month:'2026-10-01',asOf:'2026-10-20',count:30}]}},{...cockpit,collections:{...cockpit.collections,weekly:[{from:'2026-10-01',to:'2026-10-07',amount:-120.5},{from:'2026-10-08',to:'2026-10-14',amount:0}]}});
 render(<PremiumOwnerDashboard/>);
 const weekly=await screen.findByRole('group',{name:'التحصيل الأسبوعي'});
 expect(within(weekly).getByRole('link',{name:/2026-10-01.*-120.5/})).toHaveAttribute('href','/dashboard/reports/financial?from=2026-10-01&to=2026-10-07&branchId=b1');
 fireEvent.click(screen.getByRole("button",{name:"نمو الاشتراكات"}));
 await screen.findByRole("group",{name:"نمو الاشتراكات"});
 const monthly=screen.getByRole('group',{name:'نمو الاشتراكات'});
 expect(within(monthly).getByRole('link',{name:/2026-05-31/})).toHaveAttribute('href',expect.stringContaining('dimension=months&key=2026-05-31'));
 expect(within(monthly).queryByText(/ج.م/)).not.toBeInTheDocument();
 expect(weekly.querySelector('svg')?.outerHTML).not.toMatch(/NaN|Infinity/);
 for(const chart of [weekly]) {
  expect(chart.querySelector('polyline')?.getAttribute('points')?.split(' ')[0]).toMatch(/^0,/);
  expect(chart.querySelector('a')?.parentElement).toHaveAttribute('dir','ltr');
  expect(chart.querySelector('small > span')).toHaveAttribute('dir','ltr');
  expect(chart.querySelector('polygon')).toBeInTheDocument();
 }
 const coordinates=weekly.querySelector('polyline')!.getAttribute('points')!.split(' ').map(p=>p.split(',').map(Number));
 expect(coordinates[0][1]).toBeGreaterThan(coordinates[1][1]);
 expect(weekly.querySelector('circle')).toHaveAttribute('cx','100');
 expect(monthly.querySelector('circle')).not.toBeInTheDocument();
});
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
 source(summary,{...cockpit,target:{amount:50000,achievement:11.2}});render(<PremiumOwnerDashboard/>);expect(await screen.findByLabelText("تحقيق هدف التحصيل")).toHaveAttribute("aria-valuenow","11.2");
 expect(screen.getByRole("button",{name:/ضبط هدف التحصيل/})).toBeEnabled();
});
it("new home has no duplicated welcome/latest/payroll blocks and keeps subscription/report drilldowns",async()=>{
 source();render(<PremiumOwnerDashboard/>);expect(await screen.findByRole("heading",{name:"اشتراكات الأكاديمية"})).toBeVisible();
 expect(screen.getByRole("link",{name:/فعالة/})).toHaveAttribute("href",expect.stringContaining("state=active"));
 expect(screen.getByRole("link",{name:/بطولات/})).toHaveAttribute("href",expect.stringContaining("type=Tournament"));
 expect(screen.queryByText("أحدث التحصيلات")).not.toBeInTheDocument();expect(screen.queryByText(/أهلًا/)).not.toBeInTheDocument();expect(screen.queryByText("مرجع التحليل")).not.toBeInTheDocument();
 expect(screen.queryByLabelText("تحقيق هدف التحصيل")).not.toBeInTheDocument();expect(screen.queryByText(/لم يحدد هدف/)).not.toBeInTheDocument();
 fireEvent.click(screen.getByRole("button",{name:"مسح الفلاتر"}));expect(screen.getByLabelText("من تاريخ")).toHaveValue("");expect(screen.getByLabelText("إلى تاريخ")).toHaveValue("");
});
it("does not invent a pie when there are no stored records",()=>{
 render(<DonutChart center="0" caption="لاعبًا" rows={[{name:"فعال",value:0,href:"/players"}]}/>);expect(screen.queryByRole("img")).not.toBeInTheDocument();expect(screen.getByText("لا توجد سجلات ضمن الفلاتر الحالية.")).toBeVisible();
});
it("keeps quick actions and branch subscription counts without a duplicated status pie",async()=>{
 const coaches=[
  {id:"c1",name:"مدرب التقييم الأعلى",branches:["b1"],sports:["كرة القدم"],groupCount:1,sessionCount:9,parentRating:5,parentRatingCount:2},
  {id:"c2",name:"مدرب المجموعات الأكثر",branches:["b1"],sports:["كرة القدم"],groupCount:4,sessionCount:6,parentRating:null,parentRatingCount:0},
  {id:"c3",name:"مدرب بلا إسناد",branches:[],sports:[],groupCount:0,sessionCount:0,parentRating:null,parentRatingCount:0},
 ];
 source({...summary,operations:{...summary.operations,coachPerformance:coaches}});render(<PremiumOwnerDashboard/>);
 const section=await screen.findByRole('region',{name:'المدربون'});
 const records=()=>within(section).getAllByRole('link').filter(l=>l.getAttribute('href')?.includes('/academy/coaches/c'));
 expect(records().map(r=>r.textContent)).toEqual([expect.stringContaining('مدرب التقييم الأعلى'),expect.stringContaining('مدرب المجموعات الأكثر'),expect.stringContaining('مدرب بلا إسناد')]);
 fireEvent.click(within(section).getByRole('button',{name:'حسب المجموعات المسندة'}));
 expect(records()[0]).toHaveTextContent('مدرب المجموعات الأكثر');
 expect(within(section).getByRole('link',{name:'كل المدربين'})).toHaveAttribute('href',expect.stringContaining('sort=groups'));
 expect(screen.queryByRole('table',{name:'ترتيب المدربين حسب عدد المجموعات المسندة'})).not.toBeInTheDocument();
 const shortcuts=screen.getByRole("navigation",{name:"الإجراءات التشغيلية السريعة"});
 expect(within(shortcuts).getByRole("link",{name:/تسجيل لاعب/})).toHaveAttribute("href","/dashboard/players/new");
 expect(within(shortcuts).getByRole("link",{name:/تجديد اشتراك/})).toHaveAttribute("href","/dashboard/subscriptions/renew/new");
 expect(screen.queryByRole("region",{name:"الرسوم الدائرية"})).not.toBeInTheDocument();
 expect(screen.getByRole("region",{name:"التحصيلات ومصادرها"})).toBeVisible();
 expect(screen.queryByRole("img",{name:/فعالة: 20، منتهية: 10/})).not.toBeInTheDocument();
 expect(screen.queryByRole('button',{name:'الفروع'})).not.toBeInTheDocument();
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
  incoming.set("tab","operations");
  global.fetch = vi.fn(input => String(input).includes("experience") ? response({ displayName: "أكاديمية اختبار", platformBrandOperator: false, coachAttendanceEnabled: true }) : String(input).includes("my-permissions") ? response(["academy.settings.manage"]) : String(input).endsWith("/me") ? response({ role: "AcademyOwner" }) : response({ configured: false })) as typeof fetch;
  render(<AcademySettings />); expect(await screen.findByRole("checkbox")).toBeVisible(); expect(screen.queryByText("هوية الأكاديميات — مشغّل المنصة")).not.toBeInTheDocument(); expect(screen.queryByLabelText("رفع شعار")).not.toBeInTheDocument();
});

it("collection source and branch drilldowns keep real periods and switch together",async()=>{
 const collections={...summary.collections,monthBranches:[{id:'b1',name:'فرع التحصيل',amount:5600,count:3}],todayBranches:[{id:'b1',name:'فرع التحصيل',amount:1800,count:1}],monthSources:[{key:'Enrollment',label:'اشتراكات جديدة',amount:5600,count:3,collectionType:'Subscription',subscriptionKind:'Enrollment'}],todaySources:[{key:'Refund',label:'استردادات الاشتراكات',amount:-40,count:1,collectionType:'Refund'}]};
 source({...summary,collections});render(<PremiumOwnerDashboard/>);
 const section=await screen.findByRole('region',{name:'التحصيلات ومصادرها'});
 expect(within(section).getByText('2026-10-01 — 2026-10-20')).toHaveAttribute('dir','ltr');
 expect(within(section).getByRole('link',{name:/اشتراكات جديدة/})).toHaveAttribute('href',expect.stringContaining('from=2026-10-01&to=2026-10-20'));
 expect(within(section).getByRole('link',{name:/اشتراكات جديدة/})).toHaveAttribute('href',expect.stringContaining('subscriptionKind=Enrollment'));
 expect(within(section).getByRole('link',{name:/فرع التحصيل/})).toHaveAttribute('href',expect.stringContaining('branchId=b1'));
 fireEvent.click(within(section).getByRole('button',{name:'اليوم'}));
 expect(within(section).getByRole('link',{name:/استردادات الاشتراكات/})).toHaveAttribute('href',expect.stringContaining('from=2026-10-20&to=2026-10-20&collectionType=Refund'));
 expect(within(section).queryByRole('link',{name:/اشتراكات جديدة/})).not.toBeInTheDocument();
});
it('renders ready Owner sections during a delayed statement and retries only a failed source',async()=>{
 const base=source();let finish:(value:Response)=>void=()=>{};let statementReads=0;
 global.fetch=vi.fn((input:RequestInfo|URL)=>{
  if(String(input).includes('operating-statement')){statementReads++;return statementReads===1?new Promise<Response>(resolve=>{finish=resolve;}):response(statement);}
  return base(input);
 }) as typeof fetch;
 render(<PremiumOwnerDashboard/>);
 await screen.findByRole('heading',{name:'ملخص الأكاديمية'});
 expect(screen.getByRole('heading',{name:'التحصيلات'})).toBeInTheDocument();
 expect(screen.getByRole('status',{name:'جارٍ تحميل البيان المالي'})).toBeInTheDocument();
 finish(new Response('{}',{status:503}));
 const retry=await screen.findByRole('button',{name:'إعادة تحميل البيان المالي'});
 const summaryReads=base.mock.calls.filter(([path])=>String(path).includes('owner-summary')).length;
 fireEvent.click(retry);
 await screen.findByRole('heading',{name:'تقرير الأرباح والخسائر'});
 expect(base.mock.calls.filter(([path])=>String(path).includes('owner-summary'))).toHaveLength(summaryReads);
 expect(statementReads).toBe(2);
});
