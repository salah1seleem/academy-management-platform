// Explicit API-contract fixtures for the restructured home, never UI defaults.
export function v41HomeFixture(path:string):unknown|undefined {
  if(path.includes('/academy/calendar'))return {today:'2026-09-28',trailingMonthFrom:'2026-08-28'};
  if(path.includes('/academy/experience'))return {displayName:'أكاديمية الاختبار',logoUrl:null};
  if(path.includes('/notifications'))return {items:[],unread:0,total:0};
  if(path.includes('/reports/owner-summary'))return {from:'2026-08-28',to:'2026-09-28',localToday:'2026-09-28',collectionDay:'2026-09-28',collectionMonthFrom:'2026-09-01',currency:'EGP',collections:{today:900,month:4100,todayCash:900,todayOnline:0,monthCash:4100,monthOnline:0,todayUnspecified:0,monthUnspecified:0},subscriptions:{total:4,active:3,expiring:1,expired:1},paymentRequests:{total:1,newEnrollments:1,renewals:0,products:0,tournaments:0},operations:{coachPerformance:[],groupAttention:[],openSafetyFeedback:0}};
  if(path.includes('/reports/owner-cockpit'))return {currency:'EGP',collections:{net:4100,changePercentage:null},target:{amount:null,achievement:null},overdue:{amount:0,families:0},risk:{players:0},attendance:{percentage:null},capacity:{seats:0}};
  if(path.includes('/reports/operating-statement'))return {currency:'EGP',surplus:4100,income:[],expenses:[],totalIncome:4100,totalExpenses:0};
  if(path.includes('/reports/vip-members')||path.includes('/reports/featured-players'))return {items:[]};
  return undefined;
}
