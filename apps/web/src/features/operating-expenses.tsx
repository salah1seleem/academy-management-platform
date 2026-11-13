"use client";

import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { AdminDataTable as DataTable, ConfirmationDialog, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

const root = "/api/v1/finance/operating-expenses";
type Option = { id: string; arabicName: string; branchId?: string };
type Resource = { id: string; name: string; branchId: string };
type ExpenseScope = "Group" | "Resource" | "Branch" | "Sport";
type Expense = { id: string; branchId: string; scope: ExpenseScope; trainingGroupId?: string; resourceId?: string; sportId?: string; category: string; periodFrom: string; periodTo: string; paidOn?: string; amount: number; currency: string; note?: string; status: "Draft" | "Approved" | "Posted" | "Reversed" };
type Share = { groupId: string | null; serviceKind: string; weight: number; amount: number };
type Quote = { quoteHash: string; shares: Share[]; total: number; currency: string; method: string; warning?: string };
type ExpenseDetail = { expense: Expense; allocations: (Omit<Share, "groupId"> & { trainingGroupId: string | null })[]; allocationTotal: number };
type Verification = "approve" | "post" | "post-conflict";
type AllocationRow = { expenseId: string; trainingGroupId: string | null; serviceKind: string; amount: number; currency: string; expenseAmount: number; category: string; periodFrom: string; periodTo: string };
const categories: Record<string, string> = { Facility: "تشغيل مرفق", Electricity: "كهرباء", Water: "مياه", Heating: "تدفئة", Maintenance: "صيانة", Rent: "إيجار", Supplies: "مستلزمات", Administration: "إدارة", CollectionFees: "رسوم التحصيل الإلكتروني", Other: "أخرى" };
const statuses: Record<string, string> = { Draft: "مسودة", Approved: "معتمد للمراجعة", Posted: "مرحّل", Reversed: "معكوس" };
const services: Record<string, string> = { Training: "التدريب", DayPass: "زيارة يومية", OpenSwim: "سباحة حرة", FacilityBooking: "حجز مرفق", Event: "فعالية", Unallocated: "غير موزع" };

function messageOf(error: unknown) {
  return error instanceof DashboardRequestError || error instanceof TypeError && /\p{Script=Arabic}/u.test(error.message)
    ? error.message : "تعذر إتمام الطلب. أعد المحاولة.";
}

function uncertainWrite(error: unknown) {
  return error instanceof TypeError || error instanceof DashboardRequestError && error.status >= 500;
}

function shareLabel(service: string, groupName?: string) {
  if (groupName) return groupName;
  if (service === "Training" || service === "Unallocated") return "غير موزع على المجموعات";
  return services[service] ?? "خدمة تشغيلية";
}

function ReadError({ message, retry }: { message: string; retry?: () => void }) {
  return <div><ErrorState message={message} />{retry && <button type="button" className="secondary-button" onClick={retry}>إعادة التحميل</button>}</div>;
}

async function readExpense(id: string, signal?: AbortSignal): Promise<ExpenseDetail> {
  const response = await fetch(`${root}/${id}`, { cache: "no-store", signal });
  if (!response.ok) throw new DashboardRequestError(
    response.status === 403 || response.status === 404 ? "هذا المصروف غير متاح لهذا الحساب." : "تعذر تحميل تفاصيل المصروف.",
    response.status,
  );
  const detail = await response.json() as ExpenseDetail;
  if (detail.expense?.id !== id || !statuses[detail.expense.status]) {
    throw new DashboardRequestError("تعذر تحميل تفاصيل المصروف.", 0);
  }
  return detail;
}

type OperatingExpenseFilters = {
  initialFrom?: string; initialTo?: string; initialBranchId?: string; initialSportId?: string;
  initialStatus?: string; initialCategory?: string; initialPaid?: boolean; initialUnpaid?: boolean;
  initialPaymentDates?: boolean; initialAllDates?: boolean; initialExpenseId?: string;
};
export function OperatingExpenses({
  initialFrom, initialTo, initialBranchId, initialSportId, initialStatus, initialCategory,
  initialPaid = false, initialUnpaid = false, initialPaymentDates = false, initialAllDates = false, initialExpenseId,
}: OperatingExpenseFilters = {}) {
  const defaults = useDateFilterDefaults();
  const [creating,setCreating]=useState(false),[pageSize,setPageSize]=useState(25);
  const [listCategory,setListCategory]=useState(initialCategory && categories[initialCategory] ? initialCategory : "");
  const [paidOnly,setPaidOnly]=useState(initialPaid && !initialUnpaid);
  const [paymentDates,setPaymentDates]=useState(initialPaymentDates && !initialUnpaid);
  const [listSportId,setListSportId]=useState(initialSportId ?? "");
  const [options, setOptions] = useState<{ branches: Option[]; groups: Option[]; sports: Option[] }>();
  const [currency, setCurrency] = useState(""), [canApprove,setCanApprove]=useState(false),[canRecord,setCanRecord]=useState(false);
  const [resources, setResources] = useState<Resource[]>([]), [items, setItems] = useState<Expense[]>([]);
  const [detailId, setDetailId] = useState(initialExpenseId), [detail, setDetail] = useState<ExpenseDetail>();
  const [detailIssue, setDetailIssue] = useState(""), [detailRead, setDetailRead] = useState(0);
  const [quote, setQuote] = useState<Quote>(), [verification, setVerification] = useState<Verification>();
  const [confirmPost, setConfirmPost] = useState(false), [draftLocked, setDraftLocked] = useState(false);
  const [listIssue, setListIssue] = useState(""), [allocationIssue, setAllocationIssue] = useState("");
  const [listLoading, setListLoading] = useState(true), [revision, setRevision] = useState(0);
  const reloadLists = () => setRevision(value => value + 1);
  const selected = detail?.expense;
  const [allocationGroupId, setAllocationGroupId] = useState(""), [allocationData, setAllocationData] = useState<{ count: number; items: AllocationRow[]; total: number; truncated: boolean }>();
  const [range, setRange] = useState(() => {
    const openRange = initialUnpaid || initialAllDates || initialPaymentDates || !!initialFrom || !!initialTo;
    return { from: initialFrom ?? (openRange ? "" : defaults.from), to: initialTo ?? (openRange ? "" : defaults.to) };
  }), [page, setPage] = useState(1), [total, setTotal] = useState(0);
  const [listBranchId, setListBranchId] = useState(initialBranchId ?? ""), [listStatus, setListStatus] = useState(statuses[initialStatus ?? ""] ? initialStatus! : "");
  const [unpaidOnly, setUnpaidOnly] = useState(initialUnpaid);
  const [branchId, setBranchId] = useState(""), [scope, setScope] = useState<ExpenseScope>("Group"), [targetId, setTargetId] = useState("");
  const [category, setCategory] = useState("Facility"), [periodFrom, setPeriodFrom] = useState(""), [periodTo, setPeriodTo] = useState("");
  const [academyToday, setAcademyToday] = useState("");
  const [paidOn, setPaidOn] = useState(""), [amount, setAmount] = useState(""), [note, setNote] = useState("");
  const [busy, setBusy] = useState(false), [error, setError] = useState(""), [notice, setNotice] = useState("");
  const createKey = useRef(crypto.randomUUID()), postKey = useRef(crypto.randomUUID());
  const locked = useRef(false), newExpenseButton = useRef<HTMLButtonElement>(null);
  const returnFocus = useRef(false);
  useEffect(() => {
    if (!detailId) return;
    const controller = new AbortController();
    void readExpense(detailId, controller.signal).then(value => {
      if (!controller.signal.aborted) setDetail(value);
    }).catch(error => {
      if (!controller.signal.aborted) setDetailIssue(error instanceof DashboardRequestError ? error.message : "تعذر تحميل تفاصيل المصروف.");
    });
    return () => controller.abort();
  }, [detailId, detailRead]);
  useEffect(() => {
    if (!creating && !detailId && returnFocus.current) {
      returnFocus.current = false;
      newExpenseButton.current?.focus();
    }
  }, [creating, detailId]);
  const load = useCallback(async (signal: AbortSignal) => {
    const query = new URLSearchParams({ page: String(page),pageSize:String(pageSize),...(listCategory?{category:listCategory}:{}),...(paidOnly?{paid:"true"}:{}), ...(range.from ? { from: range.from } : {}), ...(range.to ? { to: range.to } : {}), ...(listBranchId ? { branchId: listBranchId } : {}), ...(listStatus ? { status: listStatus } : {}), ...(unpaidOnly ? { unpaid: "true" } : {}),
      ...(paymentDates ? { dateBasis: "payment" } : {}), ...(listSportId ? { sportId: listSportId } : {}) });
    const response = await fetch(`${root}/?${query}`, { cache: "no-store", signal });
    if (!response.ok) throw new DashboardRequestError(response.status === 403 ? "المصروفات غير متاحة لهذا الحساب." : "تعذر تحميل المصروفات.", response.status);
    const data = await response.json() as { count: number; items: Expense[] };
    if (!signal.aborted) { setItems(data.items); setTotal(data.count); }
  }, [page, pageSize,listCategory,paidOnly,range, listBranchId, listStatus, unpaidOnly, paymentDates, listSportId]);
  const loadAllocations = useCallback(async (signal: AbortSignal) => {
    const query = new URLSearchParams({ ...(allocationGroupId ? { groupId: allocationGroupId } : {}), ...(range.from ? { from: range.from } : {}), ...(range.to ? { to: range.to } : {}) });
    const response = await fetch(`${root}/allocations?${query}`, { cache: "no-store", signal });
    if (!response.ok) throw new Error("تعذر تحميل أنصبة المجموعات.");
    const data = await response.json();
    if (!signal.aborted) setAllocationData(data);
  }, [allocationGroupId, range]);
  useEffect(() => {
    let active = true;
    void Promise.all([fetch("/api/v1/manage/structure/options"), fetch("/api/v1/facilities"), fetch(`${root}/options`)])
      .then(async ([o, f, meta]) => { if (!o.ok || !meta.ok) throw new Error("تعذر تحميل الفروع والمجموعات والمرافق."); return [await o.json(), f.ok ? await f.json() : { resources: [] }, await meta.json()] as const; })
      .then(([o, f, meta]) => { if (active) { setOptions(o); setResources(f.resources ?? []); setCurrency(meta.currency);setCanApprove(meta.canApprove===true);setCanRecord(meta.canRecord===true);setAcademyToday(typeof meta.today === "string" && /^\d{4}-\d{2}-\d{2}$/.test(meta.today) ? meta.today : ""); } })
      .catch(e => { if (active) setError(e instanceof Error ? e.message : "تعذر التحميل."); });
    return () => { active = false; };
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    void Promise.resolve().then(async () => {
      if (controller.signal.aborted) return;
      setListLoading(true); setListIssue("");
      try { await load(controller.signal); }
      catch (error) { if (!controller.signal.aborted) setListIssue(error instanceof DashboardRequestError ? error.message : "تعذر تحميل المصروفات."); }
      finally { if (!controller.signal.aborted) setListLoading(false); }
    });
    return () => controller.abort();
  }, [load, revision]);
  useEffect(() => {
    const controller = new AbortController();
    void Promise.resolve().then(async () => {
      if (controller.signal.aborted) return;
      setAllocationData(undefined); setAllocationIssue("");
      try { await loadAllocations(controller.signal); }
      catch { if (!controller.signal.aborted) setAllocationIssue("تعذر تحميل أنصبة المجموعات."); }
    });
    return () => controller.abort();
  }, [loadAllocations, revision]);
  const targets = scope === "Group" ? options?.groups.filter(g => g.branchId === branchId) ?? [] : scope === "Resource" ? resources.filter(r => r.branchId === branchId) : scope === "Sport" ? options?.sports ?? [] : [];
  async function act(work: () => Promise<void>) {
    if (locked.current) return;
    locked.current = true;
    setBusy(true); setError(""); setNotice("");
    try { await work(); }
    catch (error) { setError(messageOf(error)); }
    finally { locked.current = false; setBusy(false); }
  }
  function openExpense(id?: string) {
    setDetailId(id); setDetail(undefined); setDetailIssue(""); setQuote(undefined);
    setVerification(undefined); setError(""); setNotice("");
  }
  function closeExpense() {
    returnFocus.current = true;
    setCreating(false);
    openExpense();
  }
  function startDraft() {
    setBranchId("");
    setScope("Group");
    setTargetId("");
    setCategory("Facility");
    setPeriodFrom(academyToday ? `${academyToday.slice(0, 8)}01` : "");
    setPeriodTo(academyToday);
    setPaidOn("");
    setAmount("");
    setNote("");
    setError(""); setNotice("");
    createKey.current = crypto.randomUUID();
    setCreating(true);
  }
  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const issue = !branchId ? "اختر الفرع."
      : scope !== "Branch" && !targetId ? "اختر نطاق التكلفة والهدف."
      : !periodFrom || !periodTo || periodFrom > periodTo ? "حدد فترة استحقاق صحيحة."
      : !/^(?:[0-9]+(?:\.[0-9]{1,2})?|\.[0-9]{1,2})$/.test(amount) || Number(amount) <= 0 || Number(amount) > 999999999
        ? "أدخل مبلغًا موجبًا بخانتين عشريتين على الأكثر، حتى 999,999,999." : "";
    if (issue) { setError(issue); return; }
    await act(async () => {
      try {
        const response = await csrfRequest(`${root}/`, "POST", { branchId, scope, targetId: scope === "Branch" ? branchId : targetId, category, periodFrom, periodTo, paidOn: paidOn || null, amount, currency, note: note || null }, { "Idempotency-Key": createKey.current }, { announce: false });
        const saved = await response.json().catch(() => null) as { id?: string } | null;
        if (typeof saved?.id !== "string" || !saved.id) throw new TypeError();
        openExpense(saved.id);
        setCreating(false); setDraftLocked(false); reloadLists();
        setNotice("حُفظ المصروف مسودة. اعتمده وراجع التوزيع قبل الترحيل.");
      } catch (error) {
        if (!uncertainWrite(error)) throw error;
        setDraftLocked(true);
        setError("تعذر تأكيد حفظ المسودة. أعد التحقق بالبيانات نفسها.");
      }
    });
  }
  async function verify(id: string, action: Verification) {
    try {
      const current = await readExpense(id);
      setDetail(current); setDetailIssue(""); setVerification(undefined);
      reloadLists();
      if (current.expense.status === "Posted") {
        setQuote(undefined);
        setNotice("رُحّل المصروف وحُفظ التوزيع.");
      } else if (action === "approve" && current.expense.status === "Approved") {
        setNotice("تم اعتماد المسودة. راجع التوزيع قبل الترحيل.");
      } else if (action === "post-conflict") {
        setError("تغيّرت بيانات التوزيع. افتح معاينة جديدة قبل الترحيل.");
      } else if (action === "post" && current.expense.status === "Approved") {
        setNotice("المصروف معتمد. يمكنك إعادة طلب الترحيل بالتوزيع نفسه.");
      }
    } catch (error) {
      setVerification(action);
      if (error instanceof DashboardRequestError && [401, 403, 404].includes(error.status)) {
        setDetail(undefined); setQuote(undefined); setDetailIssue(error.message);
      } else {
        setError("تعذر قراءة حالة المصروف. أعد التحقق قبل اتخاذ إجراء آخر.");
      }
    }
  }
  async function approve() {
    if (!selected || verification) return;
    await act(async () => {
      try { await csrfRequest(`${root}/${selected.id}/approve`, "POST", {}, {}, { announce: false }); }
      catch (error) {
        if (uncertainWrite(error)) {
          setVerification("approve");
          setError("تعذر تأكيد اعتماد المسودة. تحقّق من حالتها.");
          return;
        }
        if (error instanceof DashboardRequestError && [401, 403, 404, 409].includes(error.status)) {
          if (error.status === 403) setCanApprove(false);
          await verify(selected.id, "approve");
          throw error;
        }
        throw error;
      }
      await verify(selected.id, "approve");
    });
  }
  async function preview() {
    if (!selected || verification) return;
    await act(async () => {
      const response = await csrfRequest(`${root}/${selected.id}/quote`, "POST", {}, {}, { announce: false }).catch(async error => {
        if (error instanceof DashboardRequestError && [401, 403, 404, 409].includes(error.status)) {
          if (error.status === 403) setCanApprove(false);
          await verify(selected.id, "approve");
        }
        throw error;
      });
      const value = await response.json() as Quote;
      if (typeof value.quoteHash !== "string" || value.quoteHash.length !== 64 || !Array.isArray(value.shares)) throw new Error();
      setQuote(value); postKey.current = crypto.randomUUID();
    });
  }
  async function post() {
    if (!selected || !quote || verification) return;
    setConfirmPost(false);
    await act(async () => {
      try { await csrfRequest(`${root}/${selected.id}/post`, "POST", { quoteHash: quote.quoteHash }, { "Idempotency-Key": postKey.current }, { announce: false }); }
      catch (error) {
        if (uncertainWrite(error)) {
          setVerification("post");
          setError("تعذر تأكيد الترحيل. تحقّق من حالة المصروف قبل إعادة الإرسال.");
          return;
        }
        if (error instanceof DashboardRequestError && [401, 403, 404, 409].includes(error.status)) {
          setQuote(undefined);
          if (error.status === 403) setCanApprove(false);
          await verify(selected.id, "post-conflict");
          if (error.status !== 409) throw error;
          return;
        }
        throw error;
      }
      await verify(selected.id, "post");
    });
  }
  const branchName = options?.branches.find(b => b.id === selected?.branchId)?.arabicName ?? "الفرع المسجّل";
  const costTarget = selected?.scope === "Group" ? options?.groups.find(g => g.id === selected.trainingGroupId)?.arabicName ?? "مجموعة مباشرة"
    : selected?.scope === "Resource" ? resources.find(r => r.id === selected.resourceId)?.name ?? "مرفق تدريب مشترك"
    : selected?.scope === "Sport" ? options?.sports.find(s => s.id === selected.sportId)?.arabicName ?? "رياضة داخل الفرع" : "الفرع كله";
  if (!options && !error) return <LoadingState />;
  return <div className="operating-expenses-page"><PageHeader title="المصروفات التشغيلية" context="المالية" />
    {(creating || detailId) && <div className="form-actions"><button className="secondary-button" type="button" disabled={busy || draftLocked || !!verification} onClick={closeExpense}>العودة إلى المصروفات</button></div>}
    {error && <ErrorState message={error} />}{notice && <p role="status" className="success-note">{notice}</p>}
    {options && creating && <section className="detail-card"><h2>تسجيل مصروف</h2><form className="form-shell" noValidate onSubmit={e => void create(e)}><fieldset className="form-grid" disabled={busy || draftLocked}>
      <label>الفرع<select aria-label="الفرع" required value={branchId} onChange={e => { setBranchId(e.target.value); setTargetId(""); }}><option value="">اختر الفرع</option>{options.branches.map(b => <option key={b.id} value={b.id}>{b.arabicName}</option>)}</select></label>
      <label>نطاق التكلفة<select aria-label="نطاق التكلفة" value={scope} onChange={e => { setScope(e.target.value as ExpenseScope); setTargetId(""); }}><option value="Group">مجموعة مباشرة</option>{resources.length > 0 && <option value="Resource">مرفق تدريب مشترك</option>}<option value="Branch">الفرع كله</option><option value="Sport">رياضة داخل الفرع</option></select></label>
      {scope !== "Branch" && <label>{scope === "Group" ? "المجموعة" : scope === "Resource" ? "المرفق" : "الرياضة"}<select aria-label={scope === "Group" ? "المجموعة" : scope === "Resource" ? "المرفق" : "الرياضة"} required value={targetId} onChange={e => setTargetId(e.target.value)}><option value="">اختر {scope === "Group" ? "المجموعة" : scope === "Resource" ? "المرفق" : "الرياضة"}</option>{targets.map(t => <option key={t.id} value={t.id}>{"arabicName" in t ? t.arabicName : t.name}</option>)}</select></label>}
      <label>الفئة<select aria-label="الفئة" value={category} onChange={e => setCategory(e.target.value)}>{Object.entries(categories).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
      <label>من فترة الاستحقاق<input required type="date" value={periodFrom} onChange={e => setPeriodFrom(e.target.value)} /></label><label>إلى فترة الاستحقاق<input required type="date" min={periodFrom} value={periodTo} onChange={e => setPeriodTo(e.target.value)} /></label>
      <label>تاريخ السداد · اختياري<input type="date" value={paidOn} onChange={e => setPaidOn(e.target.value)} /></label><label>المبلغ · {currency}<input required type="number" min="0.01" max="999999999" step="0.01" value={amount} onChange={e => setAmount(e.target.value)} /></label>
      <label>ملاحظة · اختياري<input maxLength={400} value={note} onChange={e => setNote(e.target.value)} /></label>
    </fieldset><button className="primary-button" disabled={busy}>{busy ? "جارٍ التحقق…" : draftLocked ? "التحقق من حفظ المسودة" : "حفظ مسودة المصروف"}</button></form></section>}
    {!creating&&!detailId&&<><div className="form-actions">{canRecord&&<button ref={newExpenseButton} className="primary-button" onClick={startDraft}>تسجيل مصروف</button>}</div><section className="filter-bar" aria-label="فلاتر المصروفات"><label>{paymentDates ? "من تاريخ السداد" : "من تاريخ"}<input type="date" value={range.from} onChange={e => { setRange({ ...range, from: e.target.value }); setPage(1); }} /></label><label>{paymentDates ? "إلى تاريخ السداد" : "إلى تاريخ"}<input type="date" value={range.to} onChange={e => { setRange({ ...range, to: e.target.value }); setPage(1); }} /></label><label>الفرع<select value={listBranchId} onChange={e => { setListBranchId(e.target.value); setPage(1); }}><option value="">كل الفروع</option>{options?.branches.map(b => <option key={b.id} value={b.id}>{b.arabicName}</option>)}</select></label>{listSportId && <p>الرياضة: {options?.sports.find(s => s.id === listSportId)?.arabicName ?? "الرياضة المحددة"}</p>}<label>الحالة<select value={listStatus} onChange={e => { setListStatus(e.target.value); setPage(1); }}><option value="">كل الحالات</option>{Object.entries(statuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label><label>الفئة<select value={listCategory} onChange={e=>{setListCategory(e.target.value);setPage(1);}}><option value="">كل الفئات</option>{Object.entries(categories).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label><label>السداد<select value={unpaidOnly ? "unpaid" : paidOnly?"paid":""} onChange={e => { setUnpaidOnly(e.target.value === "unpaid");setPaidOnly(e.target.value==="paid"); if (e.target.value === "unpaid") setPaymentDates(false); setPage(1); }}><option value="">الكل</option><option value="unpaid">غير مسدد</option><option value="paid">مسدد</option></select></label><button className="secondary-button" onClick={() => { setRange({ from: "", to: "" }); setListBranchId(""); setListStatus(""); setUnpaidOnly(false);setPaidOnly(false);setListCategory("");setPaymentDates(false);setListSportId("");setPage(1); }}>مسح الفلاتر</button></section>
    {listLoading ? <LoadingState /> : listIssue ? <ReadError message={listIssue} retry={reloadLists} /> : !items.length ? <EmptyState title="لا توجد مصروفات ضمن الفلاتر الحالية" /> : <DataTable label="المصروفات التشغيلية" rows={items} rowKey={r => r.id} serverPagination={{page:page-1,pageSize,total,onPageChange:p=>setPage(p+1),onPageSizeChange:n=>{setPageSize(n);setPage(1);}}} columns={[
      { key: "category", header: "المصروف", primary: true, render: r => <><strong>{categories[r.category] ?? r.category}</strong><small>{r.periodFrom} — {r.periodTo}</small>{paymentDates && r.paidOn && <small>سُدد في {r.paidOn}</small>}</> },
      { key: "amount", header: "المبلغ", render: r => formatMoneyAr(r.amount, r.currency) },
      { key: "status", header: "الحالة", render: r => statuses[r.status] ?? r.status },
      { key: "action", header: "المراجعة", render: r => <button className="secondary-button" onClick={() => openExpense(r.id)}>عرض المصروف</button> },
    ]} />}
    </>}
    {detailId && detailIssue && <ReadError message={detailIssue} retry={verification ? undefined : () => { setDetailIssue(""); setDetailRead(value => value + 1); }} />}
    {detailId && !selected && !detailIssue && <LoadingState />}
    {verification && detailId && <div className="form-actions"><button className="secondary-button" disabled={busy} onClick={() => void act(() => verify(detailId, verification))}>{verification === "approve" ? "التحقق من حالة الاعتماد" : "التحقق من حالة الترحيل"}</button></div>}
    {selected && <section className="detail-card" aria-label="مراجعة المصروف">
      <h2>{categories[selected.category] ?? selected.category} · {formatMoneyAr(selected.amount, selected.currency)}</h2>
      <dl className="expense-review-facts">
        <div><dt>الفرع ونطاق التكلفة</dt><dd>{branchName} · {costTarget}</dd></div>
        <div><dt>الحالة</dt><dd>{statuses[selected.status]}</dd></div>
        <div><dt>الاستحقاق</dt><dd>{formatDateAr(selected.periodFrom)}{selected.periodTo !== selected.periodFrom && ` إلى ${formatDateAr(selected.periodTo)}`}</dd></div>
        <div><dt>السداد</dt><dd>{selected.paidOn ? formatDateAr(selected.paidOn) : "غير مسدد"}</dd></div>
      </dl>
      {selected.note && <p>{selected.note}</p>}
      {canApprove && !verification && selected.status === "Draft" && <button className="primary-button" disabled={busy} onClick={() => void approve()}>اعتماد المصروف</button>}
      {canApprove && !verification && selected.status === "Approved" && !quote && <button className="secondary-button" disabled={busy} onClick={() => void preview()}>معاينة توزيع التكلفة</button>}
      {canApprove && quote && selected.status === "Approved" && <>
        <h3>معاينة الأنصبة</h3><p>{quote.method}</p>{quote.warning && <p role="alert">{quote.warning}</p>}
        <ul>{quote.shares.map(s => <li key={`${s.serviceKind}:${s.groupId ?? "unallocated"}`}>{shareLabel(s.serviceKind, s.groupId ? options?.groups.find(g => g.id === s.groupId)?.arabicName ?? "مجموعة محفوظة" : undefined)} — {formatMoneyAr(s.amount, quote.currency)}</li>)}</ul>
        <p>إجمالي التوزيع {formatMoneyAr(quote.total, quote.currency)}</p>
        {!verification && <button className="primary-button" disabled={busy} onClick={() => setConfirmPost(true)}>ترحيل المصروف</button>}
      </>}
      {selected.status === "Posted" && detail?.allocations && <>
        <h3>الأنصبة المحفوظة</h3>
        <p>الإجمالي المرحّل {formatMoneyAr(detail.allocationTotal, selected.currency)}</p>
        <ul>{detail.allocations.map(s => <li key={`${s.serviceKind}:${s.trainingGroupId ?? "unallocated"}`}>{shareLabel(s.serviceKind, s.trainingGroupId ? options?.groups.find(g => g.id === s.trainingGroupId)?.arabicName ?? "مجموعة محفوظة" : undefined)} — {formatMoneyAr(s.amount, selected.currency)}</li>)}</ul>
      </>}
    </section>}
    <ConfirmationDialog open={confirmPost && !!selected && !!quote} title="ترحيل المصروف؟" tone="primary"
      message={selected ? `${formatMoneyAr(selected.amount, selected.currency)} · ${branchName}. ${selected.paidOn ? `يدخل المبلغ في المصروفات المدفوعة بتاريخ ${formatDateAr(selected.paidOn)}.` : "يظل المبلغ ضمن المصروفات غير المسددة."}` : ""}
      confirmLabel="تأكيد الترحيل" busy={busy} onConfirm={() => void post()} onCancel={() => setConfirmPost(false)} />
    {!creating && !detailId && <details className="detail-card"><summary>كيف تُوزع تكلفة المصروف؟</summary><p>المصروفات التشغيلية اليدوية فقط، وليست رواتب أو تحصيلًا. المرفق المشترك يُقسم بين الخدمات حسب دقائق الحجز. مصروف الفرع أو الرياضة يُوزع بالتساوي على مجموعات التدريب ذات حصة أو حجز محفوظ في الفترة. إن لم توجد مجموعة يبقى غير موزع.</p></details>}
    <details className="detail-card" aria-label="أنصبة التكاليف المحفوظة"><summary>أنصبة المجموعات المرحّلة</summary><p>تصفية مجموعة واحدة تعرض نصيبها المحفوظ ولا تعيد تقسيم المصروف الكامل عليها.</p>
      <label>المجموعة<select value={allocationGroupId} onChange={e => setAllocationGroupId(e.target.value)}><option value="">كل المجموعات</option>{options?.groups.map(g => <option key={g.id} value={g.id}>{g.arabicName}</option>)}</select></label>
      {allocationIssue ? <ReadError message={allocationIssue} retry={reloadLists} /> : !allocationData ? <LoadingState /> : <><p role="status">{allocationData.count} نصيب · الإجمالي {formatMoneyAr(allocationData.total, currency)}</p>{allocationData.truncated && <p role="alert">تظهر أول 500 حركة فقط؛ الإجمالي يشمل جميع الحركات ضمن الفلتر.</p>}
        {!allocationData.items.length ? <EmptyState title="لا توجد أنصبة مرحّلة ضمن الفلتر" /> : <DataTable label="أنصبة تكلفة المجموعات" rows={allocationData.items} rowKey={r => `${r.expenseId}:${r.serviceKind}:${r.trainingGroupId ?? "unallocated"}`} columns={[
          { key: "group", header: "الخدمة / المجموعة", primary: true, render: r => shareLabel(r.serviceKind, r.trainingGroupId ? options?.groups.find(g => g.id === r.trainingGroupId)?.arabicName ?? "مجموعة محفوظة" : undefined) },
          { key: "period", header: "فترة الاستحقاق", render: r => `${r.periodFrom} — ${r.periodTo}` },
          { key: "share", header: "النصيب المحفوظ", render: r => formatMoneyAr(r.amount, r.currency) },
          { key: "original", header: "أصل المصروف", render: r => formatMoneyAr(r.expenseAmount, r.currency) },
        ]} />}</>}
    </details>
  </div>;
}
