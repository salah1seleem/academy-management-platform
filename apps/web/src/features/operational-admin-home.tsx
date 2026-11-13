"use client";
import Image from "next/image";
import Link from "next/link";
import {UserPlus,RefreshCcw,ClipboardCheck,Users,CalendarDays,Trophy} from "lucide-react";
import { useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { SubscriptionHome } from "./subscription-home";
import { AcademyActivityPreview } from "./academy-activities";
import { formatDateAr, formatSessionDateAr, formatTimeAr } from "../lib/formatters";
import { conditions, followUpStates } from "./owner-followups";
type Access={useActionGrants:boolean;actions?:{module:string;read:boolean;add:boolean;edit:boolean}[];workflowActions?:{key:string;allowed:boolean}[]};
export function OperationalAdminHome({logoUrl,primaryColor}:{logoUrl?:string|null;primaryColor?:string}={}){
 const [meta,setMeta]=useState<{permissions:string[];access:Access}>();const [error,setError]=useState(false);const [revision,setRevision]=useState(0);
 const matchAccess=useSource<{canRead:boolean}>("/api/v1/my-match-access");
 useEffect(() => {
  const controller = new AbortController();
  async function read(path: string) {
   const response = await fetch(path, { signal: controller.signal, cache: "no-store" });
   if (!response.ok) throw new Error();
   return response.json();
  }
  Promise.all([read("/api/v1/my-permissions"), read("/api/v1/my-access")]).then(([permissions, access]) => {
   if (!Array.isArray(permissions) || typeof access?.useActionGrants !== "boolean") throw new Error();
   if (!controller.signal.aborted) setMeta({ permissions, access });
  }).catch(() => { if (!controller.signal.aborted) setError(true); });
  return () => controller.abort();
 }, [revision]);
 const heading = <h1 id="admin-home-title">يوم الأكاديمية</h1>;
 if (!meta) return <section className="page-shell" aria-labelledby="admin-home-title">
  {heading}
  {error ? <><ErrorState message="تعذر تحميل يوم الأكاديمية." /><button type="button" className="secondary-button" onClick={() => { setError(false); setRevision(value => value + 1); }}>إعادة المحاولة</button></> : <LoadingState message="جارٍ تحميل يوم الأكاديمية…" />}
 </section>;
 const allowed=(permission:string,module:string,operation:"read"|"add"|"edit"|"write"="read",workflow?:string)=>{if(!meta.permissions.includes(permission))return false;if(!meta.access.useActionGrants)return true;const g=meta.access.actions?.find(g=>g.module===module);return g?.read===true&&(operation==="read"||operation==="write"?operation==="read"||g.add&&g.edit:g[operation])&&(!workflow||meta.access.workflowActions?.some(w=>w.key===workflow&&w.allowed)===true);};
 const attendanceWrite=allowed("attendance.manage","attendance","edit","workflow.attendance.players");
 const catalogRead=allowed("content.catalog.manage","catalog");
 const actions=[
  {icon:UserPlus,label:"اشتراك جديد",href:"/dashboard/players/new",show:allowed("people.manage","players","add","workflow.players.create")&&allowed("payment.read","payments","write","workflow.payments.collect")},
  {icon:RefreshCcw,label:"تجديد اشتراك",href:"/dashboard/subscriptions/renew/new",show:allowed("subscription.renew.manage","subscriptions","write","workflow.subscriptions.renew")},
  {icon:ClipboardCheck,label:"تسجيل الحضور",href:"/dashboard/attendance/sessions",show:attendanceWrite},
  {icon:Users,label:"المجموعات والتسكين",href:"/dashboard/academy/groups",show:allowed("people.manage","players","edit","workflow.players.place")&&allowed("structure.manage","academy")},
  {icon:CalendarDays,label:"الحصص",href:"/dashboard/attendance/sessions",show:!attendanceWrite&&allowed("attendance.read","attendance")},
  {icon:Trophy,label:"المباريات والنتائج",href:"/dashboard/matches?tab=matches",show:matchAccess.data?.canRead===true},
 ];
 const visibleActions = actions.filter(action => action.show);
 return <section className="page-shell" dir="rtl" aria-labelledby="admin-home-title">{heading}
 {visibleActions.length > 0 && <nav className="sports-quick-actions" aria-label="إجراءات سريعة">{visibleActions.map(({icon:Icon,...action})=><Link key={action.label} href={action.href}><span className="quick-action-icon"><Icon aria-hidden="true"/></span><span><b>{action.label}</b></span></Link>)}</nav>}
 {allowed("subscription.read","subscriptions")&&<SubscriptionHome owner={false} logoUrl={logoUrl} primaryColor={primaryColor}/>}
 {allowed("attendance.read","attendance")&&<AdminSessions/>}
 {allowed("payment.read","payments")&&<AdminPendingPayments/>}
 {allowed("followups.read","followups")&&<AdminFollowups/>}
 {catalogRead&&<AdminCatalogStock/>}
 <AcademyActivityPreview/>
 </section>;
}
function useSource<T>(path: string, valid?: (value: T) => boolean) {
 const [data, setData] = useState<T>();
 const [error, setError] = useState(false);
 const [revision, setRevision] = useState(0);
 useEffect(() => {
  const controller = new AbortController();
  fetch(path, { signal: controller.signal, cache: "no-store" }).then(async response => {
   if (!response.ok) throw new Error();
   const value = await response.json() as T;
   if (value == null || valid && !valid(value)) throw new Error();
   if (!controller.signal.aborted) setData(value);
  }).catch(() => { if (!controller.signal.aborted) setError(true); });
  return () => controller.abort();
 }, [path, revision, valid]);
 return { data, error, retry: () => { setData(undefined); setError(false); setRevision(value => value + 1); } };
}
function AdminSessions(){const s=useSource<{id:string;group:string;sessionDate:string;startTime:string;endTime:string;status:string}[]>("/api/v1/attendance/sessions");return <section className="detail-card"><h2>حصص اليوم</h2>{s.error?<><ErrorState message="تعذر تحميل حصص اليوم."/><button onClick={s.retry}>إعادة تحميل الحصص</button></>:!s.data?<LoadingState/>:s.data.filter(x=>x.status!=="Cancelled").length?s.data.filter(x=>x.status!=="Cancelled").map(x=><article className="detail-card" key={x.id}><h3>{x.group}</h3><p>{formatSessionDateAr(x.sessionDate)} · {formatTimeAr(x.startTime)}–{formatTimeAr(x.endTime)}</p><Link href={`/dashboard/attendance/sessions/${x.id}/players`}>عرض الحصة</Link></article>):<p>لا توجد حصص في اليوم.</p>}</section>;}
const hasPendingCount = (value: { pending: number }) => Number.isSafeInteger(value.pending) && value.pending >= 0;
function AdminPendingPayments() {
 const requests = useSource("/api/v1/manage/payment-requests?status=Pending&allDates=true", hasPendingCount);
 return <section className="detail-card">
  <h2>طلبات الدفع</h2>
  {requests.error ? <><ErrorState message="تعذر تحميل طلبات الدفع." /><button type="button" onClick={requests.retry}>إعادة تحميل الطلبات</button></>
   : !requests.data ? <LoadingState message="جارٍ تحميل طلبات الدفع…" />
   : requests.data.pending === 0 ? <p>لا توجد طلبات دفع معلقة.</p>
   : <><p>بانتظار المراجعة: {requests.data.pending}</p><Link href="/dashboard/subscriptions/payments?status=Pending&allDates=true">مراجعة الطلبات</Link></>}
 </section>;
}
type AssignedPreview = { id: string; condition: string; state: string; dueOn?: string | null; source?: { name?: string } | null };
const hasFollowups = (value: { items: AssignedPreview[] }) => Array.isArray(value.items);

function AdminFollowups() {
 const followups = useSource("/api/v1/my-followups?state=Open&allDates=true", hasFollowups);
 return <section className="detail-card" aria-labelledby="admin-followups-title">
  <header className="section-heading">
   <h2 id="admin-followups-title">متابعات مسندة إليّ</h2>
   <Link href="/dashboard/my-followups?allDates=true">فتح متابعاتي</Link>
  </header>
  {followups.error ? <><ErrorState message="تعذر تحميل المتابعات." /><button type="button" onClick={followups.retry}>إعادة تحميل المتابعات</button></>
   : !followups.data ? <LoadingState message="جارٍ تحميل المتابعات…" />
   : followups.data.items.length === 0 ? <p>لا توجد متابعات مفتوحة.</p>
   : <ul className="admin-home-followups">{followups.data.items.slice(0, 3).map(item => {
    const condition = conditions[item.condition] ?? "متابعة مسندة";
    return <li key={item.id}>
     <Link href={`/dashboard/my-followups/${encodeURIComponent(item.id)}?allDates=true`}>{item.source?.name || condition}</Link>
     <small>{[item.source?.name ? condition : null, followUpStates[item.state], item.dueOn ? `الموعد ${formatDateAr(item.dueOn)}` : null].filter(Boolean).join(" · ")}</small>
    </li>;
   })}</ul>}
 </section>;
}


type ProductStock = { id: string; arabicName: string; imageReference?: string; usesVariants: boolean; stockQuantity: number };
type ProductInventory = { stockQuantity: number; variants: { id: string; color?: string; size: string; shirtNumber?: number; quantity: number }[] };

function AdminCatalogStock() {
  const catalog = useSource<ProductStock[]>("/api/v1/content/catalog/?active=true");
  const [expanded, setExpanded] = useState<string>();
  return <section className="detail-card">
    <header className="section-heading">
      <h2>مخزون المنتجات الرياضية</h2>
      <Link href="/dashboard/content/catalog">عرض المنتجات</Link>
    </header>
    {catalog.error ? <>
      <ErrorState message="تعذر تحميل مخزون المنتجات." />
      <button onClick={catalog.retry}>إعادة المحاولة</button>
    </> : !catalog.data ? <LoadingState /> : catalog.data.length === 0 ? <p>لا توجد منتجات فعالة.</p>
      : catalog.data.map(product => <article key={product.id} className="detail-card">
        <div className="section-heading">
          <div className="content-cell">
            {product.imageReference?.startsWith("/") && !product.imageReference.startsWith("//") && <Image
              src={product.imageReference} alt="" width={54} height={54} unoptimized
            />}
            <h3>{product.arabicName}</h3>
          </div>
          <strong>المتبقي {product.stockQuantity}</strong>
        </div>
        {product.usesVariants && <button
          type="button"
          aria-expanded={expanded === product.id}
          onClick={() => setExpanded(value => value === product.id ? undefined : product.id)}
        >{expanded === product.id ? "إخفاء المقاسات" : "المتبقي حسب المقاس"}</button>}
        {expanded === product.id && <AdminProductVariants key={product.id} id={product.id} />}
      </article>)}
  </section>;
}

function AdminProductVariants({ id }: { id: string }) {
  const stock = useSource<ProductInventory>(`/api/v1/content/catalog/${encodeURIComponent(id)}/inventory`);
  if (stock.error) return <>
    <ErrorState message="تعذر تحميل رصيد المقاسات." />
    <button onClick={stock.retry}>إعادة المحاولة</button>
  </>;
  if (!stock.data) return <LoadingState />;
  return <>
    <p>الإجمالي الحالي {stock.data.stockQuantity}</p>
    <dl>
      {stock.data.variants.map(variant => <div key={variant.id} className="section-heading">
        <dt>{variant.color && `${variant.color} · `}مقاس {variant.size}
          {variant.shirtNumber != null && ` · رقم ${variant.shirtNumber}`}</dt>
        <dd>{variant.quantity}</dd>
      </div>)}
    </dl>
  </>;
}
