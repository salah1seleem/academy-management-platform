"use client";
import Link from "next/link";
import {useEffect,useState} from "react";
import {useSearchParams} from "next/navigation";
import {ErrorState,LoadingState,PageHeader,SectionCard} from "../components/dashboard-shell";
import {useDateFilterDefaults} from "../components/date-filter-defaults";
import {MoneyValue} from "../components/money-value";
import {formatMoneyAr} from "../lib/formatters";
import {OperatingMonthlyChart,type OperatingMonth} from "./operating-monthly-chart";
import {ApprovedWorkspace} from "../components/approved-workspace";
import styles from "./operating-statement.module.css";
type Line={key:string;label:string;amount:number;sourceUrl:string};
type Statement={from?:string;to?:string;monthly?:OperatingMonth[]|null;currency:string;basis:"Cash";income:Line[];expenses:Line[];totalIncome:number;totalExpenses:number;surplus:number;refunds:number;discounts:number;receivables:number;approvedPayrollPayable:number;unpaidOperatingExpenses:number;advanceCashOut:number;advanceApplied:number;earnedRevenue:null;warnings:string[];externalReceivables?:number};
const params=(from:string,to:string,branchId:string,sportId="")=>new URLSearchParams({...from?{from}:{},...to?{to}:{},...branchId?{branchId}:{},...sportId?{sportId}:{},...!from&&!to?{allDates:"true"}:{}});
function scopedHref(source:string,q:URLSearchParams){const [path,search]=source.split("?");const result=new URLSearchParams(search);q.forEach((value,key)=>result.set(key,value));return `${path}?${result}`;}
function useStatement(query: string | null, includeMonthly = false) {
 const [revision, setRevision] = useState(0);
 const key = `${revision}:${query}`;
 const [result, setResult] = useState<{ key: string; data?: Statement; status?: number }>();
 useEffect(() => {
  if (query === null) return;
  let controller: AbortController | undefined;
  const load = () => {
   controller?.abort();
   if (document.hidden) return;
   controller = new AbortController();
   const { signal } = controller;
   setResult(undefined);
   void (async () => {
    try {
     const response = await fetch(`/api/v1/reports/operating-statement?${query}${includeMonthly ? "&includeMonthly=true" : ""}`, { signal, cache: "no-store" });
     if (signal.aborted) return;
     if (!response.ok) { setResult({ key, status: response.status }); return; }
     const data = await response.json() as Statement;
     if (!Array.isArray(data?.income) || !Array.isArray(data?.expenses) || !Array.isArray(data?.warnings) ||
       ![data.totalIncome, data.totalExpenses, data.surplus].every(Number.isFinite)) throw new Error();
     if (!signal.aborted) setResult({ key, data });
    } catch { if (!signal.aborted) setResult({ key, status: 0 }); }
   })();
  };
  load();
  window.addEventListener("focus", load);
  document.addEventListener("visibilitychange", load);
  return () => {
   controller?.abort();
   window.removeEventListener("focus", load);
   document.removeEventListener("visibilitychange", load);
  };
 }, [key, query, includeMonthly]);
 const current = query !== null && result?.key === key ? result : undefined;
 const status = current?.status;
 const error = status === undefined ? "" : status === 401 || status === 403 ? "قائمة الأرباح والخسائر غير متاحة لهذا الحساب."
  : status === 400 || status === 422 ? "راجع التواريخ والفلاتر قبل عرض القائمة." : status === 404 ? "الفرع أو الرياضة غير متاحين."
   : "تعذر تحميل قائمة الأرباح والخسائر.";
 return { data: current?.data, error, retry: status === 0 || (status ?? 0) >= 500 ? () => setRevision(value => value + 1) : undefined };
}
export function OperatingStatementReport(){
 const incoming=useSearchParams(),defaults=useDateFilterDefaults(),allDates=incoming.get("allDates")==="true";
 const initial={
   from:incoming.get("from")??(allDates?"":defaults.from),
   to:incoming.get("to")??(allDates?"":defaults.to),
   branch:incoming.get("branchId")??"",
   sport:incoming.get("sportId")??"",
 };
 return <OperatingStatementForm key={params(initial.from,initial.to,initial.branch,initial.sport).toString()} initial={initial}/>;
}
function OperatingStatementForm({initial}:{initial:{from:string;to:string;branch:string;sport:string}}){
 const [from,setFrom]=useState(initial.from),[to,setTo]=useState(initial.to),[branch,setBranch]=useState(initial.branch),[sport,setSport]=useState(initial.sport);
 const [options,setOptions]=useState<{branches:{id:string;arabicName:string}[];sports:{id:string;arabicName:string}[]}>({branches:[],sports:[]});
 const invalid = !!from && !!to && from > to;
 const q = params(from, to, branch, sport);
 const { data, error, retry } = useStatement(invalid ? null : q.toString(), true);
 const applied = q.has("allDates") ? q : params(data?.from ?? from, data?.to ?? to, branch, sport);
 const unpaidScope = new URLSearchParams(applied);
 unpaidScope.delete("from");
 const balances: { label: string; amount?: number; href: string; scope?: URLSearchParams }[] = [
  { label: "مطالبات لاعبين قائمة", amount: data?.receivables, href: "/dashboard/finance/receivables" },
  ...(data?.externalReceivables == null ? [] : [{ label: "رصيد اتفاقات خارجية — مستقل عن الرواتب", amount: data.externalReceivables, href: "/dashboard/finance/external-agreements?state=Open" }]),
  { label: "رواتب معتمدة غير مسددة", amount: data?.approvedPayrollPayable, href: "/dashboard/payroll" },
  { label: "مصروفات مرحلة غير مسددة", amount: data?.unpaidOperatingExpenses, href: "/dashboard/finance/operating-expenses?unpaid=true&status=Posted", scope: unpaidScope },
  { label: "خصومات اشتراكات — داخل صافي التحصيل بالفعل", amount: data?.discounts, href: "/dashboard/reports/financial?collectionType=Subscription" },
  { label: "استردادات — خُصمت مرة واحدة أعلاه", amount: data?.refunds, href: "/dashboard/reports/financial?collectionType=Refund" },
 ];
 useEffect(()=>{const c=new AbortController();fetch("/api/v1/manage/structure/options",{signal:c.signal,cache:"no-store"}).then(r=>r.ok?r.json():{branches:[],sports:[]}).then(setOptions).catch(()=>{});return()=>c.abort();},[]);
 return <ApprovedWorkspace><div className={styles.report}><PageHeader title="قائمة الأرباح والخسائر" context="الحسابات والماليات" back={{label:"العودة إلى الماليات",href:"/dashboard/finance"}}/><div className="report-filters operating-statement-filters"><label>من تاريخ<input type="date" aria-invalid={invalid} aria-describedby={invalid ? "operating-range-error" : undefined} value={from} onChange={e=>setFrom(e.target.value)}/></label><label>إلى تاريخ<input type="date" aria-invalid={invalid} aria-describedby={invalid ? "operating-range-error" : undefined} value={to} onChange={e=>setTo(e.target.value)}/></label><label>الفرع<select aria-label="الفرع" value={branch} onChange={e=>setBranch(e.target.value)}><option value="">كل الفروع</option>{options.branches.map(b=><option key={b.id} value={b.id}>{b.arabicName}</option>)}</select></label><label>الرياضة<select aria-label="الرياضة" value={sport} onChange={e=>setSport(e.target.value)}><option value="">كل الرياضات</option>{options.sports.map(b=><option key={b.id} value={b.id}>{b.arabicName}</option>)}</select></label><button type="button" onClick={()=>{setFrom("");setTo("");setBranch("");setSport("");}}>مسح</button>{data && <a className="secondary-button" href={`/api/v1/reports/operating-statement?${applied}&format=csv`} download>تصدير القائمة نفسها</a>}</div>
 {invalid ? <div id="operating-range-error"><ErrorState message="تاريخ البداية يجب أن يسبق تاريخ النهاية أو يساويه." /></div>
  : error ? <><ErrorState message={error}/>{retry && <button type="button" className="secondary-button" onClick={retry}>إعادة المحاولة</button>}</>
   :!data?<LoadingState/>:<>
 <details className="statement-basis"><summary>على أساس التحصيل والصرف · معلومات التقرير</summary><p>المقبوضات والمدفوعات الفعلية بعد الاسترداد. المستحقات غير المحصلة والرواتب والمصروفات غير المسددة أرصدة منفصلة؛ الفائض النقدي ليس ربحًا نهائيًا على أساس الاستحقاق.</p></details>
 <OperatingMonthlyChart points={data.monthly??[]} currency={data.currency} href={month=>`/dashboard/finance/operating-statement?${params(month.from,month.to,branch,sport)}`}/>
 <div className="operating-statement-grid">{[["المقبوضات التشغيلية",data.income,data.totalIncome],["المدفوعات التشغيلية",data.expenses,data.totalExpenses]].map(([title,lines,total])=><SectionCard key={String(title)} title={String(title)}><div className="operating-statement-lines">{(lines as Line[]).filter(l=>l.amount!==0).map(l=><Link key={l.key} href={scopedHref(l.sourceUrl,applied)}><span>{l.label}</span><bdi><MoneyValue amount={l.amount} currency={data.currency}/></bdi></Link>)}<div className="operating-statement-total"><strong>الإجمالي</strong><bdi>{formatMoneyAr(Number(total),data.currency)}</bdi></div></div></SectionCard>)}</div>
 <section className={`operating-result ${data.surplus<0?"deficit":""}`}><div><small>الفائض / العجز التشغيلي النقدي</small><h2>{formatMoneyAr(data.surplus,data.currency)}</h2></div><span>ليس ربحًا نهائيًا على أساس الاستحقاق</span></section>
 <section className="list-card"><h2>أرصدة وتفاصيل منفصلة عن النتيجة</h2><div className="statement-reconciliation">{balances.map(({label,amount,href,scope})=><Link key={label} href={scopedHref(href,scope ?? applied)}><small>{label}</small><strong>{formatMoneyAr(amount,data.currency)}</strong></Link>)}</div><details><summary>السلف وأساس احتساب الأرقام</summary><p>صرف سلف: {formatMoneyAr(data.advanceCashOut,data.currency)} · تخصيص سلف: {formatMoneyAr(data.advanceApplied,data.currency)}. منفصلان عن الصرف المباشر؛ لا نكرر خروج المال عند التخصيص.</p></details></section>
 <details className="detail-card"><summary>اكتمال البيانات وأساس التقرير</summary><ul>{data.warnings.map(w=><li key={w}>{w}</li>)}</ul></details>
 </>}</div></ApprovedWorkspace>;
}
export function OperatingStatementSnapshot({from,to,branchId=""}:{from:string;to:string;branchId?:string}){const q=params(from,to,branchId),{data,error}=useStatement(q.toString());return <SectionCard title="لقطة النتيجة التشغيلية" action={<Link href={`/dashboard/finance/operating-statement?${q}`}>القائمة الكاملة</Link>}>{error?<p>{error}</p>:!data?<LoadingState/>:<div className="statement-snapshot" data-negative={data.surplus<0}><strong className="command-value money-value">{formatMoneyAr(data.surplus,data.currency)}</strong><small>فائض / عجز نقدي · وليس ربح الاستحقاق</small><p>مقبوض {formatMoneyAr(data.totalIncome,data.currency)} · مدفوع {formatMoneyAr(data.totalExpenses,data.currency)}</p><small>راجع نطاق التكاليف ومصادر الأرقام في القائمة.</small></div>}</SectionCard>;}
