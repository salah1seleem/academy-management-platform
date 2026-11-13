"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { FormEvent, useEffect, useMemo, useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge, useDashboardPermission } from "../components/dashboard-shell";
import { ApprovedWorkspace } from "../components/approved-workspace";
import styles from "./financial-report.module.css";
import { MoneyValue } from "../components/money-value";
import { DailyStaffReport } from "./daily-staff-report";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { useReceiptRead } from "./receipt-read";
import { formatDateAr, formatReceiptTimestampAr } from "../lib/formatters";
import { financialLedgerScope } from "../lib/financial-ledger-query";

type Option = { id: string; arabicName: string; branchId?: string; sportId?: string };
type FinancialRow = { collectionType?: string; collectionId: string; receiptId: string; receiptNumber: string; player: string; sport: string; branch: string; group: string; plan: string; amount: number; refundAmount?: number; netAmount?: number; currency: string; paymentMethod: string; provider: string; confirmedAtUtc: string; confirmedAtAcademy?: string | null; historicalPaidOn?: string | null };
type FinancialSource = {key:string;label:string;amount:number};
type FinancialResponse = { sourceSummary?:FinancialSource[]; page?:number; pageSize?:number; from: string; to: string; totalAmount: number; totalRefunds?: number; netAmount?: number; totalCount: number; currency: string; items: FinancialRow[] };
type AttendanceRow = { id: string; date: string; time: string; playerCode?: string; player?: string; birthYear?: number; staff?: string; role?: string; sport: string; branch: string; group: string; status: string; sessionConsumed?: boolean };
type AttendanceResponse = { subject: string; from: string; to: string; coverage: string; totalCount: number; items: AttendanceRow[] };
type Filters = Record<string, string>;

const money = (value: number, currency: string) => `${new Intl.NumberFormat("ar-EG-u-nu-latn", { maximumFractionDigits: 2 }).format(value)} ${currency === "EGP" ? "جنيه" : currency}`;
const statusLabels: Record<string, string> = { Present: "حاضر", Late: "متأخر", Excused: "معتذر", Absent: "غائب", NotRecorded: "لم يُسجل", AcademyOwner: "مالك", AcademyAdmin: "إداري", Coach: "مدرب" };

export { PremiumOwnerDashboard as OwnerDashboard } from "./owner-home";

function financialParams(filters: Filters) {
  const query = financialLedgerScope(new URLSearchParams(params(filters)));
  query.delete("page");
  if (filters.from || filters.to) query.delete("allDates");
  return query.toString();
}

export function FinancialReport() {
  const incoming = useSearchParams(); const defaults = useDateFilterDefaults();
  const initialQuery = useMemo(() => {
    const scope = Object.fromEntries(financialLedgerScope(incoming));
    if (!scope.from && !scope.to && scope.allDates !== "true") Object.assign(scope, defaults);
    return financialParams(scope);
  }, [incoming, defaults]);
  const initialPage = Math.min(100000, Math.max(1, Number.parseInt(incoming.get("page") ?? "1", 10) || 1));
  return <FinancialReportForm key={`${initialQuery}#${initialPage}`} initialQuery={initialQuery} initialPage={initialPage}/>;
}
function FinancialReportForm({initialQuery, initialPage}:{initialQuery:string; initialPage:number}) {
  const canExport = useDashboardPermission("reports.export");
  const [filters,setFilters]=useState<Filters>(()=>Object.fromEntries(new URLSearchParams(initialQuery)));
  const [appliedQuery,setAppliedQuery]=useState(initialQuery),[page,setPage]=useState(initialPage),[revision,setRevision]=useState(0);
  const [result,setResult]=useState<{key:string;value:FinancialResponse}|null>(null),[failure,setFailure]=useState<{key:string;status:number}|null>(null);
  const [filterError, setFilterError] = useState("");
  const [options,setOptions]=useState<{sports:Option[];branches:Option[];groups:Option[];coaches?:Option[]}>({sports:[],branches:[],groups:[],coaches:[]}),[plans,setPlans]=useState<Option[]>([]);
  const query=useMemo(()=>financialParams(filters),[filters]),requestKey=`${appliedQuery}#${page}#${revision}`;
  const data=result?.key===requestKey?result.value:null,error=failure?.key===requestKey?failure:null;
  const applied = new URLSearchParams(appliedQuery);
  const documentScope = new URLSearchParams(applied);
  documentScope.set("page", String(page));
  documentScope.set("report", "financial");
  useEffect(() => {
    const controller = new AbortController();
    fetch(`/api/v1/reports/financial?${appliedQuery}&page=${page}&pageSize=25`, {signal:controller.signal,cache:"no-store"})
      .then(async response => {
        if (controller.signal.aborted) return;
        if (!response.ok) { setFailure({key:requestKey,status:response.status}); return; }
        const value = await response.json() as FinancialResponse;
        if (controller.signal.aborted) return;
        if (!Array.isArray(value?.items)) { setFailure({key:requestKey,status:0}); return; }
        setResult({key:requestKey,value});
        setFailure(null);
      }).catch(() => { if (!controller.signal.aborted) setFailure({key:requestKey,status:0}); });
    return () => controller.abort();
  }, [appliedQuery,page,requestKey]);
  useEffect(()=>{const controller=new AbortController();fetch("/api/v1/manage/structure/options",{signal:controller.signal,cache:"no-store"}).then(r=>r.ok?r.json():Promise.reject()).then(value=>{if(!controller.signal.aborted)setOptions(value);}).catch(()=>undefined);fetch("/api/v1/subscriptions/plans",{signal:controller.signal,cache:"no-store"}).then(r=>r.ok?r.json():Promise.reject()).then(value=>{if(!controller.signal.aborted)setPlans(value);}).catch(()=>undefined);return()=>controller.abort();},[]);
  function load(next = query) { setFilterError(""); setAppliedQuery(next); setPage(1); setRevision(v => v + 1); }
  function submit(e: FormEvent) {
    e.preventDefault();
    const invalid = filters.to === "9999-12-31" ? "اختر تاريخ نهاية صالحًا." : filters.from && filters.to ? receiptRangeError(filters.from, filters.to) : "";
    if (invalid) { setFilterError(invalid); return; }
    load();
  }
  function source(key: string) {
    const category = {collectionType:key,subscriptionKind:"",externalKind:""};
    setFilters(current => ({...current,...category}));
    load(financialParams({...Object.fromEntries(applied),...category}));
  }
  return <ApprovedWorkspace><PageHeader title="الإيرادات والتحصيلات" context="التقارير / التحصيلات المؤكدة" /><form className="report-filters financial-ledger-filters" onSubmit={submit}>
    <DateField label="من تاريخ" value={filters.from} set={v=>setFilters({...filters,from:v})}/>
    <DateField label="إلى تاريخ" value={filters.to} set={v=>setFilters({...filters,to:v})}/>
    <SelectFilter label="الفرع" value={filters.branchId} set={v=>setFilters({...filters,branchId:v})} options={options.branches}/>
    <label>{filters.collectionType==="External"?"البحث بالطرف أو الاتفاق":"البحث باللاعب"}<input value={filters.search??""} onChange={e=>setFilters({...filters,search:e.target.value})}/></label>
    <details className="report-advanced"><summary>فلاتر إضافية{["sportId","planId","collectionType","provider"].some(key=>applied.has(key))&&<span className="status-badge status-info">مطبقة</span>}</summary><div>
      <SelectFilter label="الرياضة" value={filters.sportId} set={v=>setFilters({...filters,sportId:v})} options={options.sports}/>
      <SelectFilter label="الباقة" value={filters.planId} set={v=>setFilters({...filters,planId:v})} options={plans}/>
      <label>نوع التحصيل<select value={filters.collectionType??""} onChange={e=>setFilters({...filters,collectionType:e.target.value,subscriptionKind:"",externalKind:""})}><option value="">الكل</option><option value="Subscription">اشتراكات</option><option value="Product">منتجات رياضية</option><option value="Tournament">رسوم بطولات</option><option value="DayPass">زيارات يومية</option><option value="External">اتفاقيات وتأجير</option><option value="Refund">استردادات الاشتراكات</option></select></label>
      {filters.collectionType==="Subscription"&&<label>مصدر الاشتراك<select value={filters.subscriptionKind??""} onChange={e=>setFilters({...filters,subscriptionKind:e.target.value})}><option value="">الكل</option><option value="Enrollment">اشتراكات جديدة</option><option value="Renewal">تجديد الاشتراكات</option><option value="Unclassified">اشتراكات غير مصنفة</option></select></label>}
      {filters.collectionType==="External"&&<label>نوع الاتفاق<select aria-label="نوع الاتفاق" value={filters.externalKind??""} onChange={e=>setFilters({...filters,externalKind:e.target.value})}><option value="">الكل</option><option value="PoolRental">تأجير مسبح / حارة</option><option value="ExternalProgram">برنامج خارجي</option></select></label>}
      <label>طريقة الدفع<select aria-label="طريقة الدفع" value={filters.provider??""} onChange={e=>setFilters({...filters,provider:e.target.value})}><option value="">الكل</option><option value="Cash">نقدي</option><option value="Online">إلكتروني</option><option value="Unspecified">طريقة صرف قديمة غير موثقة</option></select></label>
    </div></details>
    <div className="report-filter-actions"><button className="primary-button">تطبيق الفلاتر</button><button type="button" className="secondary-button" onClick={()=>{setFilters({from:"",to:""});load("allDates=true");}}>مسح</button>{canExport !== false && error?.status !== 401 && error?.status !== 403 && <a className="secondary-button export-link" href={`/api/v1/reports/financial/export?${appliedQuery}`} download>تصدير CSV</a>}</div>
  </form>
  {filterError && <ErrorState message={filterError} />}
  {error ? <>
    <ErrorState message={error.status === 403 || error.status === 401 ? "التقرير المالي غير متاح لهذا الحساب." : error.status === 400 ? "راجع التواريخ والفلاتر قبل عرض التقرير المالي." : "تعذر تحميل التقرير المالي."} />
    {![400,401,403].includes(error.status) && <button type="button" className="secondary-button" onClick={()=>setRevision(v=>v+1)}>إعادة المحاولة</button>}
  </> : !data ? <LoadingState /> : <section className="list-card">
    <FinancialSourceTiles sources={data.sourceSummary??[]} currency={data.currency} selected={applied.get("collectionType")??""} onSelect={source}/>
    <h2>سجل التحصيلات</h2>
    <div className="report-total"><span>إجمالي التحصيل المؤكد</span><strong><MoneyValue amount={data.totalAmount} currency={data.currency}/></strong><small>{data.totalCount} عملية · استردادات {money(data.totalRefunds ?? 0, data.currency)} · صافي {money(data.netAmount ?? data.totalAmount, data.currency)}</small></div>
    {data.items.length === 0 ? <EmptyState message="لا توجد تحصيلات مؤكدة أو استردادات ضمن الفلاتر." /> : <FinancialCollectionTable rows={data.items} documentScope={documentScope} />}
    {(data.totalCount > 25 || page > 1) && <nav aria-label="صفحات سجل التحصيلات" className="form-actions"><button disabled={page<=1} onClick={()=>setPage(v=>v-1)}>السابق</button><span>الصفحة {page} · {data.totalCount} سجل</span><button disabled={page*25>=data.totalCount} onClick={()=>setPage(v=>v+1)}>التالي</button></nav>}
  </section>}</ApprovedWorkspace>;
}

function FinancialCollectionTable({rows, documentScope}:{rows:FinancialRow[]; documentScope:URLSearchParams}) {
  return <AdminDataTable label="سجل التحصيلات" rows={rows} rowKey={row=>row.collectionId} paginate={false} columns={[
    {key:"beneficiary",header:"المستفيد والمصدر",primary:true,width:"minmax(0,1.4fr)",render:row=><div className="cell-stack"><strong>{row.player}</strong>{row.collectionType==="External"&&<small>طرف اتفاق خارجي · ليس تسجيل لاعب</small>}{(row.collectionType==="Refund"||row.collectionType==="Void")&&<small>{row.collectionType==="Void"?"عكس إيصال نقدي · الفترة ملغاة":"استرداد اشتراك · بتاريخ الصرف"}</small>}</div>},
    {key:"branch",header:"الفرع والرياضة",render:row=><span>{row.branch}<small>{row.sport}</small></span>},
    {key:"service",header:"الخدمة",render:row=><span>{row.plan}<small>{row.group}</small></span>},
    {key:"receipt",header:"رقم الإيصال",render:row=><bdi dir="ltr" className={styles.receiptNumber}>{row.receiptNumber}</bdi>},
    {key:"date",header:"التاريخ",render:row=>{
      if (row.historicalPaidOn || row.provider === "HistoricalImport") return <span><time dateTime={row.historicalPaidOn ?? undefined}>{formatDateAr(row.historicalPaidOn)}</time><small>يوم القبض</small></span>;
      const stamp = formatReceiptTimestampAr(row.confirmedAtUtc, row.confirmedAtAcademy);
      return stamp ? <span><time dateTime={row.confirmedAtUtc}>{stamp.text}</time><small>{stamp.zone}</small></span> : "غير متاح";
    }},
    {key:"method",header:"طريقة التحصيل",render:row=><span>{row.paymentMethod==="Online"?"دفع إلكتروني":row.paymentMethod==="Cash"?"نقدي":row.paymentMethod==="Unspecified"?"طريقة غير موثقة":row.paymentMethod} / {row.historicalPaidOn || row.provider === "HistoricalImport" ? "تحصيل سابق" : row.provider==="InternalTest"||row.provider==="Internal"?"بوابة الاختبار الداخلية":row.provider==="Manual"?"تسجيل يدوي":row.provider}</span>},
    {key:"amount",header:"الصافي",render:row=><span><span className="report-amount"><MoneyValue amount={row.netAmount??row.amount-(row.refundAmount??0)} currency={row.currency}/></span>{Boolean(row.refundAmount)&&<small>استرداد {money(row.refundAmount!,row.currency)}</small>}</span>},
    {key:"action",header:"المستند",width:"80px",render:row=><Link className="row-primary-action" href={row.collectionType==="External"?`/dashboard/finance/external-agreements/${row.receiptId}/receipts/${row.collectionId}`:row.collectionType==="Product"?`/dashboard/product-sales/${row.collectionId}`:row.collectionType==="Tournament"?`/dashboard/tournament-payments/${row.collectionId}`:row.collectionType==="DayPass"?"/dashboard/facilities":`/dashboard/subscriptions/receipts/${row.receiptId}?${documentScope}`}>{row.collectionType==="Product"?"المنتج":row.collectionType==="DayPass"?"المرفق":"الإيصال"}</Link>},
  ]}/>;
}

export function FinancialSourceTiles({sources,currency,selected,onSelect}:{sources:FinancialSource[];currency:string;selected:string;onSelect:(key:string)=>void}) {
 return <section className={styles.sources} aria-label="مصادر التحصيل"><h2>مصادر التحصيل</h2><div className={styles.grid}>{sources.filter(s=>s.amount!==0).map(s=><button type="button" className={`${styles.source} ${s.amount<0?styles.negative:""}`} key={s.key} aria-pressed={selected===s.key} onClick={()=>onSelect(s.key)}><span>{s.label}</span><strong><MoneyValue amount={s.amount} currency={currency}/></strong></button>)}</div>{selected&&<button type="button" onClick={()=>onSelect("")}>عرض سجل كل المصادر</button>}<details><summary>أساس المبالغ المحصلة</summary><p>ملخص المصادر وسجل التحصيلات يستخدمان الفلاتر نفسها لكل النتائج. الاسترداد بالسالب، وطلب الدفع المعلق لا يُعد تحصيلًا.</p></details></section>;
}

export function AttendanceReport() {
  const incoming = useSearchParams(); const defaults = useDateFilterDefaults();
  const [filters, setFilters] = useState<Filters>({ subject: incoming.get("subject") === "staff" ? "staff" : "players", from: incoming.get("from") ?? (incoming.get("allDates") === "true" ? "" : defaults.from), to: incoming.get("to") ?? (incoming.get("allDates") === "true" ? "" : defaults.to), coachId: "", sportId: "", branchId: incoming.get("branchId") ?? "", groupId: "", status: incoming.get("status") ?? "", search: "", birthYear: "" }); const [data, setData] = useState<AttendanceResponse | null>(null); const [options, setOptions] = useState<{ sports: Option[]; branches: Option[]; groups: Option[]; coaches?: Option[] }>({ sports: [], branches: [], groups: [] }); const [error, setError] = useState(""); const query = useMemo(() => params(filters), [filters]);
  function load(next = query) { setData(null); setError(""); fetch(`/api/v1/reports/attendance?${next}`).then(async r => { if (!r.ok) throw new Error(); const value = await r.json() as AttendanceResponse; setData(value); }).catch(() => setError("تعذر تحميل تقرير الحضور.")); }
  useEffect(() => { fetch(`/api/v1/reports/attendance?${params({ subject: "players", from: incoming.get("from") ?? (incoming.get("allDates") === "true" ? "" : defaults.from), to: incoming.get("to") ?? (incoming.get("allDates") === "true" ? "" : defaults.to), branchId: incoming.get("branchId") ?? "", status: incoming.get("status") ?? "" })}`).then(async r => { if (!r.ok) throw new Error(); const value = await r.json() as AttendanceResponse; setData(value); }).catch(() => setError("تعذر تحميل تقرير الحضور.")); fetch("/api/v1/manage/structure/options").then(r => r.ok ? r.json() : Promise.reject()).then(setOptions).catch(() => undefined); }, [incoming, defaults.from, defaults.to]);
  function submit(e: FormEvent) { e.preventDefault(); load(); }
  if (filters.subject === "staff") return <DailyStaffReport initialRange={{ from: filters.from, to: filters.to }} onPlayers={() => setFilters({ subject: "players", from: defaults.from, to: defaults.to, sportId: "", branchId: "", groupId: "", status: "", search: "", birthYear: "" })} />;
  return <><PageHeader title="تقارير الحضور" context="التقارير / الحضور الفعلي" /><form className="report-filters" onSubmit={submit}><label>نوع التقرير<select aria-label="نوع التقرير" value={filters.subject} onChange={e => setFilters({ ...filters, subject: e.target.value, birthYear: "", search: "", coachId: "" })}><option value="players">اللاعبون</option><option value="staff">المدربون / الجهاز الفني</option></select></label><DateField label="من تاريخ" value={filters.from} set={v => setFilters({ ...filters, from: v })} /><DateField label="إلى تاريخ" value={filters.to} set={v => setFilters({ ...filters, to: v })} /><SelectFilter label="الرياضة" value={filters.sportId} set={v => setFilters({ ...filters, sportId: v })} options={options.sports} /><SelectFilter label="الفرع" value={filters.branchId} set={v => setFilters({ ...filters, branchId: v })} options={options.branches} /><SelectFilter label="المجموعة" value={filters.groupId} set={v => setFilters({ ...filters, groupId: v })} options={options.groups} /><label>حالة الحضور<select aria-label="حالة الحضور" value={filters.status ?? ""} onChange={e => setFilters({ ...filters, status: e.target.value })}><option value="">كل الحالات</option><option value="Present">حاضر</option><option value="Late">متأخر</option><option value="Absent">غائب</option><option value="Excused">معتذر</option><option value="NotRecorded">لم يُسجل</option></select></label>{filters.subject === "players" && <label>سنة الميلاد<input aria-label="سنة الميلاد" type="number" min="2000" max="2100" value={filters.birthYear ?? ""} onChange={e => setFilters({ ...filters, birthYear: e.target.value })} /></label>}{filters.subject === "staff" ? <SelectFilter label="المدرب" value={filters.coachId} set={v => setFilters({ ...filters, coachId: v })} options={options.coaches ?? []} /> : <label>البحث بالاسم<input aria-label="البحث بالاسم" value={filters.search ?? ""} onChange={e => setFilters({ ...filters, search: e.target.value })} /></label>}<button className="primary-button">تطبيق الفلاتر</button><button type="button" onClick={() => { setFilters({ subject: "players", from: "", to: "" }); setData(null); setError(""); load("allDates=true"); }}>مسح</button><a className="secondary-button export-link" href={`/api/v1/reports/attendance/export?${query}`} download>تصدير CSV</a></form>{error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : data && <section className="list-card"><p className="report-note">يعرض التقرير سجلات الحضور المحفوظة فقط؛ عدم وجود سجل لا يُحوّل إلى غياب.</p><p className="metric">عدد السجلات: {data.totalCount}</p>{data.items.length === 0 ? <EmptyState message="لا توجد سجلات حضور مطابقة." /> : <AdminDataTable label="سجلات الحضور" rows={data.items} rowKey={row=>row.id} columns={[
 {key:"person",header:filters.subject==="staff"?"الموظف":"اللاعب",primary:true,render:row=><span><strong>{row.player?`${row.playerCode} — ${row.player}`:row.staff}</strong>{row.birthYear&&<small>سنة الميلاد: {row.birthYear}</small>}{row.role&&<small>{statusLabels[row.role]??row.role}</small>}</span>},
 {key:"date",header:"التاريخ والوقت",render:row=><span>{row.date}<small dir="ltr">{row.time.slice(0,5)}</small></span>},
 {key:"branch",header:"الفرع",render:row=>row.branch},
 {key:"sport",header:"الرياضة",render:row=>row.sport},
 {key:"group",header:"المجموعة",render:row=>row.group},
 {key:"status",header:"الحالة",render:row=><span><StatusBadge status={row.status} label={statusLabels[row.status]}/>{row.sessionConsumed&&<small>تم خصم حصة</small>}</span>},
 ]}/>}</section>}</>;
}

type ReceiptRow = { id: string; receiptNumber: string; player: string; sport: string; plan: string; amount: number; currency: string; paidAtUtc: string; historicalPaidOn?: string | null };
type ReceiptFeed = { items: ReceiptRow[]; totalCount: number; page: number; pageSize: number; from?: string; to?: string };
const receiptReport = "/dashboard/reports/receipts";

function receiptRangeError(from: string, to: string) {
  if (!from && !to) return "";
  const validDate = (value: string) => /^[1-9]\d{3}-\d{2}-\d{2}$/.test(value) &&
    Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value;
  if (!validDate(from) || !validDate(to)) return "أدخل تاريخ بداية ونهاية صالحين.";
  if (to === "9999-12-31") return "اختر تاريخ نهاية صالحًا.";
  if (from > to) return "تاريخ البداية يجب أن يسبق النهاية أو يساويها.";
  return Date.parse(to) - Date.parse(from) > 366 * 86400000 ? "اختر فترة لا تتجاوز 366 يومًا." : "";
}

export function ReceiptList() {
  const incoming = useSearchParams();
  const router = useRouter();
  const from = incoming.get("from") ?? "", to = incoming.get("to") ?? "";
  const page = Math.min(100000, Math.max(1, Number.parseInt(incoming.get("page") ?? "1", 10) || 1));
  const size = Number(incoming.get("pageSize"));
  const pageSize = [10, 25, 50].includes(size) ? size : 25;
  const [filterError, setFilterError] = useState("");
  const rangeError = receiptRangeError(from, to);

  function receiptQuery(start: string, end: string, next: number, perPage: number) {
    const result = new URLSearchParams({ page: String(next), pageSize: String(perPage) });
    if (start) result.set("from", start);
    if (end) result.set("to", end);
    return result;
  }
  const query = receiptQuery(from, to, page, pageSize);
  const { current, retry } = useReceiptRead<ReceiptFeed>(rangeError ? null : `/api/v1/reports/receipts?${query}`);
  const data = current?.data;
  const detailQuery = new URLSearchParams(query);
  detailQuery.set("report", "1");

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    const start = String(values.get("from") ?? ""), end = String(values.get("to") ?? "");
    const error = receiptRangeError(start, end);
    setFilterError(error);
    if (!error) router.push(`${receiptReport}?${receiptQuery(start, end, 1, pageSize)}`, { scroll: false });
  }
  function paginate(next: number, perPage = pageSize) {
    router.push(`${receiptReport}?${receiptQuery(from, to, next, perPage)}`, { scroll: false });
  }

  return <>
    <PageHeader title="الإيصالات" context="التقارير / إيصالات الدفع" />
    <form className="report-filters receipt-report-filters" aria-label="فترة الإيصالات" key={`${from}:${to}`} onSubmit={submit}>
      <label>من تاريخ<input type="date" name="from" defaultValue={from} onChange={() => setFilterError("")} /></label>
      <label>إلى تاريخ<input type="date" name="to" defaultValue={to} onChange={() => setFilterError("")} /></label>
      <button type="submit" className="primary-button">عرض الإيصالات</button>
      {(from || to) && <Link className="secondary-button" href={receiptReport} onClick={() => setFilterError("")}>الشهر الحالي</Link>}
    </form>
    {(filterError || rangeError) && <ErrorState message={filterError || rangeError} />}
    {!rangeError && (!current ? <LoadingState message="جارٍ تحميل الإيصالات…" />
      : current.error === "unavailable" ? <ErrorState message="الإيصالات غير متاحة في هذا الحساب." />
      : current.error || !Array.isArray(data?.items) ? <><ErrorState message="تعذر تحميل الإيصالات." /><button type="button" className="secondary-button" onClick={retry}>إعادة المحاولة</button></>
      : data.items.length === 0 ? <><EmptyState message={from || to ? "لا توجد إيصالات في الفترة المحددة." : "لا توجد إيصالات في الفترة الحالية."} />{page > 1 && <Link className="secondary-button" href={`${receiptReport}?${receiptQuery(from, to, 1, pageSize)}`}>الصفحة الأولى</Link>}</>
      : <section className="list-card">
        {data.from && data.to && <p className="muted">الإيصالات من {formatDateAr(data.from)} إلى {formatDateAr(data.to)}</p>}
        <AdminDataTable label="قائمة الإيصالات" rows={data.items} rowKey={row => row.id}
          paginate={data.totalCount > pageSize || page > 1}
          serverPagination={{ page, pageSize, total: data.totalCount, onPageChange: paginate, onPageSizeChange: value => paginate(1, value) }}
          columns={[
            { key: "receipt", header: "رقم الإيصال", primary: true, render: row => <strong dir="ltr">{row.receiptNumber}</strong> },
            { key: "player", header: "اللاعب", render: row => row.player },
            { key: "sport", header: "الرياضة والباقة", render: row => <span>{row.sport}<small>{row.plan}</small></span> },
            { key: "amount", header: "المبلغ", render: row => <span className="report-amount"><MoneyValue amount={row.amount} currency={row.currency} /></span> },
            { key: "date", header: "تاريخ الدفع", render: row => row.historicalPaidOn ? <>{formatDateAr(row.historicalPaidOn)}<small>تحصيل سابق</small></> : new Date(row.paidAtUtc).toLocaleDateString("ar-EG-u-nu-latn") },
            { key: "actions", header: "الإجراءات", render: row => <Link className="row-primary-action" href={`/dashboard/subscriptions/receipts/${row.id}?${detailQuery}`}>عرض وطباعة</Link> },
          ]} />
      </section>)}
  </>;
}

function params(filters: Filters) { const p = new URLSearchParams(); Object.entries(filters).forEach(([key, value]) => { if (value) p.set(key, value); }); if (!filters.from && !filters.to) p.set("allDates", "true"); return p.toString(); }
function DateField({ label, value, set }: { label: string; value?: string; set: (v: string) => void }) { return <label>{label}<input aria-label={label} type="date" value={value ?? ""} onChange={e => set(e.target.value)} /></label>; }
function SelectFilter({ label, value, set, options }: { label: string; value?: string; set: (v: string) => void; options: Option[] }) { return <label>{label}<select aria-label={label} value={value ?? ""} onChange={e => set(e.target.value)}><option value="">الكل</option>{options.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>; }
