"use client";

import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { AdminDataTable as DataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { monthToDate } from "../lib/date-range";
import { formatMoneyAr } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";

const root = "/api/v1/finance/operating-expenses";
type Option = { id: string; arabicName: string; branchId?: string };
type Resource = { id: string; name: string; branchId: string };
type Expense = { id: string; branchId: string; scope: "Group" | "Resource"; trainingGroupId?: string; resourceId?: string; category: string; periodFrom: string; periodTo: string; paidOn?: string; amount: number; currency: string; status: "Draft" | "Approved" | "Posted" | "Reversed" };
type Share = { groupId: string | null; serviceKind: string; weight: number; amount: number };
type Quote = { quoteHash: string; shares: Share[]; total: number; currency: string; method: string; warning?: string };
type AllocationRow = { expenseId: string; trainingGroupId: string | null; serviceKind: string; amount: number; currency: string; expenseAmount: number; category: string; periodFrom: string; periodTo: string };
const categories: Record<string, string> = { Facility: "تشغيل مرفق", Electricity: "كهرباء", Water: "مياه", Heating: "تدفئة", Maintenance: "صيانة", Rent: "إيجار", Supplies: "مستلزمات", Administration: "إدارة", Other: "أخرى" };
const statuses: Record<string, string> = { Draft: "مسودة", Approved: "معتمد للمراجعة", Posted: "مرحّل", Reversed: "معكوس" };
const services: Record<string, string> = { Training: "التدريب", DayPass: "زيارة يومية", OpenSwim: "سباحة حرة", FacilityBooking: "حجز مرفق", Event: "فعالية", Unallocated: "غير موزع" };

export function OperatingExpenses({ initialFrom, initialTo, initialBranchId, initialStatus, initialUnpaid = false }: { initialFrom?: string; initialTo?: string; initialBranchId?: string; initialStatus?: string; initialUnpaid?: boolean } = {}) {
  const [options, setOptions] = useState<{ branches: Option[]; groups: Option[] }>();
  const [currency, setCurrency] = useState("");
  const [resources, setResources] = useState<Resource[]>([]), [items, setItems] = useState<Expense[]>([]);
  const [selected, setSelected] = useState<Expense>(), [quote, setQuote] = useState<Quote>();
  const [allocationGroupId, setAllocationGroupId] = useState(""), [allocationData, setAllocationData] = useState<{ count: number; items: AllocationRow[]; total: number; truncated: boolean }>();
  const [range, setRange] = useState(() => ({ from: initialFrom ?? (initialUnpaid ? "" : monthToDate().from), to: initialTo ?? (initialUnpaid ? "" : monthToDate().to) })), [page, setPage] = useState(1), [total, setTotal] = useState(0);
  const [listBranchId, setListBranchId] = useState(initialBranchId ?? ""), [listStatus, setListStatus] = useState(statuses[initialStatus ?? ""] ? initialStatus! : "");
  const [unpaidOnly, setUnpaidOnly] = useState(initialUnpaid);
  const [branchId, setBranchId] = useState(""), [scope, setScope] = useState<"Group" | "Resource">("Group"), [targetId, setTargetId] = useState("");
  const [category, setCategory] = useState("Facility"), [periodFrom, setPeriodFrom] = useState(() => monthToDate().from), [periodTo, setPeriodTo] = useState(() => monthToDate().to);
  const [paidOn, setPaidOn] = useState(""), [amount, setAmount] = useState(""), [note, setNote] = useState("");
  const [busy, setBusy] = useState(false), [error, setError] = useState(""), [notice, setNotice] = useState("");
  const createKey = useRef(crypto.randomUUID()), postKey = useRef(crypto.randomUUID());
  const load = useCallback(async () => {
    const query = new URLSearchParams({ page: String(page), ...(range.from ? { from: range.from } : {}), ...(range.to ? { to: range.to } : {}), ...(listBranchId ? { branchId: listBranchId } : {}), ...(listStatus ? { status: listStatus } : {}), ...(unpaidOnly ? { unpaid: "true" } : {}) });
    const response = await fetch(`${root}/?${query}`, { cache: "no-store" });
    if (!response.ok) throw new Error(response.status === 403 ? "هذه الصفحة متاحة للمالك فقط." : "تعذر تحميل المصروفات.");
    const data = await response.json() as { count: number; items: Expense[] };
    setItems(data.items); setTotal(data.count);
  }, [page, range, listBranchId, listStatus, unpaidOnly]);
  const loadAllocations = useCallback(async () => {
    const query = new URLSearchParams({ ...(allocationGroupId ? { groupId: allocationGroupId } : {}), ...(range.from ? { from: range.from } : {}), ...(range.to ? { to: range.to } : {}) });
    const response = await fetch(`${root}/allocations?${query}`, { cache: "no-store" });
    if (!response.ok) throw new Error("تعذر تحميل أنصبة المجموعات.");
    setAllocationData(await response.json());
  }, [allocationGroupId, range]);
  useEffect(() => {
    let active = true;
    void Promise.all([fetch("/api/v1/manage/structure/options"), fetch("/api/v1/facilities"), fetch(`${root}/options`)])
      .then(async ([o, f, meta]) => { if (!o.ok || !meta.ok) throw new Error("تعذر تحميل الفروع والمجموعات والمرافق."); return [await o.json(), f.ok ? await f.json() : { resources: [] }, await meta.json()] as const; })
      .then(([o, f, meta]) => { if (active) { setOptions(o); setResources(f.resources ?? []); setCurrency(meta.currency); } })
      .catch(e => { if (active) setError(e instanceof Error ? e.message : "تعذر التحميل."); });
    return () => { active = false; };
  }, []);
  useEffect(() => { void Promise.resolve().then(load).catch(e => setError(e instanceof Error ? e.message : "تعذر التحميل.")); }, [load]);
  useEffect(() => { void Promise.resolve().then(loadAllocations).catch(e => setError(e instanceof Error ? e.message : "تعذر التحميل.")); }, [loadAllocations]);
  const targets = scope === "Group" ? options?.groups.filter(g => g.branchId === branchId) ?? [] : resources.filter(r => r.branchId === branchId);
  async function act(work: () => Promise<void>) {
    if (busy) return; setBusy(true); setError(""); setNotice("");
    try { await work(); await Promise.all([load(), loadAllocations()]); } catch (e) { setError(e instanceof Error ? e.message : "تعذر إتمام العملية."); } finally { setBusy(false); }
  }
  function choose(row: Expense) { setSelected(row); setQuote(undefined); postKey.current = crypto.randomUUID(); setError(""); setNotice(""); }
  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await act(async () => {
      if (!targetId) throw new Error("اختر المجموعة أو المرفق.");
      const response = await csrfRequest(`${root}/`, "POST", { branchId, scope, targetId, category, periodFrom, periodTo, paidOn: paidOn || null, amount, currency, note: note || null }, { "Idempotency-Key": createKey.current }, { announce: false });
      const saved = await response.json() as { id: string };
      const detail = await fetch(`${root}/${saved.id}`).then(r => r.json()) as { expense: Expense };
      choose(detail.expense); createKey.current = crypto.randomUUID(); setNotice("حُفظ المصروف مسودة دون أثر على التحصيل. اعتمده وراجع التوزيع قبل الترحيل.");
    });
  }
  async function approve() {
    if (!selected) return; await act(async () => {
      await csrfRequest(`${root}/${selected.id}/approve`, "POST", {}, {}, { announce: false });
      setSelected({ ...selected, status: "Approved" }); setQuote(undefined); setNotice("تم اعتماد المسودة. عاين الأنصبة قبل ترحيل التكلفة.");
    });
  }
  async function preview() {
    if (!selected) return; await act(async () => {
      const response = await csrfRequest(`${root}/${selected.id}/quote`, "POST", {}, {}, { announce: false });
      setQuote(await response.json() as Quote); postKey.current = crypto.randomUUID();
    });
  }
  async function post() {
    if (!selected || !quote) return; await act(async () => {
      await csrfRequest(`${root}/${selected.id}/post`, "POST", { quoteHash: quote.quoteHash }, { "Idempotency-Key": postKey.current }, { announce: false });
      setSelected({ ...selected, status: "Posted" }); setNotice("تم ترحيل المصروف وأنصبته المحفوظة مرة واحدة؛ لم يُنشأ قبض أو إيصال.");
    });
  }
  if (!options && !error) return <LoadingState />;
  return <div className="operating-expenses-page"><PageHeader title="المصروفات التشغيلية" context="المالية / توزيع تكلفة" />
    <section className="detail-card"><h2>مصدر تكلفة واحد</h2><p>هذه الدفعة للمصروفات التشغيلية اليدوية فقط، وليست رواتب أو تحصيلًا. في المرفق المشترك تُقسم التكلفة أولًا حسب دقائق حجز كل خدمة، ثم بالتساوي بين مجموعات التدريب؛ وتبقى تكلفة الخدمات الأخرى ظاهرة خارج المجموعات.</p></section>
    {options && <section className="detail-card"><h2>تسجيل مصروف</h2><form className="form-shell" onSubmit={e => void create(e)}><div className="form-grid">
      <label>الفرع<select required value={branchId} onChange={e => { setBranchId(e.target.value); setTargetId(""); }}><option value="">اختر الفرع</option>{options.branches.map(b => <option key={b.id} value={b.id}>{b.arabicName}</option>)}</select></label>
      <label>نطاق التكلفة<select value={scope} onChange={e => { setScope(e.target.value as "Group" | "Resource"); setTargetId(""); }}><option value="Group">مجموعة مباشرة</option>{resources.length > 0 && <option value="Resource">مرفق تدريب مشترك</option>}</select></label>
      <label>{scope === "Group" ? "المجموعة" : "المرفق"}<select required value={targetId} onChange={e => setTargetId(e.target.value)}><option value="">اختر {scope === "Group" ? "المجموعة" : "المرفق"}</option>{targets.map(t => <option key={t.id} value={t.id}>{"arabicName" in t ? t.arabicName : t.name}</option>)}</select></label>
      <label>الفئة<select value={category} onChange={e => setCategory(e.target.value)}>{Object.entries(categories).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
      <label>من فترة الاستحقاق<input required type="date" value={periodFrom} onChange={e => setPeriodFrom(e.target.value)} /></label><label>إلى فترة الاستحقاق<input required type="date" min={periodFrom} value={periodTo} onChange={e => setPeriodTo(e.target.value)} /></label>
      <label>تاريخ السداد · اختياري<input type="date" value={paidOn} onChange={e => setPaidOn(e.target.value)} /></label><label>المبلغ · {currency}<input required type="number" min="0.01" max="999999999" step="0.01" value={amount} onChange={e => setAmount(e.target.value)} /></label>
      <label>ملاحظة · اختياري<input maxLength={400} value={note} onChange={e => setNote(e.target.value)} /></label>
    </div><button className="primary-button" disabled={busy || !branchId || !targetId}>حفظ مسودة المصروف</button></form></section>}
    <section className="filter-bar" aria-label="فلاتر المصروفات"><label>من تاريخ<input type="date" value={range.from} onChange={e => { setRange({ ...range, from: e.target.value }); setPage(1); }} /></label><label>إلى تاريخ<input type="date" value={range.to} onChange={e => { setRange({ ...range, to: e.target.value }); setPage(1); }} /></label><label>الفرع<select value={listBranchId} onChange={e => { setListBranchId(e.target.value); setPage(1); }}><option value="">كل الفروع</option>{options?.branches.map(b => <option key={b.id} value={b.id}>{b.arabicName}</option>)}</select></label><label>الحالة<select value={listStatus} onChange={e => { setListStatus(e.target.value); setPage(1); }}><option value="">كل الحالات</option>{Object.entries(statuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label><label>السداد<select value={unpaidOnly ? "unpaid" : ""} onChange={e => { setUnpaidOnly(e.target.value === "unpaid"); setPage(1); }}><option value="">الكل</option><option value="unpaid">غير مسدد مسجلًا</option></select></label><button className="secondary-button" onClick={() => { setRange({ from: "", to: "" }); setListBranchId(""); setListStatus(""); setUnpaidOnly(false); setPage(1); }}>مسح الفلاتر</button></section>
    {error && <ErrorState message={error} />}{notice && <p role="status" className="success-note">{notice}</p>}
    {!items.length ? <EmptyState title="لا توجد مصروفات ضمن الفترة" /> : <DataTable label="المصروفات التشغيلية" rows={items} rowKey={r => r.id} columns={[
      { key: "category", header: "المصروف", primary: true, render: r => <><strong>{categories[r.category] ?? r.category}</strong><small>{r.periodFrom} — {r.periodTo}</small></> },
      { key: "amount", header: "المبلغ", render: r => formatMoneyAr(r.amount, r.currency) },
      { key: "status", header: "الحالة", render: r => statuses[r.status] ?? r.status },
      { key: "action", header: "المراجعة", render: r => <button className="secondary-button" onClick={() => choose(r)}>عرض وتوزيع</button> },
    ]} />}
    <nav className="form-actions" aria-label="صفحات المصروفات"><button className="secondary-button" disabled={page === 1} onClick={() => setPage(page - 1)}>السابق</button><span>{page} / {Math.max(1, Math.ceil(total / 50))}</span><button className="secondary-button" disabled={page * 50 >= total} onClick={() => setPage(page + 1)}>التالي</button></nav>
    {selected && <section className="detail-card" aria-label="مراجعة المصروف"><h2>{categories[selected.category] ?? selected.category} · {formatMoneyAr(selected.amount, selected.currency)}</h2><p>فترة الاستحقاق: {selected.periodFrom} — {selected.periodTo} · الحالة: {statuses[selected.status]}</p>
      <div className="form-actions">{selected.status === "Draft" && <button className="primary-button" disabled={busy} onClick={() => void approve()}>اعتماد المصروف</button>}{selected.status === "Approved" && <button className="secondary-button" disabled={busy} onClick={() => void preview()}>معاينة توزيع التكلفة</button>}</div>
      {quote && selected.status === "Approved" && <><h3>أنصبة محفوظة عند الترحيل</h3><p>قاعدة التوزيع: {quote.method}</p>{quote.warning && <p role="alert">{quote.warning}</p>}<ul>{quote.shares.map(s => <li key={`${s.serviceKind}:${s.groupId ?? "unallocated"}`}>{services[s.serviceKind] ?? s.serviceKind} · {s.groupId ? options?.groups.find(g => g.id === s.groupId)?.arabicName ?? "مجموعة تشغيلية" : "خارج المجموعات"} — {formatMoneyAr(s.amount, quote.currency)}</li>)}</ul><p>المجموع: {formatMoneyAr(quote.total, quote.currency)}</p><button className="primary-button" disabled={busy} onClick={() => void post()}>ترحيل التكلفة مرة واحدة</button></>}
    </section>}
    <section className="detail-card" aria-label="أنصبة التكاليف المحفوظة"><h2>أنصبة المجموعات المرحّلة</h2><p>تصفية مجموعة واحدة تعرض نصيبها المحفوظ ولا تعيد تقسيم المصروف الكامل عليها.</p>
      <label>المجموعة<select value={allocationGroupId} onChange={e => setAllocationGroupId(e.target.value)}><option value="">كل المجموعات</option>{options?.groups.map(g => <option key={g.id} value={g.id}>{g.arabicName}</option>)}</select></label>
      {!allocationData ? <LoadingState /> : <><p role="status">{allocationData.count} نصيب · الإجمالي {formatMoneyAr(allocationData.total, currency)}</p>{allocationData.truncated && <p role="alert">تظهر أول 500 حركة فقط؛ الإجمالي يشمل جميع الحركات ضمن الفلتر.</p>}
        {!allocationData.items.length ? <EmptyState title="لا توجد أنصبة مرحّلة ضمن الفلتر" /> : <DataTable label="أنصبة تكلفة المجموعات" rows={allocationData.items} rowKey={r => `${r.expenseId}:${r.serviceKind}:${r.trainingGroupId ?? "unallocated"}`} columns={[
          { key: "group", header: "الخدمة / المجموعة", primary: true, render: r => <>{services[r.serviceKind] ?? r.serviceKind} · {r.trainingGroupId ? options?.groups.find(g => g.id === r.trainingGroupId)?.arabicName ?? "مجموعة محفوظة" : "خارج المجموعات"}</> },
          { key: "period", header: "فترة الاستحقاق", render: r => `${r.periodFrom} — ${r.periodTo}` },
          { key: "share", header: "النصيب المحفوظ", render: r => formatMoneyAr(r.amount, r.currency) },
          { key: "original", header: "أصل المصروف", render: r => formatMoneyAr(r.expenseAmount, r.currency) },
        ]} />}</>}
    </section>
  </div>;
}
