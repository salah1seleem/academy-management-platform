import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { DonutChart, PremiumOwnerDashboard } from "./owner-home";
import { AcademySettings } from "./academy-settings";
import { OperationalAdminHome } from "./operational-admin-home";
const incoming=new URLSearchParams();
vi.mock("next/navigation",()=>({useSearchParams:()=>incoming,usePathname:()=>"/dashboard/settings",useRouter:()=>({replace:vi.fn()})}));
afterEach(()=>{cleanup();for(const key of [...incoming.keys()])incoming.delete(key);window.history.replaceState(null,"","/");});
const response=(v:unknown)=>Promise.resolve(new Response(JSON.stringify(v),{headers:{"Content-Type":"application/json"}}));
function adminSource(readFollowups: () => Promise<Response>) {
 global.fetch = vi.fn(path => {
  switch (String(path)) {
   case "/api/v1/my-permissions": return response(["followups.read"]);
   case "/api/v1/my-access": return response({useActionGrants: false});
   case "/api/v1/my-followups?state=Open&allDates=true": return readFollowups();
   case "/api/v1/my-match-access": return response({canRead: false});
   case "/api/v1/news": return response([]);
   default: throw Error("Unexpected source " + String(path));
  }
 }) as typeof fetch;
}

it("distinguishes a follow-up read failure from an empty inbox", async () => {
 const read = vi.fn().mockResolvedValueOnce(Response.json({}, {status: 503})).mockImplementation(() => response({items: []}));
 adminSource(read);
 render(<OperationalAdminHome/>);
 const inbox = within(await screen.findByRole("region", {name: "متابعات مسندة إليّ"}));
 expect(await inbox.findByRole("alert")).toHaveTextContent("تعذر تحميل المتابعات.");
 expect(inbox.queryByText("لا توجد متابعات مفتوحة.")).not.toBeInTheDocument();
 fireEvent.click(inbox.getByRole("button", {name: "إعادة تحميل المتابعات"}));
 expect(await inbox.findByText("لا توجد متابعات مفتوحة.")).toBeVisible();
 expect(inbox.getByRole("link", {name: "فتح متابعاتي"})).toHaveAttribute("href", "/dashboard/my-followups?allDates=true");
});

it("previews the first three open tasks in the order returned by the server", async () => {
 adminSource(() => response({items: [1, 2, 3, 4].map(id => ({id: String(id), source: {name: `مجموعة العرض ${id}`}, condition: "GroupLowOccupancy", state: "Assigned", dueOn: "2026-09-20"}))}));
 render(<OperationalAdminHome/>);
 const inbox = within(await screen.findByRole("region", {name: "متابعات مسندة إليّ"}));
 expect(await inbox.findByRole("link", {name: "مجموعة العرض 1"})).toHaveAttribute("href", "/dashboard/my-followups/1?allDates=true");
 expect(inbox.getAllByRole("listitem")).toHaveLength(3);
 expect(inbox.queryByText("مجموعة العرض 4")).not.toBeInTheDocument();
});
const summary={from:"2026-09-20",to:"2026-10-20",localToday:"2026-10-20",collectionDay:"2026-10-20",collectionMonthFrom:"2026-10-01",currency:"EGP",collections:{today:1800,month:5600,todayCash:800,todayOnline:1000,monthCash:1600,monthOnline:4000,todayUnspecified:0,monthUnspecified:0},subscriptions:{total:30,active:20,expiring:5,expired:10,branches:[{id:"b1",name:"فرع اصطناعي",count:30}]},paymentRequests:{total:4,newEnrollments:1,renewals:1,products:1,tournaments:1},operations:{coachPerformance:[],groupAttention:[],openSafetyFeedback:0}};
const cockpit={currency:"EGP",collections:{net:5600,changePercentage:12},target:{amount:null,achievement:null},overdue:{amount:43000,families:32},risk:{players:10},attendance:{percentage:80},capacity:{seats:90},followups:{overdue:0,unassigned:0}};
const statement={currency:"EGP",surplus:3500,surplusChangePercentage:-13.2,income:[{key:"subscription",label:"الاشتراكات",amount:5600,sourceUrl:"/dashboard/reports/financial?collectionType=Subscription"}],expenses:[{key:"Rent",label:"إيجار المنشأة",amount:2100,sourceUrl:"/dashboard/finance/operating-expenses"}],totalIncome:5600,totalExpenses:2100};
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
 if(path==="/api/v1/news")return response([]);
 if(path.includes("/manage/decision-center"))return response({expiring:10,expired:2,absence:1,unassigned:1,seats:90,openTasks:0});
 throw Error("Unexpected source "+path);
 });global.fetch=fn as typeof fetch;return fn;}
it('weekly refunds and month subscription points retain their own dates and the selected branch',async()=>{
 incoming.set('branchId','b1');
 source({...summary,subscriptions:{...summary.subscriptions,trend:[{month:'2026-05-01',asOf:'2026-05-31',count:20},{month:'2026-10-01',asOf:'2026-10-20',count:30}]}},{...cockpit,collections:{...cockpit.collections,weekly:[{from:'2026-10-01',to:'2026-10-07',amount:-120.5},{from:'2026-10-08',to:'2026-10-14',amount:0}]}});
 render(<PremiumOwnerDashboard/>);
 const weekly=await screen.findByRole('group',{name:'التحصيل الأسبوعي'});
 expect(within(weekly).getByRole('link',{name:/-120.5 جنيه.*1 أكتوبر/})).toHaveAttribute('href','/dashboard/reports/financial?from=2026-10-01&to=2026-10-07&branchId=b1');
 fireEvent.click(screen.getByRole("button",{name:/تحليل الاشتراكات/}));
 fireEvent.click(screen.getByRole("button",{name:"نمو الاشتراكات"}));
 await screen.findByRole("group",{name:"نمو الاشتراكات"});
 const monthly=screen.getByRole('group',{name:'نمو الاشتراكات'});
 expect(within(monthly).getByRole('link',{name:/2026-05-31/})).toHaveAttribute('href',expect.stringContaining('dimension=months&key=2026-05-31'));
 expect(within(monthly).queryByText(/ج.م/)).not.toBeInTheDocument();
 expect(weekly.querySelector('svg')?.outerHTML).not.toMatch(/NaN|Infinity/);
 for(const chart of [weekly]) {
  expect(chart.querySelector('polyline')?.getAttribute('points')?.split(' ')[0]).toMatch(/^25,/);
  expect(chart.querySelector('a')?.parentElement).toHaveAttribute('dir','ltr');
  expect(chart.querySelector('small > span')).toHaveTextContent('آخر أسبوعين حتى 14 أكتوبر 2026');
  expect(chart.querySelector('polygon')).toBeInTheDocument();
 }
 const coordinates=weekly.querySelector('polyline')!.getAttribute('points')!.split(' ').map(p=>p.split(',').map(Number));
 expect(coordinates[0][1]).toBeGreaterThan(coordinates[1][1]);
 expect(weekly.querySelector('circle')).toHaveAttribute('cx','75');
 expect(monthly.querySelector('circle')).not.toBeInTheDocument();
});
it("incoming date/branch scope initializes compact controls and actual source requests",async()=>{
 incoming.set("from","2026-09-01");incoming.set("to","2026-09-28");incoming.set("branchId","b1");const fetcher=source();render(<PremiumOwnerDashboard/>);
 const period = within(await screen.findByRole("group",{name:"فترة الاشتراكات"}));
 fireEvent.click(period.getByRole("button",{name:"الفترة المخصصة"}));
 expect(await screen.findByLabelText("من تاريخ (الاشتراكات)")).toHaveValue("2026-09-01");expect(screen.getByLabelText("إلى تاريخ (الاشتراكات)")).toHaveValue("2026-09-28");
 await waitFor(()=>expect(fetcher.mock.calls.some(([p])=>String(p).includes("owner-summary?from=2026-09-01&to=2026-09-28&branchId=b1"))).toBe(true));
});
it("keeps a custom period as a draft until apply and restores the applied scope from the URL", async () => {
  const fetcher = source();
  const view = render(<PremiumOwnerDashboard/>);
  const scope = within(await screen.findByRole("group", { name: "فترة ملخص الأكاديمية" }));
  const today = scope.getByRole("button", { name: "هذا اليوم" });
  fireEvent.click(today);
  await waitFor(() => expect(fetcher.mock.calls.some(([path]) => String(path).includes("owner-cockpit?from=2026-10-20&to=2026-10-20"))).toBe(true));
  expect(scope.getByLabelText("الفترة المطبقة (ملخص الأكاديمية)")).toHaveTextContent("20 أكتوبر 2026");
  const custom = scope.getByRole("button", { name: "الفترة المخصصة" });
  fireEvent.click(custom);
  const from = scope.getByLabelText("من تاريخ (ملخص الأكاديمية)");
  const to = scope.getByLabelText("إلى تاريخ (ملخص الأكاديمية)");
  expect(from).toHaveFocus();
  fireEvent.change(from, { target: { value: "2026-10-16" } });
  fireEvent.change(to, { target: { value: "2026-10-10" } });
  fireEvent.click(scope.getByRole("button", { name: "تطبيق الفترة" }));
  expect(scope.getByRole("alert")).toHaveTextContent("تاريخ النهاية يجب ألا يسبق البداية");
  expect(fetcher.mock.calls.some(([path]) => String(path).includes("owner-cockpit?from=2026-10-16"))).toBe(false);
  expect(today).toHaveAttribute("aria-pressed", "true");
  fireEvent.keyDown(from, { key: "Escape" });
  expect(custom).toHaveFocus();
  expect(today).toHaveAttribute("aria-pressed", "true");
  fireEvent.click(custom);
  fireEvent.change(scope.getByLabelText("من تاريخ (ملخص الأكاديمية)"), { target: { value: "2026-10-16" } });
  fireEvent.click(scope.getByRole("button", { name: "تطبيق الفترة" }));
  await waitFor(() => expect(fetcher.mock.calls.some(([path]) => String(path).includes("owner-cockpit?from=2026-10-16&to=2026-10-20"))).toBe(true));
  expect(fetcher.mock.calls.some(([path]) => String(path).includes("owner-summary?from=2026-10-16"))).toBe(false);
  const query = new URLSearchParams(window.location.search);
  expect(query.get("period-cockpit")).toBe("range,2026-10-16,2026-10-20");
  for (const [key, value] of query) incoming.set(key, value);
  view.unmount();
  render(<PremiumOwnerDashboard/>);
  expect(await screen.findByLabelText("الفترة المطبقة (ملخص الأكاديمية)")).toHaveTextContent("من 16 أكتوبر 2026 إلى 20 أكتوبر 2026");
});

it("payment request categories and real reminder recipients remain distinct actions",async()=>{
 source();render(<PremiumOwnerDashboard/>);const renewal=await screen.findByRole("link",{name:/تجديد الاشتراك/});expect(renewal).toHaveAttribute("href",expect.stringContaining("type=Renewal"));
 expect(screen.getByRole("link",{name:/تحضير تذكيرات التحصيل/})).toHaveAttribute("href",expect.stringContaining("kind=Overdue"));
 expect(await screen.findByRole("link",{name:/عرض حالات الغياب/})).toHaveAttribute("href",expect.stringContaining("kind=Absence"));
 expect(screen.getAllByRole("link",{name:/مراجعة اللاعبين/}).map(link=>link.getAttribute("href"))).toEqual(expect.arrayContaining([expect.stringContaining("kind=Expired"),expect.stringContaining("kind=Expiring")]));
 expect(screen.queryByText(/أولوية 1/)).not.toBeInTheDocument();expect(screen.getAllByRole("heading",{level:3}).length).toBeGreaterThan(0);
});
it("monthly target is displayed only when the canonical source qualifies its scope",async()=>{
 source(summary,{...cockpit,target:{amount:50000,achievement:11.2}});render(<PremiumOwnerDashboard/>);expect(await screen.findByLabelText("تحقيق هدف التحصيل")).toHaveAttribute("aria-valuenow","11.2");
 expect(screen.getByRole("button",{name:/ضبط هدف التحصيل/})).toBeEnabled();
});
it("keeps the true achieved percentage when collections exceed the target", async () => {
 source(summary, {...cockpit, target:{amount:5000,achievement:112}});
 render(<PremiumOwnerDashboard/>);
 const goal = await screen.findByRole("progressbar", {name:"تحقيق هدف التحصيل"});
 expect(goal).toHaveAttribute("aria-valuenow","100");
 expect(goal).toHaveAttribute("aria-valuetext","112% من الهدف");
 expect(goal).toHaveTextContent("112%");
});
it("links period counts to the same dates and keeps day pass requests out of the payment queue total", async () => {
 source({...summary,paymentRequests:{...summary.paymentRequests,total:6,dayPasses:2}});
 render(<PremiumOwnerDashboard/>);
 const link = await screen.findByRole("link",{name:"4"});
 expect(link).toHaveAttribute("href","/dashboard/subscriptions/payments?from=2026-09-20&to=2026-10-20&status=Pending");
});
it("new home has no duplicated welcome/latest/payroll blocks and keeps subscription/report drilldowns",async()=>{
 source();render(<PremiumOwnerDashboard/>);expect(await screen.findByRole("heading",{name:"اشتراكات الأكاديمية"})).toBeVisible();
 expect(screen.getByRole("link",{name:/سارية/})).toHaveAttribute("href",expect.stringContaining("state=active"));
 expect(screen.getByRole("link",{name:/بطولات/})).toHaveAttribute("href",expect.stringContaining("type=Tournament"));
 expect(screen.queryByText("أحدث التحصيلات")).not.toBeInTheDocument();expect(screen.queryByText(/أهلًا/)).not.toBeInTheDocument();expect(screen.queryByText("مرجع التحليل")).not.toBeInTheDocument();
 expect(screen.queryByLabelText("تحقيق هدف التحصيل")).not.toBeInTheDocument();expect(screen.getByText("لم يُحدد هدف للفترة")).toBeVisible();
 fireEvent.click(screen.getByRole("button",{name:"فرع اصطناعي"}));
 expect(screen.getByRole("button",{name:"فرع اصطناعي"})).toHaveAttribute("aria-pressed","true");
 fireEvent.click(screen.getByRole("button",{name:"كل الفروع"}));
 expect(screen.getByRole("button",{name:"فرع اصطناعي"})).toHaveAttribute("aria-pressed","false");
});
it("shows a negative operating change when the current surplus is still positive",async()=>{
 source();render(<PremiumOwnerDashboard/>);
 await screen.findByText("الفائض التشغيلي");
 const section=screen.getByRole("region",{name:"تقرير الأرباح والخسائر"});
 const trend=within(section).getByText(/أقل من الفترة السابقة/);
 expect(trend.parentElement?.querySelector(".lucide-arrow-down-right")).toBeInTheDocument();
 expect(within(section).getByText("الفائض التشغيلي")).toBeInTheDocument();
});
it("does not invent a pie when there are no stored records",()=>{
 render(<DonutChart center="0" caption="لاعبًا" rows={[{name:"فعال",value:0,href:"/players"}]}/>);expect(screen.queryByRole("img")).not.toBeInTheDocument();expect(screen.getByText("لا توجد سجلات ضمن الفلاتر الحالية.")).toBeVisible();
});
it("shows the branch rail in place of owner quick actions and retains coach sorting",async()=>{
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
 const branches=screen.getByRole("region",{name:"الفروع"});
 expect(within(branches).getByRole("button",{name:"فرع اصطناعي"})).toBeVisible();
 expect(screen.queryByRole("navigation",{name:"الإجراءات التشغيلية السريعة"})).not.toBeInTheDocument();
 expect(screen.queryByRole("region",{name:"الرسوم الدائرية"})).not.toBeInTheDocument();
 expect(screen.getByRole("region",{name:"التحصيلات ومصادرها"})).toBeVisible();
 expect(screen.queryByRole("img",{name:/فعالة: 20، منتهية: 10/})).not.toBeInTheDocument();
 expect(screen.queryByRole('button',{name:'الفروع'})).not.toBeInTheDocument();
});
it("shows real coach ratings and groups requiring intervention without inventing AI scores",async()=>{
 source({...summary,operations:{...summary.operations,coachPerformance:[{id:"c1",name:"محمود سليمان",branches:["b1"],sports:["كرة القدم"],groupCount:2,sessionCount:7,parentRating:4.5,parentRatingCount:2}],groupAttention:[{id:"g1",name:"مجموعة تحتاج متابعة"}]}});
 render(<PremiumOwnerDashboard/>);expect(await screen.findByText("تقييم الأسر 4.5 من 5")).toBeVisible();expect(screen.getByText("عدد التقييمات 2")).toBeVisible();
 const coach = screen.getByRole("link",{name:/محمود سليمان/});
 expect(coach).toHaveAttribute("href",expect.stringContaining("/academy/coaches/c1"));
 expect(coach).toHaveTextContent("المجموعات المسندة2");
 expect(coach).toHaveTextContent("حصص المجموعات7");
 expect(within(coach).queryByRole("img")).not.toBeInTheDocument();
 expect(screen.getByRole("link",{name:/مراجعة المجموعة وفتح متابعة/})).toHaveAttribute("href",expect.stringContaining("/academy/groups/g1"));
 expect(screen.queryByText(/مؤشر AI/)).not.toBeInTheDocument();expect(screen.queryByRole("heading",{name:"المدربون وتقييم الأسر"})).not.toBeInTheDocument();
});
it("does not relabel an undocumented historical refund as cash or online",async()=>{
 source({...summary,collections:{...summary.collections,month:-40,monthCash:0,monthOnline:0,monthUnspecified:-40}});render(<PremiumOwnerDashboard/>);
 const link=await screen.findByRole("link",{name:/طريقة صرف قديمة غير موثقة/});expect(link).toHaveAttribute("href",expect.stringContaining("provider=Unspecified"));expect(link).toHaveTextContent("-40");
});
it("an owner manages attendance/transfers without receiving platform branding controls", async () => {
  incoming.set("tab","operations");
  global.fetch = vi.fn(input => String(input).includes("experience") ? response({ displayName: "أكاديمية اختبار", platformBrandOperator: false, coachAttendanceEnabled: true }) : String(input).includes("my-permissions") ? response(["academy.settings.manage"]) : String(input).endsWith("/me") ? response({ role: "AcademyOwner" }) : response({ configured: false })) as typeof fetch;
  render(<AcademySettings />); expect(await screen.findByRole("checkbox")).toBeVisible(); expect(screen.queryByText("هوية الأكاديميات — مشغّل المنصة")).not.toBeInTheDocument(); expect(screen.queryByLabelText("رفع شعار")).not.toBeInTheDocument();
});

it("keeps collection, subscription, and summary dates independent through branch changes",async()=>{
 const collections={...summary.collections,monthBranches:[{id:'b1',name:'فرع التحصيل',amount:5600,count:3}],monthSources:[{key:'Enrollment',label:'اشتراكات جديدة',amount:5600,count:3,collectionType:'Subscription',subscriptionKind:'Enrollment'}]};
 const fetcher=source({...summary,collections});render(<PremiumOwnerDashboard/>);
 const section=await screen.findByRole('region',{name:'التحصيلات ومصادرها'});
 const periods = (name: string) => screen.getByRole('group',{name});
 const day=within(periods('فترة ملخص الأكاديمية')).getByRole('button',{name:'هذا اليوم'});
 fireEvent.click(day);
 const month=within(periods('فترة التحصيلات')).getByRole('button',{name:'من أول الشهر'});
 fireEvent.click(month);
 fireEvent.click(screen.getByRole('button',{name:'فرع اصطناعي'}));
 await waitFor(()=>{
  const calls=fetcher.mock.calls.map(([path])=>String(path));
  expect(calls).toEqual(expect.arrayContaining([
    expect.stringContaining('owner-cockpit?from=2026-10-20&to=2026-10-20&branchId=b1'),
    expect.stringContaining('owner-summary?from=2026-10-01&to=2026-10-20&branchId=b1'),
    expect.stringContaining('subscriptions/home?branchId=b1&asOf=2026-10-20&from=2026-09-20'),
  ]));
 });
 expect(within(section).getByRole('link',{name:/اشتراكات جديدة/})).toHaveAttribute('href',expect.stringContaining('from=2026-10-01&to=2026-10-20&branchId=b1'));
 expect(within(section).getByRole('link',{name:/اشتراكات جديدة/})).toHaveAttribute('href',expect.stringContaining('subscriptionKind=Enrollment'));
 expect(within(section).getByRole('link',{name:/فرع التحصيل/})).toHaveAttribute('href',expect.stringContaining('branchId=b1'));
 expect(screen.getByRole('link',{name:'كل المدربين'})).toHaveAttribute('href',expect.stringContaining('from=2026-10-01&to=2026-10-20&branchId=b1'));
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
 await waitFor(()=>expect(statementReads).toBe(1));
 finish(new Response('{}',{status:503}));
 const retry=await screen.findByRole('button',{name:'إعادة تحميل البيان المالي'});
 const summaryReads=base.mock.calls.filter(([path])=>String(path).includes('owner-summary')).length;
 fireEvent.click(retry);
 await screen.findByText('الفائض التشغيلي');
 expect(base.mock.calls.filter(([path])=>String(path).includes('owner-summary'))).toHaveLength(summaryReads);
 expect(statementReads).toBe(2);
});

it("groups current overdue figures in one branch link and keeps dates on period figures", async () => {
  incoming.set("branchId","b1");
  source(summary,{...cockpit,overdue:{amount:1250,families:2,players:3,asOf:"2026-10-20"},members:{groups:4,players:17}});
  render(<PremiumOwnerDashboard/>);
  const now=await screen.findByRole("group",{name:"الوضع الحالي"});
  const overdue=within(now).getByRole("link",{name:/مستحقات متأخرة/});
  expect(overdue).toHaveTextContent("لاعبون عليهم متأخرات");
  expect(overdue).toHaveTextContent("الرصيد حتى");
  expect(overdue).toHaveAttribute("href","/dashboard/finance/receivables?state=overdue&branchId=b1");
  expect(within(now).getByRole("link",{name:/أماكن تدريب متاحة/})).toHaveAttribute("href","/dashboard/operations/capacity?branchId=b1");
  const period=screen.getByRole("group",{name:"مؤشرات الفترة"});
  expect(within(period).getByRole("link",{name:/عدد اللاعبين/})).toHaveAttribute("href","/dashboard/players/subscriptions?uniquePlayers=true&origin=owner-cockpit&from=2026-09-20&to=2026-10-20&branchId=b1");
});

it.each([
  [{amount:0,players:0,families:0,asOf:"2026-10-20"}, "لا توجد مستحقات متأخرة", true],
  [{players:0,families:0,asOf:"2026-10-20"}, "رصيد المستحقات المتأخرة غير متاح", false],
  [{amount:925,players:-1,families:0,asOf:"2026-10-20"}, "لاعبون عليهم متأخرات غير متاح", true],
  [undefined, "رصيد المستحقات المتأخرة غير متاح", false],
] as const)("explains the overdue balance state and keeps the selected branch link", async (overdue, message, dated) => {
  incoming.set("branchId","b1");
  source(summary,{...cockpit,overdue});
  render(<PremiumOwnerDashboard/>);
  const now=await screen.findByRole("group",{name:"الوضع الحالي"});
  const link=within(now).getByRole("link",{name:new RegExp(message)});
  expect(link).toHaveAttribute("href","/dashboard/finance/receivables?state=overdue&branchId=b1");
  expect(within(link).queryByText("0 جنيه")).not.toBeInTheDocument();
  expect(Boolean(within(link).queryByText(/الرصيد حتى/))).toBe(dated);
});
