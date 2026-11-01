"use client";
import { SubscriptionHome } from "./subscription-home";
import { OperationalAdminHome } from "./operational-admin-home";
import { PersonPortrait } from "../components/person-portrait";
import Link from "next/link";
import {useSearchParams} from "next/navigation";
import {useEffect,useState,useRef,type CSSProperties} from "react";
import {ArrowDownRight,ArrowUpRight,Coins,Crown,Flag,HeartPulse,ReceiptText,Shield,Star,UsersRound,CalendarCheck} from "lucide-react";
import {ErrorState,LoadingState} from "../components/dashboard-shell";
import {SportsQuickActions} from "../components/sports-ui";
import {formatMoneyAr} from "../lib/formatters";
import {ownerDecisionRecords} from "./owner-home-decisions";
import {OwnerWebProfile} from "./owner-web-profile";
import {CollectionTargetDialog} from "./owner-home-settings";
import {ExperienceTools} from "./experience-tools";
import {MoneyValue} from "../components/money-value";
import {OwnerOperatingPanel,type OperatingStatementPreview} from "./owner-operating-panel";
import {OwnerHomeTrend,type HomeTrendPoint} from "./owner-home-trend";
import styles from "./owner-home-refinement.module.css";
import {useWebAppearance,saveWebAppearance} from '../lib/web-appearance';
type Filters={from:string;to:string;branchId:string};
type Coach={id:string;name:string;photoReference?:string;branches:string[];sports:string[];groupCount:number;sessionCount:number;parentRating:number|null;parentRatingCount:number};
type CollectionSource={key:string;label:string;amount:number;count:number;collectionType:string;subscriptionKind?:string};
type CollectionBranch={id:string|null;name:string;amount:number;count:number};
type Summary={from:string;to:string;currency:string;localToday:string;collectionDay:string;collectionMonthFrom:string;collections:{today:number;month:number;todayCash:number;todayOnline:number;monthCash:number;monthOnline:number;todayUnspecified:number;monthUnspecified:number;todaySources?:CollectionSource[];monthSources?:CollectionSource[];todayBranches?:CollectionBranch[];monthBranches?:CollectionBranch[]};subscriptions:{trend?:HomeTrendPoint[];total:number;active:number;expiring:number;expired:number;branches?:{id:string;name:string;count:number}[]};paymentRequests:{total:number;newEnrollments:number;renewals:number;products:number;tournaments:number};operations:{underAssignedCoaches?:{id:string;name:string;groupCount:number;target:number}[];groupAttention:{id:string;name:string}[];coachPerformance:Coach[];openSafetyFeedback:number}};
type Cockpit={home?:{quickActionsAtTop:boolean;coachAssignmentTarget:number|null};currency:string;collections:{weekly?:HomeTrendPoint[];net:number;changePercentage:number|null};target:{amount:number|null;achievement:number|null};overdue:{amount:number;families:number};risk:{players:number};attendance:{percentage:number|null};capacity:{seats:number;unknownGroups?:number};followups?:{overdue:number;unassigned:number}};
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
function DateTile({label,value,onChange}:{label:string;value:string;onChange:(v:string)=>void}){const date=value?new Date(`${value}T12:00:00Z`):null;return <div className={styles.date}><label>{label}<strong>{date?n(date.getUTCDate()):"—"}</strong></label><div><span>{date?new Intl.DateTimeFormat("ar-EG",{month:"long",timeZone:"UTC"}).format(date):"كل التواريخ"}</span><small>{date?date.getUTCFullYear():""}</small></div><input aria-label={`${label} تاريخ`} type="date" value={value} onChange={e=>onChange(e.target.value)}/></div>;}
export function RefinedOwnerHome(){
  const incoming=useSearchParams();
  const mode=useWebAppearance(),[displayName,setDisplayName]=useState('');
  const appearance=saveWebAppearance;
  const [targetOpen,setTargetOpen]=useState(false),[revision,setRevision]=useState(0),[dailyChart,setDailyChart]=useState(false),[coachByGroups,setCoachByGroups]=useState(false);
  const [filters,setFilters]=useState<Filters|null>(null),[role,setRole]=useState(""),[error,setError]=useState("");
  const [brand,setBrand]=useState<{displayName:string;logoUrl:string|null;primaryColor?:string}>({displayName:"الأكاديمية",logoUrl:null}),[branches,setBranches]=useState<{id:string;arabicName:string}[]>([]);
  const [data,setData]=useState<Partial<HomeData>>({}),[sourceErrors,setSourceErrors]=useState<Partial<Record<HomeSource,boolean>>>({});
  const retrySource=useRef<(key:HomeSource)=>void>(()=>{});
  useEffect(()=>{const controller=new AbortController(),signal=controller.signal;
    get<{role:string;displayName:string}>("/api/v1/me",signal).then(me=>{if(signal.aborted)return;setRole(me.role);setDisplayName(me.displayName);if(me.role==="AcademyOwner")get<{branches:typeof branches}>("/api/v1/manage/structure/options",signal).then(o=>{if(!signal.aborted)setBranches(o.branches);}).catch(()=>undefined);}).catch(()=>{if(!signal.aborted)setError("تعذر تحميل هوية حسابك.");});
    get<typeof brand>("/api/v1/academy/experience",signal).then(branding=>{if(!signal.aborted)setBrand(branding);}).catch(()=>undefined);
    get<{today:string;trailingMonthFrom:string}>("/api/v1/academy/calendar",signal).then(calendar=>{if(!signal.aborted)setFilters({from:incoming.get("allDates")==="true"?"":incoming.get("from")??calendar.trailingMonthFrom,to:incoming.get("allDates")==="true"?"":incoming.get("to")??calendar.today,branchId:incoming.get("branchId")??""});}).catch(()=>{if(!signal.aborted)setError("تعذر تحميل تاريخ الأكاديمية.");});
    return()=>controller.abort();
    // Search parameters initialize one coherent scope; subsequent edits remain local.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  },[]);
  useEffect(()=>{if(!filters||role!=="AcademyOwner")return;const c=new AbortController(),q=params(filters);
    const query=new URLSearchParams({...filters.branchId?{branchId:filters.branchId}:{},...filters.to?{asOf:filters.to}:{}});
    const sources:Record<HomeSource,string>={summary:`/api/v1/reports/owner-summary?${q}&includeSubscriptionAnalytics=false`,cockpit:`/api/v1/reports/owner-cockpit?${q}`,statement:`/api/v1/reports/operating-statement?${q}&includeMonthly=true`,vip:`/api/v1/reports/vip-members?${query}`,players:`/api/v1/reports/featured-players?${query}`};
    const load=(key:HomeSource)=>{get<unknown>(sources[key],c.signal).then(value=>{if(!c.signal.aborted){setSourceErrors(previous=>({...previous,[key]:false}));setData(previous=>({...previous,[key]:key==="vip"||key==="players"?(value as {items:unknown[]}).items:value}));}}).catch(()=>{if(!c.signal.aborted)setSourceErrors(previous=>({...previous,[key]:true}));});};
    retrySource.current=load;for(const key of Object.keys(sources) as HomeSource[])load(key);return()=>c.abort();
  },[filters,role,revision]);
  if(role==="AcademyAdmin")return <OperationalAdminHome logoUrl={brand.logoUrl} primaryColor={brand.primaryColor}/>;if(!filters||!role)return error?<ErrorState message={error}/>:<LoadingState/>;if(role!=="AcademyOwner")return <SportsQuickActions showReports={false}/>;
  function applyFilters(value:Filters){setData({});setSourceErrors({});setFilters(value);window.history.replaceState(null,"",`${window.location.pathname}?${params(value)}`);}
  const q=params(filters),report=(extra:Record<string,string>={})=>`/dashboard/reports/financial?${params({...filters,...extra})}`;
  const queue=(type="")=>`/dashboard/subscriptions/payments?${params({...filters,type,status:"Pending"})}`;
  const scoped=(url:string)=>`${url}${url.includes('?')?'&':'?'}${q}`;
  const title=(text:string,icon:React.ReactNode,href?:string,action="عرض الكل")=><div className={styles.section}>{icon}<h2>{text}</h2>{href&&<Link href={href}>{action}</Link>}</div>;
  const change=data.cockpit?.collections.changePercentage,summary=data.summary;
  const coaches=[...(summary?.operations.coachPerformance??[])].sort((a,b)=>(coachByGroups?b.groupCount-a.groupCount:(b.parentRating??-1)-(a.parentRating??-1))||a.name.localeCompare(b.name,"ar"));
  const decisions=data.cockpit&&summary?ownerDecisionRecords({...data.cockpit,groupAttention:summary!.operations.groupAttention,underAssignedCoaches:summary!.operations.underAssignedCoaches},filters).slice(0,3):[];
  return <div className={styles.home} data-approved-home="true" data-mode={mode} style={{'--accent':brand.primaryColor&&brand.primaryColor.toUpperCase()!=='#D71935'?brand.primaryColor:mode==='dark'?'#3B6CF0':'#C8102E'} as CSSProperties}>
    <header className={styles.header}>{brand.logoUrl&&
      // eslint-disable-next-line @next/next/no-img-element -- trusted academy logo endpoint
      <img src="/api/v1/academy/branding/logo" alt="شعار الأكاديمية"/>}<div><h1>الرئيسية</h1><p>{brand.displayName} · {branches.find(b=>b.id===filters.branchId)?.arabicName??"كل الفروع"}</p></div>
<div className={styles.tools}><ExperienceTools/><OwnerWebProfile name={displayName} mode={mode} onMode={appearance}/></div></header>
    <section className={styles.quickActions} aria-label="عمليات الأكاديمية"><SportsQuickActions/></section>
    <SubscriptionHome branchId={filters.branchId} asOf={filters.to||undefined} onBranchChange={id=>applyFilters({...filters,branchId:id})} logoUrl={brand.logoUrl} primaryColor={brand.primaryColor}/>
    {error&&<><ErrorState message={error}/><button onClick={()=>{setError("");setRevision(v=>v+1);}}>إعادة تحميل المؤشرات</button></>}
    <div className={styles.filters}><DateTile label="من" value={filters.from} onChange={v=>applyFilters({...filters,from:v})}/><DateTile label="إلى" value={filters.to} onChange={v=>applyFilters({...filters,to:v})}/><button onClick={()=>applyFilters({from:"",to:"",branchId:""})}>مسح الفلاتر</button></div>
    {(Object.keys(homeLabels) as HomeSource[]).filter(key=>!data[key]).map(key=><section className={styles.panel} key={key} aria-label={homeLabels[key]}>{sourceErrors[key]?<><ErrorState message={`تعذر تحميل ${homeLabels[key]}.`}/><button onClick={()=>{setSourceErrors(previous=>({...previous,[key]:false}));retrySource.current(key);}}>إعادة تحميل {homeLabels[key]}</button></>:<div className={styles.pendingSection} role="status" aria-label={`جارٍ تحميل ${homeLabels[key]}`}><span/><span/><span/></div>}</section>)}
    <>
      {summary&&summary.operations.openSafetyFeedback>0&&<Link className={styles.notice} href={`/dashboard/feedback?safetyOnly=true&status=Unresolved&branchId=${filters.branchId}`}>بلاغات سلامة تحتاج مراجعتك: {summary.operations.openSafetyFeedback}</Link>}
      <div className={styles.mainGrid}>
        {data.cockpit&&<section className={`${styles.panel} ${styles.academySummary}`} aria-label="ملخص الأكاديمية"><div className={styles.hero}>{title("ملخص الأكاديمية",<CalendarCheck size={20}/>)}<span className={styles.eyebrow}>التحصيل خلال الفترة</span><Link className={styles.amount} href={report()}>{<MoneyValue amount={data.cockpit.collections.net} currency={data.cockpit.currency}/>}</Link>
          {change!=null&&<div className={`${styles.growth} ${change<0?styles.decline:""}`}>{change<0?<ArrowDownRight size={20}/>:<ArrowUpRight size={20}/>}<b>{n(Math.abs(change))}%</b><span>{change<0?"أقل":"أعلى"} من الفترة السابقة</span></div>}
          {data.cockpit.target.amount!=null&&data.cockpit.target.achievement!=null&&<div className={styles.target}><div className={styles.targetRing} role="progressbar" aria-label="تحقيق هدف التحصيل" aria-valuenow={data.cockpit.target.achievement} aria-valuemin={0} aria-valuemax={100} style={{"--target-progress":`${Math.min(100,Math.max(0,data.cockpit.target.achievement))}%`} as CSSProperties}><b>{n(data.cockpit.target.achievement)}%</b></div><div><span>هدف الفترة</span><strong><MoneyValue amount={data.cockpit.target.amount} currency={data.cockpit.currency}/></strong></div></div>}
          <button className={styles.heroTargetLink} disabled={!filters.from||!filters.to} onClick={()=>setTargetOpen(true)}><Flag size={13}/> ضبط هدف التحصيل</button>
          <OwnerHomeTrend points={data.cockpit.collections.weekly??[]} currency={data.cockpit.currency} href={p=>report({from:p.from!,to:p.to!})}/>
        </div>
        <div className={styles.kpis} aria-label="مؤشرات الأكاديمية">{[
          {label:"مستحقات متأخرة",value:money(data.cockpit.overdue.amount,data.cockpit.currency),icon:Coins,tone:styles.red,href:scoped("/dashboard/finance/receivables?state=overdue")},
          {label:"أعضاء يحتاجون متابعة",value:n(data.cockpit.risk.players),icon:HeartPulse,tone:styles.amber,href:scoped("/dashboard/finance/renewals")},
          {label:"أماكن تدريب متاحة",value:(data.cockpit.capacity.unknownGroups??0)>0?"غير مكتمل":n(data.cockpit.capacity.seats),icon:UsersRound,tone:styles.blue,href:scoped("/dashboard/operations/capacity")},
          {label:"متوسط الحضور",value:data.cockpit.attendance.percentage==null?"—":`${n(data.cockpit.attendance.percentage)}%`,icon:CalendarCheck,tone:styles.green,href:scoped("/dashboard/reports/attendance")},
        ].map(k=><Link key={k.label} className={`${styles.kpi} ${k.tone}`} href={k.href}><k.icon size={26}/><strong>{k.value}</strong><span>{k.label}</span></Link>)}</div></section>}
        {summary&&<section className={`${styles.panel} ${styles.collectionPanel}`} aria-label="التحصيلات ومصادرها">{title("التحصيلات",<Coins size={20}/>)}
          <div className={styles.collections}>{[true,false].map(daily=><div className={styles.collection} key={String(daily)}><h3>{daily?"التحصيل اليومي":"التحصيل الشهري"}</h3><small dir="ltr">{daily?summary!.collectionDay:`${summary!.collectionMonthFrom} — ${summary!.collectionDay}`}</small><Link href={report({from:daily?summary!.collectionDay:summary!.collectionMonthFrom,to:summary!.collectionDay})}><MoneyValue amount={daily?summary!.collections.today:summary!.collections.month} currency={summary!.currency}/></Link><div className={styles.splits}>{[{method:"Cash",label:"نقدي",value:daily?summary!.collections.todayCash:summary!.collections.monthCash},{method:"Online",label:"إلكتروني",value:daily?summary!.collections.todayOnline:summary!.collections.monthOnline},...((daily?summary!.collections.todayUnspecified:summary!.collections.monthUnspecified)!==0)?[{method:"Unspecified",label:"طريقة صرف قديمة غير موثقة",value:daily?summary!.collections.todayUnspecified:summary!.collections.monthUnspecified}]:[]].map(m=><Link key={m.method} href={report({from:daily?summary!.collectionDay:summary!.collectionMonthFrom,to:summary!.collectionDay,provider:m.method})}><b>{n(m.value)}</b><span>{m.label}</span></Link>)}</div></div>)}</div>
          <div className={styles.chartTabs} role="group" aria-label="فترة رسم التحصيل"><button aria-pressed={dailyChart} onClick={()=>setDailyChart(true)}>اليوم</button><button aria-pressed={!dailyChart} onClick={()=>setDailyChart(false)}>الشهر</button></div>
          <CollectionBreakdown title="حسب الفرع" rows={(dailyChart?summary!.collections.todayBranches:summary!.collections.monthBranches)?.map(b=>({key:b.id??'unknown',label:b.name,amount:b.amount,href:b.id?report({from:dailyChart?summary!.collectionDay:summary!.collectionMonthFrom,to:summary!.collectionDay,branchId:b.id}):undefined}))??[]} currency={summary!.currency}/>
          <CollectionBreakdown title="مصادر التحصيل" rows={(dailyChart?summary!.collections.todaySources:summary!.collections.monthSources)?.map(s=>({key:s.key,label:s.label,amount:s.amount,href:report({from:dailyChart?summary!.collectionDay:summary!.collectionMonthFrom,to:summary!.collectionDay,collectionType:s.collectionType,...s.subscriptionKind?{subscriptionKind:s.subscriptionKind}:{}})}))??[]} currency={summary!.currency}/>
        </section>}
        {data.statement&&<OwnerOperatingPanel data={data.statement} href={scoped} monthHref={month=>`/dashboard/finance/operating-statement?${params({...filters,from:month.from,to:month.to})}`}/> }
      </div>
      <div className={styles.secondaryGrid}>{summary&&<section className={styles.paymentQueue}>{title("طلبات الدفع",<ReceiptText size={20}/>,queue())}<div className={styles.subscriptionHeader}><span>بانتظار المراجعة</span><Link href={queue()}><strong>{n(summary!.paymentRequests.total)}</strong></Link></div><div className={styles.queueRows}>{[{type:"Enrollment",label:"اشتراكات جديدة",value:summary!.paymentRequests.newEnrollments},{type:"Renewal",label:"تجديد الاشتراك",value:summary!.paymentRequests.renewals},{type:"Product",label:"منتجات رياضية",value:summary!.paymentRequests.products},...summary!.paymentRequests.tournaments?[{type:"Tournament",label:"بطولات",value:summary!.paymentRequests.tournaments}]:[]].map(r=><Link key={r.type} href={queue(r.type)}><b>{n(r.value)}</b><span>{r.label}</span><span aria-hidden="true">‹</span></Link>)}</div></section>}
      {summary&&data.cockpit&&<section>{title("مركز القرارات الذكية",<Shield size={20}/>,scoped("/dashboard/operations/decisions"),"كل القرارات")}<div className={styles.decisions}>{decisions.map(d=><article className={`${styles.panel} ${styles.decision}`} key={d.title}><Coins className={styles.amber} size={23}/><h3>{d.title}</h3><p>{d.line}</p><Link href={d.href}>{d.action} ←</Link></article>)}</div></section>}
      </div>{summary&&<section aria-label="المدربون">{title(`المدربون · ${summary!.operations.coachPerformance.length}`,<UsersRound size={20}/>,scoped(`/dashboard/academy/coaches?sort=${coachByGroups?"groups":"rating"}`),"كل المدربين")}<div className={styles.chartTabs} role="group" aria-label="ترتيب المدربين">{[false,true].map(groups=><button key={String(groups)} aria-pressed={coachByGroups===groups} onClick={()=>setCoachByGroups(groups)}>{groups?"حسب المجموعات المسندة":"حسب التقييم"}</button>)}</div><div className={styles.carousel}>{coaches.map(c=><Link className={styles.person} key={c.id} href={scoped(`/dashboard/academy/coaches/${c.id}`)}><Portrait name={c.name} reference={c.photoReference}/><h3>{c.name}</h3><small>{(c.branches??[]).map(id=>branches.find(b=>b.id===id)?.arabicName).filter(Boolean).join(" · ")}</small><small>{(c.sports??[]).join(' · ')}</small><small>{n(c.groupCount)} مجموعات · {n(c.sessionCount)} حصص</small><b className={styles.gold}>{c.parentRating==null?"لم يُقيّم بعد":`${n(c.parentRating)} ★ · ${n(c.parentRatingCount)} تقييمات`}</b></Link>)}</div></section>}
      {data.vip&&<section className={styles.vip}>{title("أعضاء مميزون",<Crown className={styles.gold} size={21}/>,`/dashboard/guardians?${new URLSearchParams({sort:"value_desc",branchId:filters.branchId,asOf:filters.to})}`)}<div className={styles.carousel}>{data.vip.slice(0,5).map((g,i)=><Link className={styles.person} href={`/dashboard/guardians/${g.id}`} key={g.id}><span className={styles.rank} aria-label={`الترتيب ${i+1}`}>{i+1}</span><Portrait name={g.displayName} reference={g.photoReference}/><h3>{g.displayName}</h3><strong className={styles.gold}>{money(g.monthlySubscriptionValue,g.currency)}</strong><small>قيمة الاشتراكات الشهرية</small></Link>)}</div><Link className={styles.rewardButton} href="/dashboard/guardians/rewards">إدارة مكافآت الأعضاء المميزين</Link></section>}
      {data.players&&<section>{title("أبرز اللاعبين",<Star className={styles.gold} size={21}/>,`/dashboard/players/featured?${new URLSearchParams({branchId:filters.branchId,asOf:filters.to})}`)}<div className={styles.carousel}>{data.players.slice(0,5).map(p=><Link className={styles.person} href={`/dashboard/players/${p.id}`} key={p.id}><b className={styles.gold}>{n(p.stars)} ★</b><Portrait adult={false} name={p.name} reference={p.photoReference}/><h3>{p.name}</h3><small>{p.branch}</small><strong className={styles.green}>{n(p.score)}</strong></Link>)}</div>{!data.players.length&&<p className={styles.note}>لا توجد تقييمات منشورة مكتملة وقابلة للمقارنة في هذا النطاق.</p>}</section>}
      {targetOpen&&data.cockpit&&<CollectionTargetDialog scope={filters} branchLabel={branches.find(b=>b.id===filters.branchId)?.arabicName??"كل الفروع"} currency={data.cockpit.currency} amount={data.cockpit.target.amount} onClose={()=>setTargetOpen(false)} onSaved={()=>{setTargetOpen(false);setData({});setSourceErrors({});setRevision(x=>x+1);}}/>}
    </>
  </div>;
}

function CollectionBreakdown({title,rows,currency}:{title:string;rows:{key:string;label:string;amount:number;href?:string}[];currency:string}) {
 const maximum=Math.max(1,...rows.map(r=>Math.abs(r.amount)));
 return <div className={styles.collectionBreakdown} role="group" aria-label={title}><h3>{title}</h3>{rows.length===0?<p className={styles.note}>لا توجد تحصيلات في هذه الفترة.</p>:rows.map((r,i)=>{const content=<><span>{r.label}</span><b className={r.amount<0?styles.red:undefined}><MoneyValue amount={r.amount} currency={currency}/></b><i aria-hidden="true"><span style={{width:`${Math.abs(r.amount)/maximum*100}%`,background:r.amount<0?'var(--negative)':['var(--accent)','#22d3ee','#a78bfa'][i%3]}}/></i></>;return r.href?<Link href={r.href} key={r.key}>{content}</Link>:<div key={r.key}>{content}</div>;})}</div>;
}