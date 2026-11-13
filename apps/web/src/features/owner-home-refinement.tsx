"use client";
import { SubscriptionHome } from "./subscription-home";
import { OperationalAdminHome } from "./operational-admin-home";
import { PersonPortrait } from "../components/person-portrait";
import Link from "next/link";
import {useSearchParams} from "next/navigation";
import {useEffect,useRef,useState,type CSSProperties} from "react";
import {ArrowDownRight,ArrowUpRight,Coins,Crown,Flag,ReceiptText,Shield,Star,UsersRound,CalendarCheck} from "lucide-react";
import {ErrorState,LoadingState} from "../components/dashboard-shell";
import {SportsQuickActions} from "../components/sports-ui";
import {formatDateAr,formatMoneyAr} from "../lib/formatters";
import {ownerDecisionRecords,smartDecisionRecords,decisionApi,decisionQuery,type SmartDecisionCounts} from "./owner-home-decisions";
import {AcademyActivityPreview} from "./academy-activities";
import {OwnerWebProfile} from "./owner-web-profile";
import {CollectionTargetDialog} from "./owner-home-settings";
import {ExperienceTools} from "./experience-tools";
import {MoneyValue} from "../components/money-value";
import {OwnerOperatingPanel,type OperatingStatementPreview} from "./owner-operating-panel";
import {OwnerHomeTrend,type HomeTrendPoint} from "./owner-home-trend";
import styles from "./owner-home-refinement.module.css";
import {OwnerBranchRail,OwnerHomePeriodControls,isOwnerHomeDate,ownerPeriodError,selectedDates,type DateBounds,type HomePeriod} from "./owner-home-period";
import {useWebAppearance,saveWebAppearance} from '../lib/web-appearance';
type Filters={from:string;to:string;branchId:string};
type Coach={id:string;name:string;photoReference?:string;branches:string[];sports:string[];groupCount:number;sessionCount:number;parentRating:number|null;parentRatingCount:number};
type CollectionSource={key:string;label:string;amount:number;count:number;collectionType:string;subscriptionKind?:string};
type CollectionBranch={id:string|null;name:string;amount:number;count:number};
type Summary={from:string;to:string;currency:string;localToday:string;collectionDay:string;collectionMonthFrom:string;collections:{today:number;month:number;todayCash:number;todayOnline:number;monthCash:number;monthOnline:number;todayUnspecified:number;monthUnspecified:number;todaySources?:CollectionSource[];monthSources?:CollectionSource[];todayBranches?:CollectionBranch[];monthBranches?:CollectionBranch[]};subscriptions:{trend?:HomeTrendPoint[];total:number;active:number;expiring:number;expired:number;branches?:{id:string;name:string;count:number}[]};paymentRequests:{total:number;newEnrollments:number;renewals:number;products:number;tournaments:number;dayPasses?:number};operations:{underAssignedCoaches?:{id:string;name:string;groupCount:number;target:number}[];groupAttention:{id:string;name:string}[];coachPerformance:Coach[];openSafetyFeedback:number;pendingPaymentQueueBacklog?:number}};
type Cockpit={home?:{quickActionsAtTop:boolean;coachAssignmentTarget:number|null};currency:string;collections:{weekly?:HomeTrendPoint[];net:number;changePercentage:number|null};target:{amount:number|null;achievement:number|null};overdue?:{amount?:number;families?:number;players?:number;asOf?:string};members?:{players:number;groups:number};risk:{players:number};attendance:{percentage:number|null};capacity:{seats:number;unknownGroups?:number};followups?:{overdue:number;unassigned:number}};
type Statement=OperatingStatementPreview;
type HomeData={summary:Summary;cockpit:Cockpit;statement:Statement;vip:Vip[];players:Player[]};
type HomeSource=keyof HomeData;
const homeLabels:Record<HomeSource,string>={summary:"التحصيلات والطلبات",cockpit:"ملخص الأكاديمية",statement:"البيان المالي",vip:"الأعضاء المميزون",players:"أبرز اللاعبين"};
type Vip={id:string;displayName:string;photoReference?:string;monthlySubscriptionValue:number;currency:string};
type Player={id:string;name:string;branch:string;photoReference?:string;score:number;stars:number};
const n=(v:number)=>v.toLocaleString("ar-EG-u-nu-latn",{maximumFractionDigits:1});
const params=(f:Partial<Filters>&Record<string,string>)=>new URLSearchParams({...Object.fromEntries(Object.entries(f).filter(([,v])=>v)),...!f.from&&!f.to?{allDates:"true"}:{}}).toString();
const money=(v:number,c:string)=>formatMoneyAr(v,c);
async function get<T>(url:string,signal:AbortSignal):Promise<T>{const r=await fetch(url,{signal,cache:"no-store"});if(!r.ok)throw Error();return r.json();}
function Portrait({name,reference,adult=true}:{name:string;reference?:string;adult?:boolean}){return <PersonPortrait name={name} photo={reference} adult={adult} size={240} className={styles.portrait}/>;}
function useHomeSource<T>(url: string | null) {
  const [result, setResult] = useState<{url:string; value?:T; failed?:boolean}>();
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    if (!url) return;
    const controller = new AbortController();
    get<T>(url, controller.signal).then(value => {
      if (!controller.signal.aborted) setResult({url, value});
    }).catch(() => {
      if (!controller.signal.aborted) setResult({url, failed:true});
    });
    return () => controller.abort();
  }, [url, revision]);
  const current = result?.url === url ? result : undefined;
  return {
    value: current?.value,
    failed: current?.failed,
    retry: () => {setResult(undefined); setRevision(previous => previous + 1);},
  };
}

const periodNames = ["subscriptions", "cockpit", "summary", "statement"] as const;
type PeriodName = typeof periodNames[number];
type SavedPeriod = Pick<HomePeriod, "preset"> & {range?: DateBounds};
function readPeriods(query: Pick<URLSearchParams, "get">) {
  const result: Partial<Record<PeriodName, SavedPeriod>> = {};
  for (const name of periodNames) {
    const [preset, from = "", to = ""] = (query.get(`period-${name}`) ?? "").split(",");
    if (preset !== "range" && preset !== "today" && preset !== "month") continue;
    const range = {from, to};
    const valid = !ownerPeriodError(range);
    if (preset !== "range" || valid) result[name] = {preset, range: valid ? range : undefined};
  }
  return result;
}

export function RefinedOwnerHome(){
  const incoming=useSearchParams();
  const mode=useWebAppearance(),[displayName,setDisplayName]=useState('');
  const appearance=saveWebAppearance;
  const [targetOpen,setTargetOpen]=useState(false),[coachByGroups,setCoachByGroups]=useState(false);
  const [filters,setFilters]=useState<Filters|null>(null),[role,setRole]=useState(""),[error,setError]=useState("");
  const [brand,setBrand]=useState<{displayName:string;logoUrl:string|null;primaryColor?:string}>({displayName:"الأكاديمية",logoUrl:null}),[branches,setBranches]=useState<{id:string;arabicName:string}[]>([]);
  const [calendarToday, setCalendarToday] = useState("");
  const [periods, setPeriods] = useState(() => readPeriods(incoming));
  const subscriptionSection = useRef<HTMLElement>(null);
  const cockpitSection = useRef<HTMLElement>(null);
  const [restored] = useState(() => ({
    to: incoming.get("subscriptionTo"),
    from: incoming.get("subscriptionFrom") ?? "",
    sport: incoming.get("subscriptionSport") ?? "",
    dimension: incoming.get("subscriptionDimension") ?? "",
  }));
  const initialRange: DateBounds = {from: filters?.from ?? "", to: filters?.to ?? ""};
  const restoredRange = isOwnerHomeDate(restored.to) &&
    (!restored.from || isOwnerHomeDate(restored.from) && restored.from <= restored.to)
    ? {from: restored.from, to: restored.to} : initialRange;
  const period = (name: PeriodName): HomePeriod => ({
    preset: periods[name]?.preset ?? "range",
    range: periods[name]?.range ?? (name === "subscriptions" ? restoredRange : initialRange),
  });
  const scope = (name: PeriodName): Filters => ({...selectedDates(period(name), calendarToday),branchId:filters?.branchId ?? ""});
  function updateHomeQuery(change: (query: URLSearchParams) => void) {
    const query = new URLSearchParams(window.location.search);
    change(query);
    const value = query.toString();
    window.history.replaceState(null, "", `${window.location.pathname}${value ? `?${value}` : ""}${window.location.hash}`);
  }
  function changePeriod(name: PeriodName, value: HomePeriod) {
    setPeriods(previous => ({...previous,[name]:value}));
    updateHomeQuery(query => query.set(`period-${name}`, [value.preset,value.range.from,value.range.to].join(",")));
  }
  const view = (name: PeriodName, title: string) => <OwnerHomePeriodControls
    name={title} today={calendarToday} value={period(name)} onChange={value => changePeriod(name,value)}/>;
  const summaryScope=scope("summary"),cockpitScope=scope("cockpit"),statementScope=scope("statement"),subscriptionScope=scope("subscriptions");
  const active=role==="AcademyOwner" && filters!==null;
  useEffect(() => {
    if (!active) return;
    const target = window.location.hash === "#academy-subscriptions" ? subscriptionSection.current
      : window.location.hash === "#academy-cockpit" ? cockpitSection.current : null;
    target?.scrollIntoView?.({ block: "start" });
    target?.focus({ preventScroll: true });
  }, [active]);
  const listQuery=new URLSearchParams({...filters?.branchId?{branchId:filters.branchId}:{},...filters?.to?{asOf:filters.to}:{}});
  const summarySource = useHomeSource<Summary>(active? `/api/v1/reports/owner-summary?${params(summaryScope)}&includeSubscriptionAnalytics=false` : null);
  const cockpitSource = useHomeSource<Cockpit>(active? `/api/v1/reports/owner-cockpit?${params(cockpitScope)}` : null);
  const statementSource = useHomeSource<Statement>(active? `/api/v1/reports/operating-statement?${params(statementScope)}&includeHistory=true` : null);
  const decisionSource = useHomeSource<SmartDecisionCounts>(active ? `${decisionApi}?${decisionQuery(filters.branchId)}` : null);
  const vipSource = useHomeSource<{items:Vip[]}>(active? `/api/v1/reports/vip-members?${listQuery}` : null);
  const playersSource = useHomeSource<{items:Player[]}>(active? `/api/v1/reports/featured-players?${listQuery}` : null);
  const sources = {summary:summarySource,cockpit:cockpitSource,statement:statementSource,vip:vipSource,players:playersSource};
  const data = {summary:summarySource.value,cockpit:cockpitSource.value,statement:statementSource.value,vip:vipSource.value?.items,players:playersSource.value?.items};
  const pending = (key: HomeSource) => <div className={styles.pendingSection} role={sources[key].failed ? undefined : "status"} aria-label={`جارٍ تحميل ${homeLabels[key]}`}>
    {sources[key].failed?<><ErrorState message={`تعذر تحميل ${homeLabels[key]}.`}/><button onClick={sources[key].retry}>إعادة تحميل {homeLabels[key]}</button></>:<><span/><span/><span/></>}
  </div>;
  useEffect(()=>{const controller=new AbortController(),signal=controller.signal;
    get<{role:string;displayName:string}>("/api/v1/me",signal).then(me=>{if(signal.aborted)return;setRole(me.role);setDisplayName(me.displayName);if(me.role==="AcademyOwner")get<{branches:typeof branches}>("/api/v1/manage/structure/options",signal).then(o=>{if(!signal.aborted)setBranches(o.branches);}).catch(()=>undefined);}).catch(()=>{if(!signal.aborted)setError("تعذر تحميل هوية حسابك.");});
    get<typeof brand>("/api/v1/academy/experience",signal).then(branding=>{if(!signal.aborted)setBrand(branding);}).catch(()=>undefined);
    get<{today:string;trailingMonthFrom:string}>("/api/v1/academy/calendar",signal).then(calendar=>{if(!signal.aborted){setCalendarToday(calendar.today);setFilters({from:incoming.get("allDates")==="true"?"":incoming.get("from")??calendar.trailingMonthFrom,to:incoming.get("allDates")==="true"?"":incoming.get("to")??calendar.today,branchId:incoming.get("branchId")??""});}}).catch(()=>{if(!signal.aborted)setError("تعذر تحميل تاريخ الأكاديمية.");});
    return()=>controller.abort();
    // Search parameters initialize one coherent scope; subsequent edits remain local.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  },[]);
  if(role==="AcademyAdmin")return <OperationalAdminHome logoUrl={brand.logoUrl} primaryColor={brand.primaryColor}/>;if(!filters||!role)return error?<ErrorState message={error}/>:<LoadingState/>;if(role!=="AcademyOwner")return <SportsQuickActions showReports={false}/>;
  function applyBranch(id: string) {
    if (!filters) return;
    const value = {...filters, branchId:id};
    setFilters(value);
    updateHomeQuery(query => {
      if (id) query.set("branchId",id);
      else query.delete("branchId");
    });
  }
  const q=params(filters);
  const currentHref=(path:string)=>cockpitScope.branchId ? `${path}${path.includes("?")?"&":"?"}${new URLSearchParams({branchId:cockpitScope.branchId})}` : path;
  const report=(dates:Filters, extra:Record<string,string>={})=>`/dashboard/reports/financial?${params({...dates,...extra})}`;
  const queue=(type="")=>`/dashboard/subscriptions/payments?${params({...summaryScope,type,status:"Pending"})}`;
  const scoped=(url:string, dates?:Filters)=>`${url}${url.includes('?')?'&':'?'}${dates ? params(dates) : q}`;
  const title=(text:string,icon:React.ReactNode,href?:string,action="عرض الكل")=><div className={styles.section}>{icon}<h2>{text}</h2>{href&&<Link href={href}>{action}</Link>}</div>;
  const change=data.cockpit?.collections.changePercentage,summary=data.summary;
  const overdue=data.cockpit?.overdue;
  const overdueAmount=typeof overdue?.amount==="number"&&Number.isFinite(overdue.amount)&&overdue.amount>=0?overdue.amount:null;
  const overduePlayers=typeof overdue?.players==="number"&&Number.isInteger(overdue.players)&&overdue.players>=0?overdue.players:null;
  const overdueAsOf=overdue?.asOf??null;
  const overdueDate=overdueAmount!==null&&isOwnerHomeDate(overdueAsOf)?formatDateAr(overdueAsOf):null;
  const targetAchievement = data.cockpit?.target.achievement;
  const targetProgress = targetAchievement == null ? 0 : Math.min(100, Math.max(0, targetAchievement));
  const coaches=[...(summary?.operations.coachPerformance??[])].sort((a,b)=>(coachByGroups?b.groupCount-a.groupCount:(b.parentRating??-1)-(a.parentRating??-1))||a.name.localeCompare(b.name,"ar"));
  const legacyDecisions=data.cockpit&&summary?ownerDecisionRecords({
    ...data.cockpit,
    risk:decisionSource.value?{players:0}:data.cockpit.risk,
    capacity:decisionSource.value?{seats:0}:data.cockpit.capacity,
    groupAttention:summary.operations.groupAttention,
    underAssignedCoaches:summary.operations.underAssignedCoaches,
  },filters):[];
  const decisions=[
    ...smartDecisionRecords(decisionSource.value,filters.branchId,summary?.operations.pendingPaymentQueueBacklog??0),
    ...legacyDecisions,
  ];
  return <div className={styles.home} data-approved-home="true" data-mode={mode} style={{'--accent':brand.primaryColor&&brand.primaryColor.toUpperCase()!=='#D71935'?brand.primaryColor:mode==='dark'?'#3B6CF0':'#C8102E'} as CSSProperties}>
    <header className={styles.header}>{brand.logoUrl&&
      // eslint-disable-next-line @next/next/no-img-element -- trusted academy logo endpoint
      <img src="/api/v1/academy/branding/logo" alt="شعار الأكاديمية"/>}<div><h1>الرئيسية</h1><p>{brand.displayName} · {branches.find(b=>b.id===filters.branchId)?.arabicName??"كل الفروع"}</p></div>
<div className={styles.tools}><ExperienceTools/><OwnerWebProfile name={displayName} mode={mode} onMode={appearance}/></div></header>
    <OwnerBranchRail academy={brand.displayName} branches={branches} branchId={filters.branchId} onChange={applyBranch}/>
    <section aria-label="الاشتراكات" id="academy-subscriptions" tabIndex={-1} ref={subscriptionSection}>
      {view("subscriptions","الاشتراكات")}
      <SubscriptionHome branchId={subscriptionScope.branchId} from={subscriptionScope.from || undefined}
        asOf={subscriptionScope.to || undefined} onBranchChange={applyBranch} inlineOwnerScope
        initialSport={restored.sport} initialDimension={restored.dimension}
        logoUrl={brand.logoUrl} primaryColor={brand.primaryColor}/>
    </section>
    <>
      {summary&&summary.operations.openSafetyFeedback>0&&<Link className={styles.notice} href={`/dashboard/feedback?safetyOnly=true&status=Unresolved&branchId=${filters.branchId}`}>بلاغات سلامة تحتاج مراجعتك: {summary.operations.openSafetyFeedback}</Link>}
      <div className={styles.mainGrid}>
        <section className={`${styles.panel} ${styles.academySummary}`} aria-label="ملخص الأكاديمية" id="academy-cockpit" tabIndex={-1} ref={cockpitSection}>
          {title("ملخص الأكاديمية",<CalendarCheck size={20}/>)}
          {view("cockpit","ملخص الأكاديمية")}
          {data.cockpit ? <><div className={styles.hero}>
          <div className={styles.summaryMeasureHeader}>
            <span className={styles.eyebrow}>التحصيل خلال الفترة</span>
            <button type="button" className={styles.heroTargetLink} disabled={!cockpitScope.from||!cockpitScope.to} onClick={()=>setTargetOpen(true)}><Flag size={13} aria-hidden="true"/>ضبط هدف التحصيل</button>
          </div>
          <div className={styles.summaryMeasures}>
            <div>
              <Link className={styles.amount} href={report(cockpitScope)}><MoneyValue amount={data.cockpit.collections.net} currency={data.cockpit.currency}/></Link>
              {change!=null&&<div className={`${styles.growth} ${change<0?styles.decline:""}`} data-neutral={change===0}>{change!==0&&(change<0?<ArrowDownRight size={17} aria-hidden="true"/>:<ArrowUpRight size={17} aria-hidden="true"/>)}<span>{change===0?"مثل الفترة السابقة":`${n(Math.abs(change))}% ${change<0?"أقل":"أعلى"} من الفترة السابقة`}</span></div>}
            </div>
            {data.cockpit.target.amount!=null
              ? <div className={styles.summaryGoal}><div className={styles.target}>
                {targetAchievement!=null&&<div className={styles.targetRing} role="progressbar" aria-label="تحقيق هدف التحصيل" aria-valuenow={targetProgress} aria-valuemin={0} aria-valuemax={100} aria-valuetext={`${n(targetAchievement)}% من الهدف`} style={{"--target-progress":`${targetProgress}%`} as CSSProperties}><b aria-hidden="true">{n(targetAchievement)}%</b></div>}
                <div><span>هدف الفترة</span><strong><MoneyValue amount={data.cockpit.target.amount} currency={data.cockpit.currency}/></strong></div>
              </div></div>
              : <span className={styles.goalEmpty}>لم يُحدد هدف للفترة</span>}
          </div>
          {(!cockpitScope.from||cockpitScope.from!==cockpitScope.to)&&<OwnerHomeTrend points={data.cockpit.collections.weekly??[]} currency={data.cockpit.currency} href={p=>report(cockpitScope,{from:p.from!,to:p.to!})}/>}
        </div>
        <div className={styles.summaryKpis} role="group" aria-label="مؤشرات الفترة">
          <h3>خلال الفترة</h3>
          <div className={styles.kpis}>
            <Link className={styles.kpi} href={scoped("/dashboard/reports/attendance",cockpitScope)}><span>متوسط الحضور</span><strong>{data.cockpit.attendance.percentage==null?"—":`${n(data.cockpit.attendance.percentage)}%`}</strong></Link>
            {data.cockpit.members&&<Link className={styles.kpi} href={scoped("/dashboard/players/subscriptions?uniquePlayers=true&origin=owner-cockpit",cockpitScope)}><span>عدد اللاعبين</span><strong>{n(data.cockpit.members.players)}</strong></Link>}
          </div>
        </div>
        <div className={styles.summaryKpis} role="group" aria-label="الوضع الحالي">
          <h3>الوضع الحالي</h3>
          <div className={styles.kpis}>
            <Link className={`${styles.kpi} ${styles.receivablesKpi}`} href={currentHref("/dashboard/finance/receivables?state=overdue")}>
              {overdueAmount===null?<span className={styles.receivablesState}>رصيد المستحقات المتأخرة غير متاح</span>
                :overdueAmount===0&&overduePlayers===0?<span className={styles.receivablesState}>لا توجد مستحقات متأخرة</span>
                :<><div><span>مستحقات متأخرة</span>{" "}<strong><MoneyValue amount={overdueAmount} currency={data.cockpit.currency}/></strong></div>
                  <div><span>لاعبون عليهم متأخرات</span>{" "}{overduePlayers===null?<span>غير متاح</span>:<strong>{n(overduePlayers)}</strong>}</div></>}
              {overdueDate&&<small>الرصيد حتى {overdueDate}</small>}
            </Link>
            <Link className={styles.kpi} href={currentHref("/dashboard/operations/capacity")}><span>أماكن تدريب متاحة</span><strong>{(data.cockpit.capacity.unknownGroups??0)>0?"غير مكتمل":n(data.cockpit.capacity.seats)}</strong></Link>
            {data.cockpit.members&&<Link className={styles.kpi} href={currentHref("/dashboard/academy/groups")}><span>عدد المجموعات</span><strong>{n(data.cockpit.members.groups)}</strong></Link>}
          </div>
        </div></>:pending("cockpit")}</section>
      {decisions.length>0&&<section className={styles.decisionSection}>{title("مركز القرارات الذكية",<Shield size={20}/>,scoped("/dashboard/operations/decisions"),"كل القرارات")}<div className={styles.decisionRail}>{decisions.map(d=><article className={`${styles.panel} ${styles.decision}`} key={d.title}><Coins className={styles.amber} size={23}/><h3>{d.title}</h3><p>{d.line}</p><Link href={d.href}>{d.action} ←</Link></article>)}</div></section>}
        <section className={`${styles.panel} ${styles.collectionPanel}`} aria-label="التحصيلات ومصادرها">
          {title("التحصيلات",<Coins size={20}/>,report(summaryScope),"السجل")}
          {view("summary","التحصيلات")}
          {summary ? <>
            <div className={styles.collection}>
              <h3>صافي التحصيل خلال الفترة</h3>
              <Link href={report(summaryScope)}><MoneyValue amount={summary.collections.month} currency={summary.currency}/></Link>
              <div className={styles.splits}>{[
                {method:"Cash",label:"نقدي",value:summary.collections.monthCash},
                {method:"Online",label:"إلكتروني",value:summary.collections.monthOnline},
                ...(summary.collections.monthUnspecified!==0?[{method:"Unspecified",label:"طريقة صرف قديمة غير موثقة",value:summary.collections.monthUnspecified}]:[]),
              ].map(m=><Link key={m.method} href={report(summaryScope,{provider:m.method})}><b>{n(m.value)}</b><span>{m.label}</span></Link>)}</div>
            </div>
            <CollectionBreakdown title="حسب الفرع" rows={summary.collections.monthBranches?.map(b=>({key:b.id??"unknown",label:b.name,amount:b.amount,href:b.id?report(summaryScope,{branchId:b.id}):undefined}))??[]} currency={summary.currency}/>
            <CollectionBreakdown title="مصادر التحصيل" rows={summary.collections.monthSources?.map(s=>({key:s.key,label:s.label,amount:s.amount,href:report(summaryScope,{collectionType:s.collectionType,...s.subscriptionKind?{subscriptionKind:s.subscriptionKind}:{}})}))??[]} currency={summary.currency}/>
          </>:pending("summary")}
        </section>
        <OwnerOperatingPanel data={data.statement} controls={view("statement","الأرباح والخسائر")} pending={pending("statement")}
          href={url=>scoped(url,statementScope)}
          monthHref={month=>`/dashboard/finance/operating-statement?${params({...statementScope,from:month.from,to:month.to})}`}/>
      </div>
      <div className={styles.secondaryGrid}>{summary&&<section className={styles.paymentQueue}>{title("طلبات الدفع",<ReceiptText size={20}/>,queue())}<div className={styles.subscriptionHeader}><span>بانتظار المراجعة خلال الفترة</span><Link href={queue()}><strong>{n(summary.paymentRequests.total - (summary.paymentRequests.dayPasses ?? 0))}</strong></Link></div><div className={styles.queueRows}>{[{type:"Enrollment",label:"اشتراكات جديدة",value:summary!.paymentRequests.newEnrollments},{type:"Renewal",label:"تجديد الاشتراك",value:summary!.paymentRequests.renewals},{type:"Product",label:"منتجات رياضية",value:summary!.paymentRequests.products},...summary!.paymentRequests.tournaments?[{type:"Tournament",label:"بطولات",value:summary!.paymentRequests.tournaments}]:[]].map(r=><Link key={r.type} href={queue(r.type)}><b>{n(r.value)}</b><span>{r.label}</span><span aria-hidden="true">‹</span></Link>)}</div></section>}

      </div>
      {summary && <section aria-label="المدربون">
        {title("المدربون", <UsersRound size={20}/>, scoped(`/dashboard/academy/coaches?sort=${coachByGroups ? "groups" : "rating"}`, summaryScope), "كل المدربين")}
        {coaches.length === 0 ? <p className={styles.note}>لا يوجد مدربون في هذا النطاق.</p> : <>
          <p className={styles.note}>الحصص وتقييمات الأسر من {formatDateAr(summary.from)} إلى {formatDateAr(summary.to)}.</p>
          <div className={styles.chartTabs} role="group" aria-label="ترتيب المدربين">
            {[false,true].map(groups => <button key={String(groups)} aria-pressed={coachByGroups === groups} onClick={() => setCoachByGroups(groups)}>{groups ? "حسب المجموعات المسندة" : "حسب التقييم"}</button>)}
          </div>
          <div key={coachByGroups ? "groups" : "rating"} className={styles.carousel}>
            {coaches.map(coach => {
              const context = [...(coach.branches ?? []).map(id => branches.find(branch => branch.id === id)?.arabicName), ...(coach.sports ?? [])].filter(Boolean).join(" · ");
              return <Link className={styles.person} key={coach.id} href={scoped(`/dashboard/academy/coaches/${coach.id}`)}>
                <PersonPortrait photo={coach.photoReference} adult decorative size={240} className={styles.portrait}/>
                <h3>{coach.name}</h3>
                {context && <small>{context}</small>}
                <dl className={styles.coachStats}>
                  <div><dt>المجموعات المسندة</dt><dd>{n(coach.groupCount)}</dd></div>
                  <div><dt>حصص المجموعات</dt><dd>{n(coach.sessionCount)}</dd></div>
                </dl>
                {coach.parentRating == null ? <small>لا توجد تقييمات من الأسر.</small> : <>
                  <b className={styles.gold}>تقييم الأسر {n(coach.parentRating)} من 5</b>
                  <small>عدد التقييمات {n(coach.parentRatingCount)}</small>
                </>}
              </Link>;
            })}
          </div>
        </>}
      </section>}
      {data.vip&&<section className={styles.vip}>{title("أعضاء مميزون",<Crown className={styles.gold} size={21}/>,`/dashboard/guardians?${new URLSearchParams({sort:"value_desc",branchId:filters.branchId,asOf:filters.to})}`)}<div className={styles.carousel}>{data.vip.slice(0,5).map((g,i)=><Link className={styles.person} href={`/dashboard/guardians/${g.id}`} key={g.id}><span className={styles.rank} aria-label={`الترتيب ${i+1}`}>{i+1}</span><Portrait name={g.displayName} reference={g.photoReference}/><h3>{g.displayName}</h3><strong className={styles.gold}>{money(g.monthlySubscriptionValue,g.currency)}</strong><small>قيمة الاشتراكات الشهرية</small></Link>)}</div><Link className={styles.rewardButton} href="/dashboard/guardians/rewards">إدارة مكافآت الأعضاء المميزين</Link></section>}
      {data.players&&<section>{title("أبرز اللاعبين",<Star className={styles.gold} size={21}/>,`/dashboard/players/featured?${new URLSearchParams({branchId:filters.branchId,asOf:filters.to})}`)}<div className={styles.carousel}>{data.players.slice(0,5).map(p=><Link className={styles.person} href={`/dashboard/players/${p.id}`} key={p.id}><b className={styles.gold}>{n(p.stars)} ★</b><Portrait adult={false} name={p.name} reference={p.photoReference}/><h3>{p.name}</h3><small>{p.branch}</small><strong className={styles.green}>{n(p.score)}</strong></Link>)}</div>{!data.players.length&&<p className={styles.note}>لا توجد تقييمات منشورة مكتملة وقابلة للمقارنة في هذا النطاق.</p>}</section>}
      {!data.vip && pending("vip")}
      {!data.players && pending("players")}
      <AcademyActivityPreview/>
      {targetOpen&&data.cockpit&&<CollectionTargetDialog scope={cockpitScope} branchLabel={branches.find(b=>b.id===filters.branchId)?.arabicName??"كل الفروع"} currency={data.cockpit.currency} usesMonthlyDefault={!cockpitScope.branchId&&cockpitScope.to===calendarToday&&cockpitScope.from===`${calendarToday.slice(0,7)}-01`} onClose={unconfirmed=>{setTargetOpen(false);if(unconfirmed)cockpitSource.retry();}} onSaved={()=>{setTargetOpen(false);cockpitSource.retry();}}/>}
    </>
  </div>;
}

function CollectionBreakdown({title,rows,currency}:{title:string;rows:{key:string;label:string;amount:number;href?:string}[];currency:string}) {
 const maximum=Math.max(1,...rows.map(r=>Math.abs(r.amount)));
 return <div className={styles.collectionBreakdown} role="group" aria-label={title}><h3>{title}</h3>{rows.length===0?<p className={styles.note}>لا توجد تحصيلات في هذه الفترة.</p>:rows.map((r,i)=>{const content=<><span>{r.label}</span><b className={r.amount<0?styles.red:undefined}><MoneyValue amount={r.amount} currency={currency}/></b><i aria-hidden="true"><span style={{width:`${Math.abs(r.amount)/maximum*100}%`,background:r.amount<0?'var(--negative)':['var(--accent)','#22d3ee','#a78bfa'][i%3]}}/></i></>;return r.href?<Link href={r.href} key={r.key}>{content}</Link>:<div key={r.key}>{content}</div>;})}</div>;
}
