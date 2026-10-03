"use client";

import Link from "next/link";
import { Activity, CalendarClock, CircleX, Clock3, CreditCard, ReceiptText, UsersRound, WalletCards } from "lucide-react";
import { CSSProperties, FormEvent, useEffect, useMemo, useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, MetricCard, PageHeader, SectionCard } from "../components/dashboard-shell";
import { ProgressTrack, SportsHero, SportsQuickActions } from "../components/sports-ui";

type OwnerSummary = { asOfDate: string; currency: string; collections: { total: number; today: number; month: number }; payments: { confirmed: number; failed: number; pending: number; cancelled: number; expired: number }; subscriptions: { active: number; expiring: number; expired: number }; activePlayers: number; attendance: { sessions: number; present: number; absent: number; notRecorded: number }; bySport: { name: string; amount: number; count: number }[]; byBranch: { name: string; amount: number; count: number }[]; latest: { receiptId: string; receiptNumber: string; player: string; amount: number; currency: string; confirmedAtUtc: string }[] };
type Option = { id: string; arabicName: string; branchId?: string; sportId?: string };
type FinancialRow = { collectionId: string; receiptId: string; receiptNumber: string; player: string; sport: string; branch: string; group: string; plan: string; amount: number; currency: string; paymentMethod: string; provider: string; providerReference: string; confirmedAtUtc: string };
type FinancialResponse = { from: string; to: string; totalAmount: number; totalCount: number; currency: string; items: FinancialRow[] };
type AttendanceRow = { id: string; date: string; time: string; playerCode?: string; player?: string; birthYear?: number; staff?: string; role?: string; sport: string; branch: string; group: string; status: string; sessionConsumed?: boolean };
type AttendanceResponse = { subject: string; from: string; to: string; coverage: string; totalCount: number; items: AttendanceRow[] };
type Filters = Record<string, string>;

const money = (value: number, currency: string) => `${new Intl.NumberFormat("ar-EG", { maximumFractionDigits: 2 }).format(value)} ${currency === "EGP" ? "جنيه" : currency}`;
const statusLabels: Record<string, string> = { Present: "حاضر", Absent: "غائب", NotRecorded: "لم يُسجل", AcademyOwner: "مالك", AcademyAdmin: "إداري", Coach: "مدرب" };

export function OwnerDashboard() {
  const [role, setRole] = useState(""); const [data, setData] = useState<OwnerSummary | null>(null); const [error, setError] = useState("");
  const [identity, setIdentity] = useState({ name: "", academy: "" });
  useEffect(() => { fetch("/api/v1/me").then(r => { if (!r.ok) throw new Error(); return r.json(); }).then((me: { role: string; displayName?: string; academyName?: string }) => { setRole(me.role); setIdentity({ name: me.displayName ?? "", academy: me.academyName ?? "" }); if (me.role === "AcademyOwner") return fetch("/api/v1/reports/owner-summary").then(r => { if (!r.ok) throw new Error(); return r.json(); }).then(setData); }).catch(() => setError("تعذر تحميل لوحة المتابعة.")); }, []);
  if (error) return <><PageHeader title="الرئيسية" context="لوحة الإدارة" /><ErrorState message={error} /></>;
  if (!role || role === "AcademyOwner" && !data) return <><PageHeader title="لوحة المالك" context="الرئيسية" /><LoadingState /></>;
  if (role !== "AcademyOwner") return <>
    <PageHeader title="الرئيسية" context={role === "Coach" ? "مساحة المدرب" : "لوحة الإدارة"} description="وصول سريع إلى مهام التشغيل اليومية للأكاديمية." />
    <SportsHero name={identity.name} academy={identity.academy} />
    <section className="dashboard-card admin-home-card">
      <div className="admin-home-intro"><h2>مرحبًا بك في مساحة الإدارة</h2><p>اختر المهمة التي تريد تنفيذها، أو استخدم القائمة للوصول إلى بقية وحدات الأكاديمية.</p></div>
      <SportsQuickActions coach={role === "Coach"} />
    </section>
  </>;

  const owner = data!;
  return <>
    <PageHeader title="لوحة المالك" context="الرئيسية / نظرة عامة" description={`ملخص أداء الأكاديمية من السجلات المحفوظة حتى ${owner.asOfDate}.`} action={{ label: "التقرير المالي", href: "/dashboard/reports/financial" }} />
    <SportsHero name={identity.name} academy={identity.academy} date={owner.asOfDate}><Link className="hero-action" href="/dashboard/attendance/sessions"><CalendarClock aria-hidden="true" />انتقل إلى أرض التدريب</Link></SportsHero>
    <section className="owner-primary-metrics" aria-label="المؤشرات الأساسية">
      <MetricCard label="تحصيل اليوم" value={money(owner.collections.today, owner.currency)} icon={<WalletCards />} emphasis="primary" />
      <MetricCard label="تحصيل الشهر" value={money(owner.collections.month, owner.currency)} icon={<ReceiptText />} emphasis="primary" />
      <MetricCard label="الاشتراكات الفعالة" value={owner.subscriptions.active} icon={<CreditCard />} emphasis="primary" />
      <MetricCard label="تنتهي قريبًا" value={owner.subscriptions.expiring} icon={<Clock3 />} emphasis="primary" detail={`${owner.subscriptions.expired} اشتراك منتهي`} />
    </section>
    <section className="owner-secondary-metrics" aria-label="المؤشرات المساندة">
      <MetricCard label="إجمالي التحصيلات" value={money(owner.collections.total, owner.currency)} icon={<WalletCards />} />
      <MetricCard label="مدفوعات معلقة" value={owner.payments.pending} icon={<Clock3 />} />
      <MetricCard label="مدفوعات فاشلة" value={owner.payments.failed} icon={<CircleX />} />
      <MetricCard label="اللاعبون النشطون" value={owner.activePlayers} icon={<UsersRound />} />
      <MetricCard label="حضور اليوم" value={`${owner.attendance.present} حاضر`} icon={<Activity />} detail={`${owner.attendance.absent} غائب · ${owner.attendance.sessions} حصة`} />
      <MetricCard label="الاشتراكات المنتهية" value={owner.subscriptions.expired} icon={<Clock3 />} />
    </section>
    <SectionCard title="خطوتك التالية" description="كل ما تحتاجه لإدارة يوم الأكاديمية"><SportsQuickActions /></SectionCard>
    <div className="owner-business-grid">
      <Breakdown title="التحصيل حسب الفرع" rows={owner.byBranch} currency={owner.currency} />
      <SectionCard title="حضور اليوم" description="نسبة الحضور من سجلات اليوم المحفوظة فقط"><AttendanceRing attendance={owner.attendance} /></SectionCard>
      <SectionCard title="الاشتراكات حسب الحالة" description="مؤشرات مستقلة؛ قد تتداخل حالات الفعالية والانتهاء القريب"><div className="subscription-state-bars">{[{ name: "فعالة", count: owner.subscriptions.active, tone: "success" }, { name: "تنتهي قريبًا", count: owner.subscriptions.expiring, tone: "warning" }, { name: "منتهية", count: owner.subscriptions.expired, tone: "danger" }].map(item => <div key={item.name} data-tone={item.tone}><div><span>{item.name}</span><b>{item.count}</b></div><ProgressTrack value={item.count} total={Math.max(owner.subscriptions.active, owner.subscriptions.expiring, owner.subscriptions.expired)} label={`اشتراكات ${item.name}`} /></div>)}</div></SectionCard>
      <Breakdown title="التحصيل حسب الرياضة" rows={owner.bySport} currency={owner.currency} />
    </div>
    <SectionCard title="أحدث التحصيلات" description="آخر العمليات المؤكدة المسجلة في الأكاديمية">
      {owner.latest.length === 0 ? <EmptyState message="لا توجد تحصيلات مؤكدة." /> : <AdminDataTable label="أحدث التحصيلات" rows={owner.latest} rowKey={row => row.receiptId} columns={[
        { key: "receipt", header: "الإيصال واللاعب", primary: true, width: "minmax(15rem, 2fr)", render: row => <><b dir="ltr">{row.receiptNumber}</b><small>{row.player}</small></> },
        { key: "amount", header: "القيمة", width: "minmax(8rem, .8fr)", render: row => <b>{money(row.amount, row.currency)}</b> },
        { key: "date", header: "التاريخ", width: "minmax(10rem, 1fr)", render: row => new Date(row.confirmedAtUtc).toLocaleString("ar-EG") },
        { key: "actions", header: "الإجراء", width: "minmax(7rem, .6fr)", render: row => <Link className="small-link" href={`/dashboard/subscriptions/receipts/${row.receiptId}`}>عرض الإيصال</Link> },
      ]} />}
    </SectionCard>
  </>;
}

function AttendanceRing({ attendance }: { attendance: OwnerSummary["attendance"] }) {
  const total = attendance.present + attendance.absent + attendance.notRecorded;
  if (!total) return <EmptyState title="لا توجد سجلات حضور لليوم" message="ابدأ بتسجيل حضور الحصة؛ ستظهر النتائج هنا من السجلات المحفوظة." />;
  return <div className="attendance-visual"><div className="performance-ring" style={{ "--ring-angle": `${attendance.present / total * 360}deg` } as CSSProperties} aria-label={`${attendance.present} حاضر من ${total} سجل`}><div><b>{attendance.present}</b><span>حاضر من {total}</span></div></div><dl><div><dt>حاضر</dt><dd>{attendance.present}</dd></div><div><dt>غائب</dt><dd>{attendance.absent}</dd></div><div><dt>لم يُسجل</dt><dd>{attendance.notRecorded}</dd></div></dl></div>;
}

function Breakdown({ title, rows, currency }: { title: string; rows: OwnerSummary["bySport"]; currency: string }) {
  const maximum = Math.max(...rows.map(row => row.amount), 1);
  return <SectionCard title={title}>{rows.length === 0 ? <EmptyState /> : <div className="breakdown-list">{rows.map(row => <div className="breakdown-item" key={row.name}><div><span>{row.name}</span><b>{money(row.amount, currency)} · {row.count} عملية</b></div><div className="breakdown-track" aria-hidden="true"><span style={{ "--bar-width": `${Math.max(0, row.amount / maximum * 100)}%` } as CSSProperties} /></div></div>)}</div>}</SectionCard>;
}

export function FinancialReport() {
  const [filters, setFilters] = useState<Filters>({ from: "", to: "", sportId: "", branchId: "", planId: "", provider: "", search: "" }); const [data, setData] = useState<FinancialResponse | null>(null); const [options, setOptions] = useState<{ sports: Option[]; branches: Option[]; groups: Option[] }>({ sports: [], branches: [], groups: [] }); const [plans, setPlans] = useState<Option[]>([]); const [error, setError] = useState("");
  const query = useMemo(() => params(filters), [filters]);
  function load(next = query) { setError(""); setData(null); fetch(`/api/v1/reports/financial${next ? `?${next}` : ""}`).then(async r => { if (!r.ok) throw new Error(); const value = await r.json() as FinancialResponse; setData(value); setFilters(current => ({ from: current.from || value.from, to: current.to || value.to, ...current })); }).catch(() => setError("تعذر تحميل التقرير المالي.")); }
  useEffect(() => { fetch("/api/v1/reports/financial").then(async r => { if (!r.ok) throw new Error(); const value = await r.json() as FinancialResponse; setData(value); setFilters(current => ({ ...current, from: value.from, to: value.to })); }).catch(() => setError("تعذر تحميل التقرير المالي.")); fetch("/api/v1/manage/structure/options").then(r => r.ok ? r.json() : Promise.reject()).then(setOptions).catch(() => undefined); fetch("/api/v1/subscriptions/plans").then(r => r.ok ? r.json() : Promise.reject()).then((rows: { id: string; arabicName: string }[]) => setPlans(rows)).catch(() => undefined); }, []);
  function submit(e: FormEvent) { e.preventDefault(); load(); }
  return <><PageHeader title="التقارير المالية" context="التقارير / التحصيلات المؤكدة" /><form className="report-filters" onSubmit={submit}><DateField label="من تاريخ" value={filters.from} set={v => setFilters({ ...filters, from: v })} /><DateField label="إلى تاريخ" value={filters.to} set={v => setFilters({ ...filters, to: v })} /><SelectFilter label="الرياضة" value={filters.sportId} set={v => setFilters({ ...filters, sportId: v })} options={options.sports} /><SelectFilter label="الفرع" value={filters.branchId} set={v => setFilters({ ...filters, branchId: v })} options={options.branches} /><SelectFilter label="الباقة" value={filters.planId} set={v => setFilters({ ...filters, planId: v })} options={plans} /><label>طريقة/مزود الدفع<input value={filters.provider ?? ""} onChange={e => setFilters({ ...filters, provider: e.target.value })} /></label><label>بحث اللاعب أو الإيصال<input value={filters.search ?? ""} onChange={e => setFilters({ ...filters, search: e.target.value })} /></label><button className="primary-button">تطبيق الفلاتر</button><a className="secondary-button export-link" href={`/api/v1/reports/financial/export${query ? `?${query}` : ""}`} download>تصدير CSV</a></form>{error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : data && <section className="list-card"><div className="report-total"><span>إجمالي التحصيل المؤكد</span><strong>{money(data.totalAmount, data.currency)}</strong><small>{data.totalCount} عملية — لا تشمل المدفوعات الفاشلة أو المعلقة</small></div>{data.items.length === 0 ? <EmptyState message="لا توجد تحصيلات مؤكدة ضمن الفلاتر." /> : <div className="report-table">{data.items.map(row => <article key={row.collectionId}><div><strong>{row.receiptNumber} — {row.player}</strong><small>{row.sport} · {row.branch} · {row.group} · {row.plan}</small><small>{row.paymentMethod === "Online" ? "دفع إلكتروني" : row.paymentMethod} / {row.provider === "InternalTest" || row.provider === "Internal" ? "بوابة الاختبار الداخلية" : row.provider} · {new Date(row.confirmedAtUtc).toLocaleString("ar-EG")}</small></div><b>{money(row.amount, row.currency)}</b><Link className="small-link" href={`/dashboard/subscriptions/receipts/${row.receiptId}`}>الإيصال</Link></article>)}</div>}</section>}</>;
}

export function AttendanceReport() {
  const [filters, setFilters] = useState<Filters>({ subject: "players", month: "", year: "", sportId: "", branchId: "", groupId: "", status: "", search: "", birthYear: "" }); const [data, setData] = useState<AttendanceResponse | null>(null); const [options, setOptions] = useState<{ sports: Option[]; branches: Option[]; groups: Option[] }>({ sports: [], branches: [], groups: [] }); const [error, setError] = useState(""); const query = useMemo(() => params(filters), [filters]);
  function load(next = query) { setData(null); setError(""); fetch(`/api/v1/reports/attendance?${next}`).then(async r => { if (!r.ok) throw new Error(); const value = await r.json() as AttendanceResponse; setData(value); setFilters(current => ({ month: current.month || String(Number(value.from.slice(5, 7))), year: current.year || value.from.slice(0, 4), ...current })); }).catch(() => setError("تعذر تحميل تقرير الحضور.")); }
  useEffect(() => { fetch("/api/v1/reports/attendance?subject=players").then(async r => { if (!r.ok) throw new Error(); const value = await r.json() as AttendanceResponse; setData(value); setFilters(current => ({ ...current, month: String(Number(value.from.slice(5, 7))), year: value.from.slice(0, 4) })); }).catch(() => setError("تعذر تحميل تقرير الحضور.")); fetch("/api/v1/manage/structure/options").then(r => r.ok ? r.json() : Promise.reject()).then(setOptions).catch(() => undefined); }, []);
  function submit(e: FormEvent) { e.preventDefault(); load(); }
  return <><PageHeader title="تقارير الحضور" context="التقارير / الحضور الفعلي" /><form className="report-filters" onSubmit={submit}><label>نوع التقرير<select aria-label="نوع التقرير" value={filters.subject} onChange={e => setFilters({ ...filters, subject: e.target.value, birthYear: "" })}><option value="players">اللاعبون</option><option value="staff">المدربون / الجهاز الفني</option></select></label><label>الشهر<input aria-label="الشهر" type="number" min="1" max="12" value={filters.month} onChange={e => setFilters({ ...filters, month: e.target.value })} /></label><label>السنة<input aria-label="السنة" type="number" min="2000" max="2100" value={filters.year} onChange={e => setFilters({ ...filters, year: e.target.value })} /></label><SelectFilter label="الرياضة" value={filters.sportId} set={v => setFilters({ ...filters, sportId: v })} options={options.sports} /><SelectFilter label="الفرع" value={filters.branchId} set={v => setFilters({ ...filters, branchId: v })} options={options.branches} /><SelectFilter label="المجموعة" value={filters.groupId} set={v => setFilters({ ...filters, groupId: v })} options={options.groups} /><label>حالة الحضور<select aria-label="حالة الحضور" value={filters.status ?? ""} onChange={e => setFilters({ ...filters, status: e.target.value })}><option value="">كل الحالات</option><option value="Present">حاضر</option><option value="Absent">غائب</option><option value="NotRecorded">لم يُسجل</option></select></label>{filters.subject === "players" && <label>سنة الميلاد<input aria-label="سنة الميلاد" type="number" min="2000" max="2100" value={filters.birthYear ?? ""} onChange={e => setFilters({ ...filters, birthYear: e.target.value })} /></label>}<label>بحث<input aria-label="بحث الحضور" value={filters.search ?? ""} onChange={e => setFilters({ ...filters, search: e.target.value })} /></label><button className="primary-button">تطبيق الفلاتر</button><a className="secondary-button export-link" href={`/api/v1/reports/attendance/export?${query}`} download>تصدير CSV</a></form>{error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : data && <section className="list-card"><p className="report-note">يعرض التقرير سجلات الحضور المحفوظة فقط؛ عدم وجود سجل لا يُحوّل إلى غياب.</p><p className="metric">عدد السجلات: {data.totalCount}</p>{data.items.length === 0 ? <EmptyState message="لا توجد سجلات حضور مطابقة." /> : <div className="report-table">{data.items.map(row => <article key={row.id}><div><strong>{row.player ? `${row.playerCode} — ${row.player}` : row.staff}</strong><small>{row.date} · {row.time.slice(0, 5)} · {row.sport} · {row.branch} · {row.group}</small>{row.birthYear && <small>سنة الميلاد: {row.birthYear}{row.sessionConsumed ? " · تم خصم حصة" : ""}</small>}{row.role && <small>{statusLabels[row.role] ?? row.role}</small>}</div><span className="status-pill">{statusLabels[row.status] ?? row.status}</span></article>)}</div>}</section>}</>;
}

export function ReceiptList() { const [data, setData] = useState<{ items: { id: string; receiptNumber: string; player: string; sport: string; plan: string; amount: number; currency: string; paidAtUtc: string }[] } | null>(null); const [error, setError] = useState(""); useEffect(() => { fetch("/api/v1/reports/receipts").then(r => { if (!r.ok) throw new Error(); return r.json(); }).then(setData).catch(() => setError("تعذر تحميل الإيصالات.")); }, []); return <><PageHeader title="الإيصالات" context="التقارير / إيصالات الدفع" />{error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : data?.items.length === 0 ? <EmptyState message="لا توجد إيصالات في الفترة الحالية." /> : <section className="list-card"><div className="data-list">{data?.items.map(row => <article key={row.id}><div><strong>{row.receiptNumber} — {row.player}</strong><small>{row.sport} · {row.plan} · {money(row.amount, row.currency)} · {new Date(row.paidAtUtc).toLocaleDateString("ar-EG")}</small></div><Link className="small-link" href={`/dashboard/subscriptions/receipts/${row.id}`}>عرض وطباعة</Link></article>)}</div></section>}</>; }

function params(filters: Filters) { const p = new URLSearchParams(); Object.entries(filters).forEach(([key, value]) => { if (value) p.set(key, value); }); return p.toString(); }
function DateField({ label, value, set }: { label: string; value?: string; set: (v: string) => void }) { return <label>{label}<input aria-label={label} type="date" value={value ?? ""} onChange={e => set(e.target.value)} /></label>; }
function SelectFilter({ label, value, set, options }: { label: string; value?: string; set: (v: string) => void; options: Option[] }) { return <label>{label}<select aria-label={label} value={value ?? ""} onChange={e => set(e.target.value)}><option value="">الكل</option>{options.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>; }
